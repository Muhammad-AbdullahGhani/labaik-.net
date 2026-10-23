using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Labaik.FakeGateway.Migrations
{
    /// <inheritdoc />
    public partial class InitialEmpty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No business entities in step 1; EF records only this baseline in its schema-local history table.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
