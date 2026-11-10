using Cupcakes.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cupcakes.Api.Data;

public sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> cart)
    {
        cart.ToTable("Carts");
        cart.HasKey(x => x.Id);
        cart.Property(x => x.UserId).IsRequired();
        cart.HasIndex(x => x.UserId).IsUnique();
        cart.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        cart.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.CartId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> item)
    {
        item.ToTable("CartItems", table => table.HasCheckConstraint("CK_CartItems_Quantity", "\"Quantity\" > 0"));
        item.HasKey(x => new { x.CartId, x.ProductId });
        item.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}
