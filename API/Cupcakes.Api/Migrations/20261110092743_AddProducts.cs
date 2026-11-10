using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Cupcakes.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Flavor = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ImagePath = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    PromotionalPrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    Stock = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                    table.CheckConstraint("CK_Products_Description", "length(btrim(\"Description\")) > 0");
                    table.CheckConstraint("CK_Products_Flavor", "length(btrim(\"Flavor\")) > 0");
                    table.CheckConstraint("CK_Products_ImagePath", "\"ImagePath\" = '/images/products/cupcake.svg'");
                    table.CheckConstraint("CK_Products_Name", "length(btrim(\"Name\")) > 0");
                    table.CheckConstraint("CK_Products_Price", "\"Price\" > 0");
                    table.CheckConstraint("CK_Products_Promotion", "\"PromotionalPrice\" IS NULL OR (\"PromotionalPrice\" > 0 AND \"PromotionalPrice\" < \"Price\")");
                    table.CheckConstraint("CK_Products_Stock", "\"Stock\" >= 0");
                });

            migrationBuilder.InsertData(
                table: "Products",
                columns: new[] { "Id", "Description", "Flavor", "ImagePath", "IsActive", "Name", "Price", "PromotionalPrice", "Stock" },
                values: new object[,]
                {
                    { new Guid("b8b95c22-a515-4f4b-a750-57343a6b0101"), "Cupcake demonstrativo de chocolate com cobertura cremosa.", "Chocolate", "/images/products/cupcake.svg", true, "Chocolate gourmet", 12.50m, null, 20 },
                    { new Guid("b8b95c22-a515-4f4b-a750-57343a6b0102"), "Cupcake demonstrativo de morango com cobertura suave.", "Morango", "/images/products/cupcake.svg", true, "Morango gourmet", 14.00m, 11.90m, 15 },
                    { new Guid("b8b95c22-a515-4f4b-a750-57343a6b0103"), "Cupcake demonstrativo de baunilha com cobertura delicada.", "Baunilha", "/images/products/cupcake.svg", true, "Baunilha gourmet", 10.00m, null, 0 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Products");
        }
    }
}
