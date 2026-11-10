using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cupcakes.Api.Migrations
{
    /// <inheritdoc />
    public partial class SeedCustomerRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "AspNetRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "Name", "NormalizedName" },
                values: new object[] { "cliente", "cliente-v1", "Cliente", "CLIENTE" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "cliente");
        }
    }
}
