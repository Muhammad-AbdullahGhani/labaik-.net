using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Labaik.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CapacityRefunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_JobRuns_Kind",
                schema: "app",
                table: "JobRuns");

            migrationBuilder.CreateTable(
                name: "RefundRequests",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CapturedPaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    GatewayKey = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    GatewayRefundId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefundRequests", x => x.Id);
                    table.CheckConstraint("CK_Refunds_Status", "\"Status\" IN ('Pending','Succeeded')");
                    table.ForeignKey(
                        name: "FK_Refunds_Captures",
                        column: x => x.CapturedPaymentId,
                        principalSchema: "app",
                        principalTable: "CapturedPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_JobRuns_Kind",
                schema: "app",
                table: "JobRuns",
                sql: "\"Kind\" IN ('RecoverCheckout','Inbox','Outbox','Refund')");

            migrationBuilder.CreateIndex(
                name: "UX_Refunds_Capture",
                schema: "app",
                table: "RefundRequests",
                column: "CapturedPaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Refunds_Key",
                schema: "app",
                table: "RefundRequests",
                column: "GatewayKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Refunds_ProviderId",
                schema: "app",
                table: "RefundRequests",
                column: "GatewayRefundId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RefundRequests",
                schema: "app");

            migrationBuilder.DropCheckConstraint(
                name: "CK_JobRuns_Kind",
                schema: "app",
                table: "JobRuns");

            migrationBuilder.AddCheckConstraint(
                name: "CK_JobRuns_Kind",
                schema: "app",
                table: "JobRuns",
                sql: "\"Kind\" IN ('RecoverCheckout','Inbox','Outbox')");
        }
    }
}
