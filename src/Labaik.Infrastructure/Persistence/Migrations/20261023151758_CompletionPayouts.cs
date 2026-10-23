using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Labaik.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompletionPayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Earnings",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    PerformerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Earnings", x => x.Id);
                    table.CheckConstraint("CK_Earnings_Money", "\"AmountMinor\">0 AND \"Currency\"='SAR'");
                    table.CheckConstraint("CK_Earnings_Status", "\"Status\" IN ('Available','Reserved','Paid')");
                    table.ForeignKey(
                        name: "FK_Earnings_Booking",
                        column: x => x.BookingId,
                        principalSchema: "app",
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Earnings_Performer",
                        column: x => x.PerformerId,
                        principalSchema: "app",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayoutBatches",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<string>(type: "text", nullable: false),
                    PerformerId = table.Column<Guid>(type: "uuid", nullable: false),
                    WeekStart = table.Column<DateOnly>(type: "date", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    TotalMinor = table.Column<long>(type: "bigint", nullable: false),
                    ExportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    BankReference = table.Column<string>(type: "text", nullable: true),
                    DestinationIban = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayoutBatches", x => x.Id);
                    table.CheckConstraint("CK_Batches_Money", "\"TotalMinor\">0 AND \"Currency\"='SAR'");
                    table.CheckConstraint("CK_Batches_Reference", "\"Status\"='Pending' OR \"BankReference\" IS NOT NULL");
                    table.CheckConstraint("CK_Batches_Status", "\"Status\" IN ('Pending','Paid','Failed')");
                    table.ForeignKey(
                        name: "FK_Batches_Performer",
                        column: x => x.PerformerId,
                        principalSchema: "app",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayoutItems",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    EarningId = table.Column<Guid>(type: "uuid", nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayoutItems", x => x.Id);
                    table.CheckConstraint("CK_Items_Money", "\"AmountMinor\">0 AND \"Currency\"='SAR'");
                    table.CheckConstraint("CK_Items_Status", "\"Status\" IN ('Pending','Paid','Failed')");
                    table.ForeignKey(
                        name: "FK_Items_Batch",
                        column: x => x.BatchId,
                        principalSchema: "app",
                        principalTable: "PayoutBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Items_Earning",
                        column: x => x.EarningId,
                        principalSchema: "app",
                        principalTable: "Earnings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Earnings_Pool",
                schema: "app",
                table: "Earnings",
                columns: new[] { "PerformerId", "Status", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_Earnings_Booking",
                schema: "app",
                table: "Earnings",
                column: "BookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Batches_Week",
                schema: "app",
                table: "PayoutBatches",
                columns: new[] { "WeekStart", "PerformerId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayoutBatches_PerformerId",
                schema: "app",
                table: "PayoutBatches",
                column: "PerformerId");

            migrationBuilder.CreateIndex(
                name: "UX_Batches_BankReference",
                schema: "app",
                table: "PayoutBatches",
                column: "BankReference",
                unique: true,
                filter: "\"BankReference\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Batches_RequestKey",
                schema: "app",
                table: "PayoutBatches",
                column: "RequestKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Items_BatchEarning",
                schema: "app",
                table: "PayoutItems",
                columns: new[] { "BatchId", "EarningId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Items_ClaimedEarning",
                schema: "app",
                table: "PayoutItems",
                column: "EarningId",
                unique: true,
                filter: "\"Status\" IN ('Pending','Paid')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayoutItems",
                schema: "app");

            migrationBuilder.DropTable(
                name: "PayoutBatches",
                schema: "app");

            migrationBuilder.DropTable(
                name: "Earnings",
                schema: "app");
        }
    }
}
