using Cupcakes.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cupcakes.Api.Data;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> order)
    {
        order.ToTable("Orders", table =>
        {
            table.HasCheckConstraint("CK_Orders_Status", "\"Status\" IN ('Recebido', 'EmPreparo', 'SaiuParaEntrega', 'Entregue')");
            table.HasCheckConstraint("CK_Orders_PaymentMethod", "\"PaymentMethod\" IN ('Pix', 'Cartao')");
            table.HasCheckConstraint("CK_Orders_PaymentResult", "\"PaymentResult\" = 'Aprovado'");
            table.HasCheckConstraint("CK_Orders_Totals", "\"Subtotal\" > 0 AND \"Discount\" >= 0 AND \"Discount\" < \"Subtotal\" AND \"Total\" = \"Subtotal\" - \"Discount\"");
        });
        order.HasKey(x => x.Id);
        order.Property(x => x.UserId).IsRequired();
        order.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
        order.Property(x => x.PaymentMethod).HasConversion<string>().HasMaxLength(16);
        order.Property(x => x.PaymentResult).HasMaxLength(16).IsRequired();
        order.Property(x => x.Subtotal).HasPrecision(12, 2);
        order.Property(x => x.Discount).HasPrecision(12, 2);
        order.Property(x => x.Total).HasPrecision(12, 2);
        order.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        order.HasOne(x => x.Address).WithOne().HasForeignKey<OrderAddress>(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        order.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        order.HasMany(x => x.StatusHistory).WithOne().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        order.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        order.Navigation(x => x.StatusHistory).UsePropertyAccessMode(PropertyAccessMode.Field);
        order.HasIndex(x => new { x.UserId, x.CreatedAt });
        order.HasIndex(x => x.CreatedAt);
        order.HasIndex(x => x.Status);
    }
}

public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> item)
    {
        item.ToTable("OrderItems", table =>
        {
            table.HasCheckConstraint("CK_OrderItems_Quantity", "\"Quantity\" > 0");
            table.HasCheckConstraint("CK_OrderItems_Prices", "\"OriginalUnitPrice\" > 0 AND \"AppliedUnitPrice\" > 0 AND \"AppliedUnitPrice\" <= \"OriginalUnitPrice\"");
            table.HasCheckConstraint("CK_OrderItems_Name", "length(btrim(\"ProductName\")) > 0");
        });
        item.HasKey(x => new { x.OrderId, x.ProductId });
        item.Property(x => x.ProductName).HasMaxLength(100).IsRequired();
        item.Property(x => x.OriginalUnitPrice).HasPrecision(12, 2);
        item.Property(x => x.AppliedUnitPrice).HasPrecision(12, 2);
        item.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        item.Ignore(x => x.Subtotal);
        item.Ignore(x => x.Discount);
        item.Ignore(x => x.Total);
    }
}

public sealed class OrderAddressConfiguration : IEntityTypeConfiguration<OrderAddress>
{
    public void Configure(EntityTypeBuilder<OrderAddress> address)
    {
        address.ToTable("OrderAddresses", table =>
        {
            table.HasCheckConstraint("CK_OrderAddresses_Text", "length(btrim(\"Recipient\")) > 0 AND length(btrim(\"Street\")) > 0 AND length(btrim(\"Number\")) > 0 AND length(btrim(\"Neighborhood\")) > 0 AND length(btrim(\"City\")) > 0");
            table.HasCheckConstraint("CK_OrderAddresses_State", "\"State\" ~ '^[A-Z]{2}$'");
            table.HasCheckConstraint("CK_OrderAddresses_PostalCode", "\"PostalCode\" ~ '^[0-9]{8}$'");
        });
        address.HasKey(x => x.OrderId);
        address.Property(x => x.Recipient).HasMaxLength(100).IsRequired();
        address.Property(x => x.Street).HasMaxLength(150).IsRequired();
        address.Property(x => x.Number).HasMaxLength(20).IsRequired();
        address.Property(x => x.Complement).HasMaxLength(100);
        address.Property(x => x.Neighborhood).HasMaxLength(80).IsRequired();
        address.Property(x => x.City).HasMaxLength(80).IsRequired();
        address.Property(x => x.State).HasMaxLength(2).IsRequired();
        address.Property(x => x.PostalCode).HasMaxLength(8).IsRequired();
    }
}

public sealed class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> history)
    {
        history.ToTable("OrderStatusHistory", table => table.HasCheckConstraint("CK_OrderStatusHistory_Status",
            "\"Status\" IN ('Recebido', 'EmPreparo', 'SaiuParaEntrega', 'Entregue')"));
        history.HasKey(x => x.Id);
        history.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
        history.HasIndex(x => new { x.OrderId, x.ChangedAt });
        history.HasIndex(x => new { x.OrderId, x.Status }).IsUnique();
    }
}
