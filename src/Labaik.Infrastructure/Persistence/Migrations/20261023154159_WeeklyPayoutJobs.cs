using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Labaik.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WeeklyPayoutJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_JobRuns_Kind",
                schema: "app",
                table: "JobRuns");

            migrationBuilder.AddCheckConstraint(
                name: "CK_JobRuns_Kind",
                schema: "app",
                table: "JobRuns",
                sql: "\"Kind\" IN ('RecoverCheckout','Inbox','Outbox','Refund','Assign','ExpireOffer','Reconcile','Heartbeat','PayoutSweep')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_JobRuns_Kind",
                schema: "app",
                table: "JobRuns");

            migrationBuilder.AddCheckConstraint(
                name: "CK_JobRuns_Kind",
                schema: "app",
                table: "JobRuns",
                sql: "\"Kind\" IN ('RecoverCheckout','Inbox','Outbox','Refund','Assign','ExpireOffer','Reconcile','Heartbeat')");
        }
    }
}
