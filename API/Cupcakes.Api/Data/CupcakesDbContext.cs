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
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfiguration(new ProductConfiguration());
        builder.ApplyConfiguration(new CartConfiguration());
        builder.ApplyConfiguration(new CartItemConfiguration());

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
