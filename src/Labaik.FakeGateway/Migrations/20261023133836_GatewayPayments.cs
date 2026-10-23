using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Labaik.FakeGateway.Migrations
{
    /// <inheritdoc />
    public partial class GatewayPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "fake_gateway");

            migrationBuilder.CreateTable(
                name: "Checkouts",
                schema: "fake_gateway",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GatewayCheckouts", x => x.Id);
                    table.CheckConstraint("CK_GatewayCheckouts_Capture", "(\"Status\"='Succeeded') = (\"TransactionId\" IS NOT NULL)");
                    table.CheckConstraint("CK_GatewayCheckouts_Money", "\"AmountMinor\" > 0 AND \"Currency\"='SAR'");
                    table.CheckConstraint("CK_GatewayCheckouts_Status", "\"Status\" IN ('Pending','Succeeded','Failed')");
                });

            migrationBuilder.CreateTable(
                name: "Charges",
                schema: "fake_gateway",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckoutId = table.Column<Guid>(type: "uuid", nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GatewayCharges", x => x.Id);
                    table.CheckConstraint("CK_GatewayCharges_Money", "\"AmountMinor\" > 0 AND \"Currency\"='SAR'");
                    table.ForeignKey(
                        name: "FK_GatewayCharges_Checkout",
                        column: x => x.CheckoutId,
                        principalSchema: "fake_gateway",
                        principalTable: "Checkouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Events",
                schema: "fake_gateway",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckoutId = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<byte[]>(type: "bytea", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    AvailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeaseOwner = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClaimVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GatewayEvents", x => x.Id);
                    table.CheckConstraint("CK_GatewayEvents_Counters", "\"Attempts\" >= 0 AND \"ClaimVersion\" >= 0");
                    table.CheckConstraint("CK_GatewayEvents_Status", "\"Status\" IN ('Pending','Processing','Completed','DeadLetter')");
                    table.ForeignKey(
                        name: "FK_GatewayEvents_Checkout",
                        column: x => x.CheckoutId,
                        principalSchema: "fake_gateway",
                        principalTable: "Checkouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_GatewayCharges_Booking",
                schema: "fake_gateway",
                table: "Charges",
                column: "BookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_GatewayCharges_Checkout",
                schema: "fake_gateway",
                table: "Charges",
                column: "CheckoutId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_GatewayCheckouts_Key",
                schema: "fake_gateway",
                table: "Checkouts",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_CheckoutId",
                schema: "fake_gateway",
                table: "Events",
                column: "CheckoutId");

            migrationBuilder.CreateIndex(
                name: "IX_GatewayEvents_Due",
                schema: "fake_gateway",
                table: "Events",
                columns: new[] { "Status", "AvailableAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Charges",
                schema: "fake_gateway");

            migrationBuilder.DropTable(
                name: "Events",
                schema: "fake_gateway");

            migrationBuilder.DropTable(
                name: "Checkouts",
                schema: "fake_gateway");
        }
    }
}
