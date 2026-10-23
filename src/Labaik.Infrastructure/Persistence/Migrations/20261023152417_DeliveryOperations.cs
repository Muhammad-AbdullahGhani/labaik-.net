using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Labaik.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeliveryOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_JobRuns_Kind",
                schema: "app",
                table: "JobRuns");

            migrationBuilder.AddColumn<string>(
                name: "ResultJson",
                schema: "app",
                table: "JobRuns",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeviceTokens",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    InvalidatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Devices_User",
                        column: x => x.UserId,
                        principalSchema: "app",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_JobRuns_Kind",
                schema: "app",
                table: "JobRuns",
                sql: "\"Kind\" IN ('RecoverCheckout','Inbox','Outbox','Refund','Assign','ExpireOffer','Reconcile','Heartbeat')");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceTokens_UserId",
                schema: "app",
                table: "DeviceTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UX_Devices_Token",
                schema: "app",
                table: "DeviceTokens",
                column: "Token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceTokens",
                schema: "app");

            migrationBuilder.DropCheckConstraint(
                name: "CK_JobRuns_Kind",
                schema: "app",
                table: "JobRuns");

            migrationBuilder.DropColumn(
                name: "ResultJson",
                schema: "app",
                table: "JobRuns");

            migrationBuilder.AddCheckConstraint(
                name: "CK_JobRuns_Kind",
                schema: "app",
                table: "JobRuns",
                sql: "\"Kind\" IN ('RecoverCheckout','Inbox','Outbox','Refund','Assign','ExpireOffer')");
        }
    }
}
