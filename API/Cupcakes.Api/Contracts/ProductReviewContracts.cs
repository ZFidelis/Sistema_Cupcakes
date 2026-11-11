using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Cupcakes.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateProductReviewRequest
{
    private string comment = "";
    [Required]
    [Range(1, 5)]
    public int? Rating { get; init; }
    [Required]
    [StringLength(1000, MinimumLength = 1)]
    public string Comment { get => comment; init => comment = value?.Trim() ?? ""; }
}

public sealed record ProductReviewResponse(Guid Id, Guid ProductId, int Rating, string Comment,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
