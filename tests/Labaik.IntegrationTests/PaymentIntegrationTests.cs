using System.Net;
using System.Net.Http.Json;
using Labaik.Application.Payments;
using Labaik.Domain.Bookings;
using Labaik.FakeGateway;
using Labaik.Infrastructure.Payments;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Labaik.IntegrationTests;

[Trait("Category", "Docker")]
public sealed class PaymentIntegrationTests(PaymentDatabase database) : IClassFixture<PaymentDatabase>, IAsyncLifetime
{
    public Task InitializeAsync() => database.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;
    private async Task<(Guid Booking, Guid Owner, Guid Checkout)> PaidGateway(PaymentTestSystem system)
    {
        var seed = await database.SeedAsync();
        var attempt = await system.Checkout(seed.BookingId, seed.OwnerId, "first");
        Assert.NotNull(attempt.CheckoutId); await system.Pay(attempt.CheckoutId.Value);
        return (seed.BookingId, seed.OwnerId, attempt.CheckoutId.Value);
    }
    [Fact]
    public async Task Two_hundred_double_taps_and_pay_retries_produce_one_charge_capture_and_paid_event()
    {
        await using var system = new PaymentTestSystem(database); var seed = await database.SeedAsync();
        var attempts = await Task.WhenAll(Enumerable.Range(0, 200).Select(i => system.Checkout(seed.BookingId, seed.OwnerId, i % 2 == 0 ? "same-key" : $"tap-{i}")));
        Assert.Single(attempts.Select(x => x.Id).Distinct()); Assert.Single(attempts.Select(x => x.CheckoutId).Distinct());
        var id = attempts[0].CheckoutId!.Value;
        await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => system.Pay(id)));
        var body = await system.Event(id, "Succeeded");
        var replies = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => system.Send(body)));
        Assert.All(replies, x => Assert.Equal(HttpStatusCode.Accepted, x.StatusCode)); foreach (var reply in replies) reply.Dispose();
        // Also deduplicate semantic success with distinct provider event IDs, across two worker instances.
        var notice = system.Codec.VerifyAndDecrypt(body, system.Codec.Sign(body));
        for (var i = 0; i < 5; i++) { using var reply = await system.Send(system.Codec.Encrypt(notice with { EventId = Guid.NewGuid() })); }
        await Task.WhenAll(system.Drain(WorkKind.Inbox), system.Drain(WorkKind.Inbox));
        await using var db = database.App(); await using var gateway = database.Gateway();
        Assert.Equal(1, await db.PaymentAttempts.CountAsync()); Assert.Equal(1, await gateway.Checkouts.CountAsync());
        Assert.Equal(1, await gateway.Charges.CountAsync()); Assert.Equal(1, await db.CapturedPayments.CountAsync());
        Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.Type == "BookingPaid"));
        Assert.Equal(BookingState.Paid, (await db.Bookings.SingleAsync()).State);
        await system.Drain(WorkKind.Outbox); Assert.Equal(1, await system.ReceiptCount());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lost_create_response_or_timeout_reuses_durable_attempt_after_restart(bool timeout)
    {
        var seed = await database.SeedAsync(); Guid attemptId;
        await using (var original = new PaymentTestSystem(database))
        {
            if (timeout) { original.Faults.CreateDelayMs = 500; original.GatewayClient.Timeout = TimeSpan.FromMilliseconds(100); }
            else original.Faults.FailCreateResponse = true;
            var result = await original.Checkout(seed.BookingId, seed.OwnerId, "lost-response"); attemptId = result.Id;
            Assert.Equal(AttemptStatus.Unknown, result.Status);
            await using var gateway = database.Gateway(); Assert.Equal(1, await gateway.Checkouts.CountAsync());
        }
        await using var restarted = new PaymentTestSystem(database);
        var recovered = await restarted.Checkout(seed.BookingId, seed.OwnerId, "another-double-tap");
        Assert.Equal(attemptId, recovered.Id); Assert.NotNull(recovered.CheckoutId);
        await restarted.Pay(recovered.CheckoutId.Value); await restarted.Pump(WorkKind.RecoverCheckout);
        await using var db = database.App(); await using var remote = database.Gateway();
        Assert.Equal(1, await remote.Checkouts.CountAsync()); Assert.Equal(1, await remote.Charges.CountAsync());
        Assert.Equal(1, await db.CapturedPayments.CountAsync());
    }
    [Fact]
    public async Task Crash_after_charge_commit_is_recovered_without_another_charge()
    {
        var seed = await database.SeedAsync(); Guid checkout;
        await using (var original = new PaymentTestSystem(database))
        {
            checkout = (await original.Checkout(seed.BookingId, seed.OwnerId, "charge-crash")).CheckoutId!.Value;
            original.Faults.FailPayResponse = true;
            using var lost = await original.GatewayClient.PostAsJsonAsync($"/checkouts/{checkout}/pay", new SimulatedPayment("Succeeded"));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, lost.StatusCode);
            await using var db = database.App(); Assert.Equal(0, await db.CapturedPayments.CountAsync());
        }
        await using var restarted = new PaymentTestSystem(database);
        await restarted.Pay(checkout); await restarted.Pump(WorkKind.RecoverCheckout);
        await using var local = database.App(); await using var gateway = database.Gateway();
        Assert.Equal(1, await gateway.Charges.CountAsync()); Assert.Equal(1, await local.CapturedPayments.CountAsync());
        Assert.Equal(BookingState.Paid, (await local.Bookings.SingleAsync()).State);
    }
    [Fact]
    public async Task Webhook_before_create_response_and_redirect_does_not_regress_success()
    {
        using var barrier = new ResponseBarrier(); await using var system = new PaymentTestSystem(database, barrier);
        var seed = await database.SeedAsync(); var checkoutTask = system.Checkout(seed.BookingId, seed.OwnerId, "early-webhook");
        await barrier.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await using var gateway = database.Gateway(); var remote = await gateway.Checkouts.SingleAsync();
            await system.Pay(remote.Id);
            using var callback = await system.Send(await system.Event(remote.Id, "Succeeded"));
            Assert.Equal(HttpStatusCode.Accepted, callback.StatusCode); await system.Drain(WorkKind.Inbox);
        }
        finally { barrier.Release.TrySetResult(); }
        Assert.Equal(AttemptStatus.Succeeded, (await checkoutTask).Status);
        var redirectRead = await system.Checkout(seed.BookingId, seed.OwnerId, "early-webhook");
        Assert.Equal(AttemptStatus.Succeeded, redirectRead.Status);
        await using var db = database.App(); Assert.Equal(1, await db.CapturedPayments.CountAsync());
        Assert.Equal(1, await db.OutboxMessages.CountAsync());
    }
    [Theory]
    [InlineData("Pending")]
    [InlineData("Failed")]
    public async Task Old_events_cannot_undo_a_verified_success(string oldStatus)
    {
        await using var system = new PaymentTestSystem(database); var seed = await database.SeedAsync();
        var id = (await system.Checkout(seed.BookingId, seed.OwnerId, "order")).CheckoutId!.Value;
        using var pending = await system.Send(await system.Event(id, "Pending")); await system.Drain(WorkKind.Inbox);
        await using (var before = database.App()) Assert.Equal(0, await before.CapturedPayments.CountAsync());
        await system.Pay(id); var body = await system.Event(id, "Succeeded"); using var success = await system.Send(body); await system.Drain(WorkKind.Inbox);
        var older = system.Codec.VerifyAndDecrypt(body, system.Codec.Sign(body)) with { EventId = Guid.NewGuid(), Status = oldStatus };
        using var old = await system.Send(system.Codec.Encrypt(older)); await system.Drain(WorkKind.Inbox);
        await using var db = database.App(); Assert.Equal(BookingState.Paid, (await db.Bookings.SingleAsync()).State);
        Assert.Equal(1, await db.CapturedPayments.CountAsync()); Assert.Equal(1, await db.OutboxMessages.CountAsync());
    }
    [Fact]
    public async Task Gateway_query_outage_keeps_inbox_retryable_without_marking_paid()
    {
        await using var system = new PaymentTestSystem(database); var payment = await PaidGateway(system);
        system.Faults.QueryUnavailable = true; using var received = await system.Send(await system.Event(payment.Checkout, "Succeeded"));
        await system.Pump(WorkKind.Inbox);
        await using (var check = database.App())
        {
            Assert.Equal(0, await check.CapturedPayments.CountAsync()); Assert.Equal(InboxStatus.Pending, (await check.WebhookInbox.SingleAsync()).Status);
            Assert.Equal(WorkStatus.Pending, (await check.JobRuns.SingleAsync(x => x.Kind == WorkKind.Inbox)).Status);
        }
        system.Faults.QueryUnavailable = false; await database.DueAsync(WorkKind.Inbox); await system.Pump(WorkKind.Inbox);
        await using var db = database.App(); Assert.Equal(1, await db.CapturedPayments.CountAsync());
    }
    [Theory]
    [InlineData("amount")]
    [InlineData("currency")]
    [InlineData("booking")]
    [InlineData("transaction")]
    public async Task Authoritative_gateway_mismatch_never_marks_paid(string mismatch)
    {
        GatewaySnapshot Change(GatewaySnapshot s) => mismatch switch
        {
            "amount" => s with { AmountMinor = s.AmountMinor + 1 }, "currency" => s with { Currency = "USD" },
            "booking" => s with { BookingId = Guid.NewGuid() }, _ => s with { TransactionId = null }
        };
        await using var system = new PaymentTestSystem(database, decorate: inner => new SnapshotOverride(inner, Change));
        var payment = await PaidGateway(system); using var received = await system.Send(await system.Event(payment.Checkout, "Succeeded"));
        await system.Drain(WorkKind.Inbox);
        await using var db = database.App(); Assert.Equal(0, await db.CapturedPayments.CountAsync());
        Assert.Equal(BookingState.PendingPayment, (await db.Bookings.SingleAsync()).State);
        Assert.Equal(InboxStatus.Rejected, (await db.WebhookInbox.SingleAsync()).Status);
        Assert.Equal("PaymentMismatch", (await db.OutboxMessages.SingleAsync()).Type);
    }
    [Fact]
    public async Task Signed_callback_amount_mismatch_and_event_ID_payload_reuse_are_quarantined()
    {
        await using var system = new PaymentTestSystem(database); var payment = await PaidGateway(system);
        var body = await system.Event(payment.Checkout, "Succeeded"); var notice = system.Codec.VerifyAndDecrypt(body, system.Codec.Sign(body));
        using var first = await system.Send(body);
        using var altered = await system.Send(system.Codec.Encrypt(notice with { AmountMinor = 1001 }));
        Assert.Equal(HttpStatusCode.Conflict, altered.StatusCode); await system.Drain(WorkKind.Inbox);
        await using var db = database.App(); Assert.True((await db.WebhookInbox.SingleAsync()).HasConflict);
        Assert.Equal(0, await db.CapturedPayments.CountAsync());
    }
    [Theory]
    [InlineData("signature")]
    [InlineData("tag")]
    [InlineData("large")]
    [InlineData("fields")]
    public async Task Invalid_webhooks_are_rejected_before_persistence(string problem)
    {
        await using var system = new PaymentTestSystem(database); var payment = await PaidGateway(system);
        var body = await system.Event(payment.Checkout, "Succeeded"); string? signature = null; var expected = HttpStatusCode.Unauthorized;
        if (problem == "signature") { signature = system.Codec.Sign(body); body[^1] ^= 1; }
        if (problem == "tag") body[13] ^= 1; // Correct HMAC over an invalid GCM tag must still fail.
        if (problem == "large") { body = new byte[WebhookCodec.MaxBodyBytes + 1]; expected = HttpStatusCode.RequestEntityTooLarge; }
        if (problem == "fields")
        { var notice = system.Codec.VerifyAndDecrypt(body, system.Codec.Sign(body)); body = system.Codec.Encrypt(notice with { EventId = Guid.Empty }); expected = HttpStatusCode.BadRequest; }
        using var result = await system.Send(body, signature); Assert.Equal(expected, result.StatusCode);
        await using var db = database.App(); Assert.Equal(0, await db.WebhookInbox.CountAsync());
    }
    [Fact]
    public async Task Paid_capture_and_outbox_roll_back_together_when_outbox_insert_fails()
    {
        await using var system = new PaymentTestSystem(database); var payment = await PaidGateway(system);
        using var notice = await system.Send(await system.Event(payment.Checkout, "Succeeded"));
        await using (var admin = database.App(true)) await admin.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION test_support.reject_outbox() RETURNS trigger LANGUAGE plpgsql AS $$BEGIN RAISE EXCEPTION 'test storage failure'; END$$;
            CREATE TRIGGER reject_payment_outbox BEFORE INSERT ON app."OutboxMessages" FOR EACH ROW EXECUTE FUNCTION test_support.reject_outbox();
            """);
        await system.Pump(WorkKind.Inbox);
        await using (var check = database.App())
        {
            Assert.Equal(0, await check.CapturedPayments.CountAsync()); Assert.Equal(0, await check.OutboxMessages.CountAsync());
            Assert.Equal(BookingState.PendingPayment, (await check.Bookings.SingleAsync()).State);
            Assert.Equal(InboxStatus.Pending, (await check.WebhookInbox.SingleAsync()).Status);
        }
        await using (var admin = database.App(true)) await admin.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_payment_outbox ON app.\"OutboxMessages\"");
        await database.DueAsync(WorkKind.Inbox); await system.Pump(WorkKind.Inbox);
        await using var db = database.App(); Assert.Equal(1, await db.CapturedPayments.CountAsync()); Assert.Equal(1, await db.OutboxMessages.CountAsync());
    }
    [Fact]
    public async Task Worker_crash_releases_work_after_lease_and_stale_owner_cannot_commit()
    {
        await using var system = new PaymentTestSystem(database); var payment = await PaidGateway(system);
        using var received = await system.Send(await system.Event(payment.Checkout, "Succeeded")); await system.Drain(WorkKind.Inbox);
        WorkClaim first;
        await using (var scope = system.Services.CreateAsyncScope()) first = (await scope.ServiceProvider.GetRequiredService<WorkQueue>().ClaimAsync(WorkKind.Outbox, default))!;
        await using (var db = database.App()) await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.\"JobRuns\" SET \"LeaseUntil\"=clock_timestamp()-interval '1 second' WHERE \"Id\"={first.Id}");
        await using (var scope = system.Services.CreateAsyncScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<WorkQueue>();
            var second = await queue.ClaimAsync(WorkKind.Outbox, default);
            Assert.NotNull(second); Assert.Equal(first.Id, second.Id); Assert.True(second.Version > first.Version);
            Assert.Null(await queue.LockAsync(first, default));
        }
    }
    [Fact]
    public async Task Lost_publish_ack_retries_same_event_and_receiver_deduplicates_it()
    {
        await using var system = new PaymentTestSystem(database); var payment = await PaidGateway(system);
        using var received = await system.Send(await system.Event(payment.Checkout, "Succeeded")); await system.Drain(WorkKind.Inbox);
        system.AfterPublish = async message =>
        {
            await using var db = database.App();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.\"JobRuns\" SET \"LeaseUntil\"=clock_timestamp()-interval '1 second' WHERE \"EntityId\"={message.Id}");
            system.AfterPublish = null;
        };
        await system.Pump(WorkKind.Outbox);
        await using (var db = database.App()) Assert.Null((await db.OutboxMessages.SingleAsync()).PublishedAt);
        await system.Pump(WorkKind.Outbox);
        Assert.Equal(2, system.DeliveryCalls); Assert.Equal(1, await system.ReceiptCount());
        await using var check = database.App(); Assert.NotNull((await check.OutboxMessages.SingleAsync()).PublishedAt);
    }
    [Fact]
    public async Task Publisher_retries_backoff_then_dead_letters_without_false_success()
    {
        await using var system = new PaymentTestSystem(database); var payment = await PaidGateway(system);
        using var received = await system.Send(await system.Event(payment.Checkout, "Succeeded")); await system.Drain(WorkKind.Inbox);
        system.RejectPublication = true; await system.Pump(WorkKind.Outbox); await database.DueAsync(WorkKind.Outbox); await system.Pump(WorkKind.Outbox);
        await using var db = database.App(); var job = await db.JobRuns.SingleAsync(x => x.Kind == WorkKind.Outbox);
        Assert.Equal(WorkStatus.DeadLetter, job.Status); Assert.Equal(2, job.Attempts);
        Assert.Null((await db.OutboxMessages.SingleAsync()).PublishedAt); Assert.Equal(0, await system.ReceiptCount());
    }
    [Fact]
    public async Task Gateway_delivery_retries_exact_wire_bytes_and_duplicate_callbacks_credit_once()
    {
        await using var system = new PaymentTestSystem(database); var payment = await PaidGateway(system); system.Faults.DeliveryCopies = 2;
        for (var i = 0; i < 2; i++)
        { await using var gateway = database.Gateway(); Assert.True(await new GatewayDelivery(gateway, system.Webhooks, system.Codec, system.Faults).RunOnceAsync()); }
        await system.Drain(WorkKind.Inbox);
        await using var db = database.App(); Assert.Equal(2, await db.WebhookInbox.CountAsync()); Assert.Equal(1, await db.CapturedPayments.CountAsync());
    }
    [Fact]
    public async Task Late_payment_keeps_cancellation_and_records_compensation_intents()
    {
        await using var system = new PaymentTestSystem(database); var seed = await database.SeedAsync();
        var attempt = await system.Checkout(seed.BookingId, seed.OwnerId, "cancelled");
        await using (var db = database.App())
        { var booking = await db.Bookings.SingleAsync(); Assert.True(booking.TryTransition(BookingState.Cancelled, ActorRole.Customer).IsSuccess); await db.SaveChangesAsync(); }
        await system.Pay(attempt.CheckoutId!.Value); using var received = await system.Send(await system.Event(attempt.CheckoutId.Value, "Succeeded")); await system.Drain(WorkKind.Inbox);
        await using var check = database.App(); Assert.Equal(BookingState.Cancelled, (await check.Bookings.SingleAsync()).State);
        Assert.Equal(1, await check.CapturedPayments.CountAsync()); Assert.Equal(2, await check.OutboxMessages.CountAsync());
        Assert.Contains(await check.OutboxMessages.ToListAsync(), x => x.Type == "RefundRequired");
        Assert.DoesNotContain(await check.OutboxMessages.ToListAsync(), x => x.Type == "BookingPaid");
    }
    [Fact]
    public async Task Only_authoritative_final_failure_allows_a_new_attempt_and_failed_checkout_cannot_charge()
    {
        await using var system = new PaymentTestSystem(database); var seed = await database.SeedAsync();
        var first = await system.Checkout(seed.BookingId, seed.OwnerId, "first"); await system.Pay(first.CheckoutId!.Value, "Failed");
        await system.Pump(WorkKind.RecoverCheckout);
        Assert.Equal(AttemptStatus.Failed, (await system.Checkout(seed.BookingId, seed.OwnerId, "first")).Status);
        using var illegal = await system.GatewayClient.PostAsJsonAsync($"/checkouts/{first.CheckoutId}/pay", new SimulatedPayment("Succeeded"));
        Assert.Equal(HttpStatusCode.Conflict, illegal.StatusCode);
        var second = await system.Checkout(seed.BookingId, seed.OwnerId, "second"); Assert.NotEqual(first.Id, second.Id);
        await system.Pay(second.CheckoutId!.Value); await system.Pump(WorkKind.RecoverCheckout);
        await using var db = database.App(); await using var gateway = database.Gateway();
        Assert.Equal(2, await db.PaymentAttempts.CountAsync()); Assert.Equal(1, await gateway.Charges.CountAsync());
    }
    [Fact]
    public async Task Stale_pending_gateway_response_cannot_reopen_a_final_failed_attempt()
    {
        QueryBarrier barrier = null!;
        await using var system = new PaymentTestSystem(database, decorate: inner => barrier = new QueryBarrier(inner));
        var seed = await database.SeedAsync(); var attempt = await system.Checkout(seed.BookingId, seed.OwnerId, "stale-query");
        using var webhook = await system.Send(await system.Event(attempt.CheckoutId!.Value, "Pending"));
        var staleWork = system.Pump(WorkKind.Inbox);
        await barrier.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await system.Pay(attempt.CheckoutId.Value, "Failed");
            await system.Pump(WorkKind.RecoverCheckout);
        }
        finally { barrier.Release.TrySetResult(); }
        await staleWork;
        await using var db = database.App(); Assert.Equal(AttemptStatus.Failed, (await db.PaymentAttempts.SingleAsync()).Status);
        Assert.Equal(0, await db.CapturedPayments.CountAsync());
    }
    [Fact]
    public async Task Gateway_redirect_does_not_mark_paid_before_verified_callback()
    {
        await using var system = new PaymentTestSystem(database); var seed = await database.SeedAsync();
        var checkout = (await system.Checkout(seed.BookingId, seed.OwnerId, "redirect")).CheckoutId!.Value;
        using var response = await system.GatewayClient.PostAsJsonAsync($"/checkouts/{checkout}/pay", new SimulatedPayment("Succeeded", true));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"https://core.example.invalid/payments/return?checkoutId={checkout}", response.Headers.Location!.ToString());
        await using (var before = database.App()) Assert.Equal(0, await before.CapturedPayments.CountAsync());
        using var webhook = await system.Send(await system.Event(checkout, "Succeeded")); await system.Drain(WorkKind.Inbox);
        await using var after = database.App(); Assert.Equal(BookingState.Paid, (await after.Bookings.SingleAsync()).State);
    }
    [Fact]
    public async Task Unauthenticated_gateway_requests_cannot_create_or_charge_checkouts()
    {
        await using var system = new PaymentTestSystem(database);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/checkouts")
        { Content = JsonContent.Create(new CheckoutCommand(Guid.NewGuid(), "attacker-key", 1000, "SAR")) };
        request.Headers.Add("X-Gateway-Key", "wrong-key");
        using var response = await system.GatewayClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using var gateway = database.Gateway(); Assert.Equal(0, await gateway.Checkouts.CountAsync());
    }
    [Fact]
    public async Task Changed_checkout_payload_and_other_owners_conflict_and_production_routes_stay_disabled()
    {
        await using var system = new PaymentTestSystem(database); var seed = await database.SeedAsync();
        await Assert.ThrowsAsync<PaymentConflictException>(() => system.Checkout(seed.BookingId, Guid.NewGuid(), "owner"));
        var attempt = await system.Checkout(seed.BookingId, seed.OwnerId, "same");
        await using (var db = database.App())
        {
            var row = await db.PaymentAttempts.SingleAsync();
            using var remoteConflict = await system.GatewayClient.PostAsJsonAsync("/checkouts", new CheckoutCommand(seed.BookingId, row.GatewayKey, 1001, "SAR"));
            Assert.Equal(HttpStatusCode.Conflict, remoteConflict.StatusCode);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.\"Bookings\" SET \"BaseMinor\"=1001,\"TotalMinor\"=1001 WHERE \"Id\"={seed.BookingId}");
        }
        await Assert.ThrowsAsync<PaymentConflictException>(() => system.Checkout(seed.BookingId, seed.OwnerId, "same"));
        await using var api = new WebApplicationFactory<ApiProgram>().WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:Database", database.AppConnection));
        using var client = api.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/payments/checkout", new { bookingId = seed.BookingId })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/webhooks/gateway", new { })).StatusCode);
    }
}
