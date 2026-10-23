using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Labaik.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssignmentFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_JobRuns_Kind",
                schema: "app",
                table: "JobRuns");

            migrationBuilder.CreateTable(
                name: "PerformerOffers",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    PerformerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerformerOffers", x => x.Id);
                    table.CheckConstraint("CK_Offers_Status", "\"Status\" IN ('Pending','Accepted','Expired','Cancelled','Finished')");
                    table.ForeignKey(
                        name: "FK_Offers_Bookings",
                        column: x => x.BookingId,
                        principalSchema: "app",
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Offers_Users",
                        column: x => x.PerformerId,
                        principalSchema: "app",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StoredFiles",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    ObjectKey = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    ETag = table.Column<string>(type: "text", nullable: true),
                    VersionId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredFiles", x => x.Id);
                    table.CheckConstraint("CK_Files_Kind", "\"Kind\" IN ('Proof','Passport')");
                    table.CheckConstraint("CK_Files_Size", "\"Size\" >= 0 AND \"Size\" <= 52428800");
                    table.CheckConstraint("CK_Files_Status", "\"Status\" IN ('PendingUpload','Uploaded','Approved')");
                    table.ForeignKey(
                        name: "FK_Files_Booking",
                        column: x => x.BookingId,
                        principalSchema: "app",
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Files_Owner",
                        column: x => x.OwnerId,
                        principalSchema: "app",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_JobRuns_Kind",
                schema: "app",
                table: "JobRuns",
                sql: "\"Kind\" IN ('RecoverCheckout','Inbox','Outbox','Refund','Assign','ExpireOffer')");

            migrationBuilder.CreateIndex(
                name: "IX_Offers_Expiry",
                schema: "app",
                table: "PerformerOffers",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "UX_Offers_ActiveBooking",
                schema: "app",
                table: "PerformerOffers",
                column: "BookingId",
                unique: true,
                filter: "\"Status\" IN ('Pending','Accepted')");

            migrationBuilder.CreateIndex(
                name: "UX_Offers_ActivePerformer",
                schema: "app",
                table: "PerformerOffers",
                column: "PerformerId",
                unique: true,
                filter: "\"Status\" IN ('Pending','Accepted')");

            migrationBuilder.CreateIndex(
                name: "IX_Files_BookingKind",
                schema: "app",
                table: "StoredFiles",
                columns: new[] { "BookingId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_OwnerId",
                schema: "app",
                table: "StoredFiles",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "UX_Files_ObjectKey",
                schema: "app",
                table: "StoredFiles",
                column: "ObjectKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PerformerOffers",
                schema: "app");

            migrationBuilder.DropTable(
                name: "StoredFiles",
                schema: "app");

            migrationBuilder.DropCheckConstraint(
                name: "CK_JobRuns_Kind",
                schema: "app",
                table: "JobRuns");

            migrationBuilder.AddCheckConstraint(
                name: "CK_JobRuns_Kind",
                schema: "app",
                table: "JobRuns",
                sql: "\"Kind\" IN ('RecoverCheckout','Inbox','Outbox','Refund')");
        }
    }
}
