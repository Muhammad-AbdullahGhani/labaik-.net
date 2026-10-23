using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Labaik.FakeGateway.Migrations
{
    /// <inheritdoc />
    public partial class GatewayRefunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Refunds",
                schema: "fake_gateway",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "text", nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GatewayRefunds", x => x.Id);
                    table.CheckConstraint("CK_GatewayRefunds_Money", "\"AmountMinor\" > 0 AND \"Currency\"='SAR'");
                    table.ForeignKey(
                        name: "FK_GatewayRefunds_Charge",
                        column: x => x.TransactionId,
                        principalSchema: "fake_gateway",
                        principalTable: "Charges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_GatewayRefunds_Charge",
                schema: "fake_gateway",
                table: "Refunds",
                column: "TransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_GatewayRefunds_Key",
                schema: "fake_gateway",
                table: "Refunds",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Refunds",
                schema: "fake_gateway");
        }
    }
}
