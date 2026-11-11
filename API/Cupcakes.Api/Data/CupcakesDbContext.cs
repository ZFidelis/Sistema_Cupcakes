using Cupcakes.Api.Identity;
using Cupcakes.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Cupcakes.Api.Data;

public sealed class CupcakesDbContext(DbContextOptions<CupcakesDbContext> options)
    : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductReview> ProductReviews => Set<ProductReview>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderAddress> OrderAddresses => Set<OrderAddress>();
    public DbSet<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfiguration(new ProductConfiguration());
        builder.ApplyConfiguration(new ProductReviewConfiguration());
        builder.ApplyConfiguration(new CartConfiguration());
        builder.ApplyConfiguration(new CartItemConfiguration());
        builder.ApplyConfiguration(new OrderConfiguration());
        builder.ApplyConfiguration(new OrderItemConfiguration());
        builder.ApplyConfiguration(new OrderAddressConfiguration());
        builder.ApplyConfiguration(new OrderStatusHistoryConfiguration());

        builder.Entity<IdentityRole>().HasData(new IdentityRole
        {
            Id = "cliente",
            Name = ApplicationRoles.Customer,
            NormalizedName = "CLIENTE",
            ConcurrencyStamp = "cliente-v1"
        });

        builder.Entity<IdentityUser>(user =>
        {
            user.Property(x => x.Email).IsRequired();
            user.Property(x => x.NormalizedEmail).IsRequired();
            user.HasIndex(x => x.NormalizedEmail).HasDatabaseName("EmailIndex").IsUnique();
        });
    }
}
