using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Labaik.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BookingCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "app");

            migrationBuilder.CreateTable(
                name: "DailyCapacities",
                schema: "app",
                columns: table => new
                {
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Limit = table.Column<int>(type: "integer", nullable: false),
                    Allocated = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyCapacities", x => x.ServiceDate);
                    table.CheckConstraint("CK_DailyCapacity_Bounds", "\"Limit\" >= 0 AND \"Allocated\" >= 0 AND \"Allocated\" <= \"Limit\"");
                });

            migrationBuilder.CreateTable(
                name: "Users",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirebaseProject = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FirebaseUid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Locale = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    SuspendedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastOfferedSequence = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.CheckConstraint("CK_Users_Identity", "length(btrim(\"FirebaseProject\")) > 0 AND length(btrim(\"FirebaseUid\")) > 0");
                    table.CheckConstraint("CK_Users_Locale", "\"Locale\" IN ('en','ar')");
                    table.CheckConstraint("CK_Users_Role", "\"Role\" IN ('Customer','Performer','Supervisor','Accountant','Admin')");
                    table.CheckConstraint("CK_Users_Sequence", "\"LastOfferedSequence\" >= 0");
                    table.CheckConstraint("CK_Users_Status", "\"Status\" IN ('Active','Disabled')");
                });

            migrationBuilder.CreateTable(
                name: "Vouchers",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeNormalized = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FixedMinor = table.Column<long>(type: "bigint", nullable: true),
                    BasisPoints = table.Column<int>(type: "integer", nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    MaxUses = table.Column<int>(type: "integer", nullable: false),
                    PerUserLimit = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vouchers", x => x.Id);
                    table.CheckConstraint("CK_Vouchers_Code", "\"CodeNormalized\" ~ '^[A-Z0-9_-]{1,64}$'");
                    table.CheckConstraint("CK_Vouchers_Currency", "\"Currency\" = 'SAR'");
                    table.CheckConstraint("CK_Vouchers_Discount", "(\"Kind\" = 'Percentage' AND \"FixedMinor\" IS NULL AND \"BasisPoints\" IS NOT NULL AND \"BasisPoints\" BETWEEN 0 AND 10000) OR (\"Kind\" = 'Fixed' AND \"BasisPoints\" IS NULL AND \"FixedMinor\" IS NOT NULL AND \"FixedMinor\" >= 0)");
                    table.CheckConstraint("CK_Vouchers_Limits", "\"MaxUses\" > 0 AND \"PerUserLimit\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "Bookings",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    BaseMinor = table.Column<long>(type: "bigint", nullable: false),
                    AddonMinor = table.Column<long>(type: "bigint", nullable: false),
                    DiscountMinor = table.Column<long>(type: "bigint", nullable: false),
                    TotalMinor = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    VoucherId = table.Column<Guid>(type: "uuid", nullable: true),
                    CapacityAllocated = table.Column<bool>(type: "boolean", nullable: false),
                    AllocationReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookings", x => x.Id);
                    table.CheckConstraint("CK_Bookings_Checkout", "\"State\" <> 'PendingPayment' OR \"TotalMinor\" > 0");
                    table.CheckConstraint("CK_Bookings_Currency", "\"Currency\" = 'SAR'");
                    table.CheckConstraint("CK_Bookings_Money", "\"BaseMinor\" >= 0 AND \"AddonMinor\" >= 0 AND \"DiscountMinor\" >= 0 AND \"TotalMinor\" >= 0");
                    table.CheckConstraint("CK_Bookings_State", "\"State\" IN ('Draft','PendingPayment','Paid','Offered','Accepted','InProgress','ProofSubmitted','Completed','Cancelled','Refunded','Disputed')");
                    table.CheckConstraint("CK_Bookings_Total", "\"TotalMinor\"::numeric = \"BaseMinor\"::numeric + \"AddonMinor\"::numeric - \"DiscountMinor\"::numeric AND \"BaseMinor\"::numeric + \"AddonMinor\"::numeric <= 9223372036854775807");
                    table.CheckConstraint("CK_Bookings_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_Bookings_Users",
                        column: x => x.CustomerId,
                        principalSchema: "app",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Bookings_Vouchers",
                        column: x => x.VoucherId,
                        principalSchema: "app",
                        principalTable: "Vouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VoucherRedemptions",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VoucherId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ReservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoucherRedemptions", x => x.Id);
                    table.CheckConstraint("CK_VoucherRedemptions_Status", "\"Status\" IN ('Reserved','Consumed','Released')");
                    table.ForeignKey(
                        name: "FK_Redemptions_Bookings",
                        column: x => x.BookingId,
                        principalSchema: "app",
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Redemptions_Users",
                        column: x => x.UserId,
                        principalSchema: "app",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Redemptions_Vouchers",
                        column: x => x.VoucherId,
                        principalSchema: "app",
                        principalTable: "Vouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_Customer",
                schema: "app",
                table: "Bookings",
                columns: new[] { "CustomerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_StateDate",
                schema: "app",
                table: "Bookings",
                columns: new[] { "State", "ServiceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_VoucherId",
                schema: "app",
                table: "Bookings",
                column: "VoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Assignment",
                schema: "app",
                table: "Users",
                columns: new[] { "Role", "Status", "LastOfferedSequence" });

            migrationBuilder.CreateIndex(
                name: "UX_Users_FirebaseIdentity",
                schema: "app",
                table: "Users",
                columns: new[] { "FirebaseProject", "FirebaseUid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Redemptions_Usage",
                schema: "app",
                table: "VoucherRedemptions",
                columns: new[] { "VoucherId", "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_VoucherRedemptions_UserId",
                schema: "app",
                table: "VoucherRedemptions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UX_Redemptions_Booking",
                schema: "app",
                table: "VoucherRedemptions",
                column: "BookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Vouchers_Code",
                schema: "app",
                table: "Vouchers",
                column: "CodeNormalized",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyCapacities",
                schema: "app");

            migrationBuilder.DropTable(
                name: "VoucherRedemptions",
                schema: "app");

            migrationBuilder.DropTable(
                name: "Bookings",
                schema: "app");

            migrationBuilder.DropTable(
                name: "Users",
                schema: "app");

            migrationBuilder.DropTable(
                name: "Vouchers",
                schema: "app");
        }
    }
}
