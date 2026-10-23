using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Labaik.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Payments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JobRuns",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ScheduledFor = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    AvailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeaseOwner = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClaimVersion = table.Column<long>(type: "bigint", nullable: false),
                    LastError = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobRuns", x => x.Id);
                    table.CheckConstraint("CK_JobRuns_Counters", "\"Attempts\" >= 0 AND \"ClaimVersion\" >= 0");
                    table.CheckConstraint("CK_JobRuns_Kind", "\"Kind\" IN ('RecoverCheckout','Inbox','Outbox')");
                    table.CheckConstraint("CK_JobRuns_Lease", "\"Status\" <> 'Processing' OR (\"LeaseOwner\" IS NOT NULL AND \"LeaseUntil\" IS NOT NULL)");
                    table.CheckConstraint("CK_JobRuns_State", "\"Status\" IN ('Pending','Processing','Completed','DeadLetter')");
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    DedupKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Outbox_Bookings",
                        column: x => x.BookingId,
                        principalSchema: "app",
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentAttempts",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    RequestKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GatewayKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CheckoutId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentAttempts", x => x.Id);
                    table.CheckConstraint("CK_PaymentAttempts_Money", "\"AmountMinor\" > 0 AND \"Currency\"='SAR'");
                    table.CheckConstraint("CK_PaymentAttempts_Sequence", "\"Sequence\" > 0");
                    table.CheckConstraint("CK_PaymentAttempts_Status", "\"Status\" IN ('Creating','Pending','Unknown','Succeeded','Failed')");
                    table.ForeignKey(
                        name: "FK_Attempts_Bookings",
                        column: x => x.BookingId,
                        principalSchema: "app",
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WebhookInbox",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Signature = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EncryptedBody = table.Column<byte[]>(type: "bytea", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    HasConflict = table.Column<bool>(type: "boolean", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookInbox", x => x.Id);
                    table.CheckConstraint("CK_Inbox_Status", "\"Status\" IN ('Pending','Processed','Rejected')");
                });

            migrationBuilder.CreateTable(
                name: "CapturedPayments",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    GatewayTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapturedPayments", x => x.Id);
                    table.CheckConstraint("CK_CapturedPayments_Money", "\"AmountMinor\" > 0 AND \"Currency\"='SAR'");
                    table.ForeignKey(
                        name: "FK_Captures_Attempts",
                        column: x => x.AttemptId,
                        principalSchema: "app",
                        principalTable: "PaymentAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Captures_Bookings",
                        column: x => x.BookingId,
                        principalSchema: "app",
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Captures_Attempt",
                schema: "app",
                table: "CapturedPayments",
                column: "AttemptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Captures_Booking",
                schema: "app",
                table: "CapturedPayments",
                column: "BookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Captures_Transaction",
                schema: "app",
                table: "CapturedPayments",
                column: "GatewayTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobRuns_Due",
                schema: "app",
                table: "JobRuns",
                columns: new[] { "Status", "AvailableAt", "LeaseUntil" });

            migrationBuilder.CreateIndex(
                name: "UX_JobRuns_Schedule",
                schema: "app",
                table: "JobRuns",
                columns: new[] { "JobName", "ScheduledFor" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_BookingId",
                schema: "app",
                table: "OutboxMessages",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "UX_Outbox_DedupKey",
                schema: "app",
                table: "OutboxMessages",
                column: "DedupKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Attempts_Checkout",
                schema: "app",
                table: "PaymentAttempts",
                column: "CheckoutId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Attempts_GatewayKey",
                schema: "app",
                table: "PaymentAttempts",
                column: "GatewayKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Attempts_LiveBooking",
                schema: "app",
                table: "PaymentAttempts",
                column: "BookingId",
                unique: true,
                filter: "\"Status\" IN ('Creating','Pending','Unknown','Succeeded')");

            migrationBuilder.CreateIndex(
                name: "UX_Attempts_RequestKey",
                schema: "app",
                table: "PaymentAttempts",
                columns: new[] { "BookingId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Attempts_Sequence",
                schema: "app",
                table: "PaymentAttempts",
                columns: new[] { "BookingId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Inbox_ProviderEvent",
                schema: "app",
                table: "WebhookInbox",
                columns: new[] { "Provider", "EventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CapturedPayments",
                schema: "app");

            migrationBuilder.DropTable(
                name: "JobRuns",
                schema: "app");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "app");

            migrationBuilder.DropTable(
                name: "WebhookInbox",
                schema: "app");

            migrationBuilder.DropTable(
                name: "PaymentAttempts",
                schema: "app");
        }
    }
}
