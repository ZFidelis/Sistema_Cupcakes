namespace Cupcakes.Api.Domain;

public sealed class ProductReview
{
    private ProductReview() { }

    public ProductReview(string userId, Guid productId, int rating, string comment)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("Informe o autor.", nameof(userId));
        if (productId == Guid.Empty) throw new ArgumentException("Informe o produto.", nameof(productId));
        Id = Guid.NewGuid(); UserId = userId; ProductId = productId;
        CreatedAt = new DateTimeOffset(DateTimeOffset.UtcNow.UtcTicks / 10 * 10, TimeSpan.Zero);
        UpdatedAt = CreatedAt;
        Update(rating, comment);
    }

    public Guid Id { get; private set; }
    public string UserId { get; private set; } = null!;
    public Guid ProductId { get; private set; }
    public int Rating { get; private set; }
    public string Comment { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(int rating, string comment)
    {
        if (rating is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(rating));
        if (string.IsNullOrWhiteSpace(comment) || comment.Trim().Length > 1000)
            throw new ArgumentException("Informe comentario de 1 a 1000 caracteres.", nameof(comment));
        var text = comment.Trim();
        if (Rating == rating && Comment == text) return;
        if (Comment is not null)
            UpdatedAt = new DateTimeOffset(Math.Max(DateTimeOffset.UtcNow.UtcTicks / 10 * 10, UpdatedAt.UtcTicks + 10), TimeSpan.Zero);
        Rating = rating; Comment = text;
    }
}
