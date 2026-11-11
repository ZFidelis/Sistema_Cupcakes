using Cupcakes.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cupcakes.Api.Data;

public sealed class ProductReviewConfiguration : IEntityTypeConfiguration<ProductReview>
{
    public void Configure(EntityTypeBuilder<ProductReview> review)
    {
        review.ToTable("ProductReviews", table =>
        {
            table.HasCheckConstraint("CK_ProductReviews_Rating", "\"Rating\" BETWEEN 1 AND 5");
            table.HasCheckConstraint("CK_ProductReviews_Comment", "length(btrim(\"Comment\")) > 0");
            table.HasCheckConstraint("CK_ProductReviews_Dates", "\"UpdatedAt\" >= \"CreatedAt\"");
        });
        review.HasKey(value => value.Id);
        review.Property(value => value.UserId).IsRequired();
        review.Property(value => value.Comment).HasMaxLength(1000).IsRequired();
        review.HasIndex(value => new { value.UserId, value.ProductId }).IsUnique();
        review.HasOne<IdentityUser>().WithMany().HasForeignKey(value => value.UserId).OnDelete(DeleteBehavior.Restrict);
        review.HasOne<Product>().WithMany().HasForeignKey(value => value.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}
