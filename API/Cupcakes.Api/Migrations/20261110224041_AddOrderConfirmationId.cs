using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cupcakes.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderConfirmationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConfirmationId",
                table: "Orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_UserId_ConfirmationId",
                table: "Orders",
                columns: new[] { "UserId", "ConfirmationId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_ConfirmationId",
                table: "Orders",
                sql: "\"ConfirmationId\" IS NULL OR \"ConfirmationId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_UserId_ConfirmationId",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_ConfirmationId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ConfirmationId",
                table: "Orders");
        }
    }
}
