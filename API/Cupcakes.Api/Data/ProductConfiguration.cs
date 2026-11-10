using Cupcakes.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cupcakes.Api.Data;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> product)
    {
        product.ToTable("Products", table =>
        {
            table.HasCheckConstraint("CK_Products_Name", "length(btrim(\"Name\")) > 0");
            table.HasCheckConstraint("CK_Products_Flavor", "length(btrim(\"Flavor\")) > 0");
            table.HasCheckConstraint("CK_Products_Description", "length(btrim(\"Description\")) > 0");
            table.HasCheckConstraint("CK_Products_Price", "\"Price\" > 0");
            table.HasCheckConstraint("CK_Products_Promotion",
                "\"PromotionalPrice\" IS NULL OR (\"PromotionalPrice\" > 0 AND \"PromotionalPrice\" < \"Price\")");
            table.HasCheckConstraint("CK_Products_Stock", "\"Stock\" >= 0");
            table.HasCheckConstraint("CK_Products_ImagePath", "\"ImagePath\" = '/images/products/cupcake.svg'");
        });
        product.HasKey(x => x.Id);
        product.Property(x => x.Name).HasMaxLength(100).IsRequired();
        product.Property(x => x.Flavor).HasMaxLength(60).IsRequired();
        product.Property(x => x.Description).HasMaxLength(1000).IsRequired();
        product.Property(x => x.ImagePath).HasMaxLength(250).IsRequired();
        product.Property(x => x.Price).HasPrecision(12, 2);
        product.Property(x => x.PromotionalPrice).HasPrecision(12, 2);
        product.Property(x => x.IsActive).HasDefaultValue(true);
        product.Ignore(x => x.EffectivePrice);


        product.HasData(
            new { Id = Guid.Parse("b8b95c22-a515-4f4b-a750-57343a6b0101"), Name = "Chocolate gourmet",
                Flavor = "Chocolate", Description = "Cupcake demonstrativo de chocolate com cobertura cremosa.",
                ImagePath = Product.DemoImagePath, Price = 12.50m, PromotionalPrice = (decimal?)null, Stock = 20, IsActive = true },
            new { Id = Guid.Parse("b8b95c22-a515-4f4b-a750-57343a6b0102"), Name = "Morango gourmet",
                Flavor = "Morango", Description = "Cupcake demonstrativo de morango com cobertura suave.",
                ImagePath = Product.DemoImagePath, Price = 14.00m, PromotionalPrice = (decimal?)11.90m, Stock = 15, IsActive = true },
            new { Id = Guid.Parse("b8b95c22-a515-4f4b-a750-57343a6b0103"), Name = "Baunilha gourmet",
                Flavor = "Baunilha", Description = "Cupcake demonstrativo de baunilha com cobertura delicada.",
                ImagePath = Product.DemoImagePath, Price = 10.00m, PromotionalPrice = (decimal?)null, Stock = 0, IsActive = true });
    }
}
