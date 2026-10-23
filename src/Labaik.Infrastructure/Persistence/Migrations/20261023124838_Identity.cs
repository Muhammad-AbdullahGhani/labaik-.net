using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Labaik.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Identity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuthRateBuckets",
                schema: "app",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WindowStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthRateBuckets", x => x.Key);
                    table.CheckConstraint("CK_AuthRateBuckets_Count", "\"Count\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "RefreshFamilies",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshFamilies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshFamilies_Users",
                        column: x => x.UserId,
                        principalSchema: "app",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RetryUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RetryKeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EncryptedSuccessor = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.UniqueConstraint("AK_RefreshTokens_IdFamily", x => new { x.Id, x.FamilyId });
                    table.CheckConstraint("CK_RefreshTokens_Hash", "length(\"Hash\")=64");
                    table.CheckConstraint("CK_RefreshTokens_Rotation", "(\"ConsumedAt\" IS NULL AND \"RetryUntil\" IS NULL AND \"RetryKeyHash\" IS NULL AND \"EncryptedSuccessor\" IS NULL) OR (\"ConsumedAt\" IS NOT NULL AND \"RetryUntil\" IS NOT NULL AND \"RetryKeyHash\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Family",
                        column: x => x.FamilyId,
                        principalSchema: "app",
                        principalTable: "RefreshFamilies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_ParentFamily",
                        columns: x => new { x.ParentId, x.FamilyId },
                        principalSchema: "app",
                        principalTable: "RefreshTokens",
                        principalColumns: new[] { "Id", "FamilyId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthRateBuckets_Expiry",
                schema: "app",
                table: "AuthRateBuckets",
                column: "WindowStart");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshFamilies_User",
                schema: "app",
                table: "RefreshFamilies",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_FamilyId",
                schema: "app",
                table: "RefreshTokens",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_ParentId_FamilyId",
                schema: "app",
                table: "RefreshTokens",
                columns: new[] { "ParentId", "FamilyId" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_RecoveryExpiry",
                schema: "app",
                table: "RefreshTokens",
                column: "RetryUntil");

            migrationBuilder.CreateIndex(
                name: "UX_RefreshTokens_Hash",
                schema: "app",
                table: "RefreshTokens",
                column: "Hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_RefreshTokens_Parent",
                schema: "app",
                table: "RefreshTokens",
                column: "ParentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthRateBuckets",
                schema: "app");

            migrationBuilder.DropTable(
                name: "RefreshTokens",
                schema: "app");

            migrationBuilder.DropTable(
                name: "RefreshFamilies",
                schema: "app");
        }
    }
}
