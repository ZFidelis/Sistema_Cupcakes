using Cupcakes.Api.Contracts;
using Cupcakes.Api.Data;
using Cupcakes.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cupcakes.Api.Services;

public sealed class ProductReviewService(CupcakesDbContext db)
{
    public async Task<ProductReviewResponse[]?> ListAsync(Guid productId, CancellationToken ct)
    {
        if (!await db.Products.AnyAsync(product => product.Id == productId && product.IsActive, ct)) return null;
        return await db.ProductReviews.AsNoTracking().Where(review => review.ProductId == productId)
            .OrderByDescending(review => review.UpdatedAt).ThenByDescending(review => review.Id)
            .Select(review => new ProductReviewResponse(review.Id, review.ProductId, review.Rating, review.Comment,
                review.CreatedAt, review.UpdatedAt)).ToArrayAsync(ct);
    }

    public async Task<ProductReviewResponse?> MineAsync(string userId, Guid productId, CancellationToken ct)
    {
        var review = await db.ProductReviews.AsNoTracking().SingleOrDefaultAsync(value => value.UserId == userId && value.ProductId == productId, ct);
        return review is null ? null : Map(review);
    }

    public async Task<ProductReviewWriteResult> SaveAsync(string userId, Guid productId, UpdateProductReviewRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await db.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {userId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        if (user is null) return new(401);
        var product = await db.Products.FromSqlInterpolated($"SELECT * FROM \"Products\" WHERE \"Id\" = {productId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        if (product is null) return new(404);
        if (!product.IsActive) return new(409);
        var review = await db.ProductReviews.SingleOrDefaultAsync(value => value.UserId == userId && value.ProductId == productId, ct);
        var created = review is null;
        if (review is null) { review = new ProductReview(userId, productId, request.Rating!.Value, request.Comment); db.ProductReviews.Add(review); }
        else review.Update(request.Rating!.Value, request.Comment);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(created ? 201 : 200, Map(review));
    }

    private static ProductReviewResponse Map(ProductReview review) => new(review.Id, review.ProductId,
        review.Rating, review.Comment, review.CreatedAt, review.UpdatedAt);
}

public sealed record ProductReviewWriteResult(int StatusCode, ProductReviewResponse? Review = null);
