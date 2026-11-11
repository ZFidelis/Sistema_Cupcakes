using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Cupcakes.Api.Domain;

namespace Cupcakes.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateProductRequest : IValidatableObject
{
    public string Name { get; init; } = "";
    public string Flavor { get; init; } = "";
    public string Description { get; init; } = "";
    public string ImagePath { get; init; } = "";
    [Required]
    public decimal? Price { get; init; }
    public decimal? PromotionalPrice { get; init; }
    [Required]
    public int? Stock { get; init; }

    public Product ToDomain() => new(Name, Flavor, Description, ImagePath, Price ?? 0,
        PromotionalPrice, Stock ?? -1);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        try
        {
            _ = ToDomain();
            return [];
        }
        catch (ArgumentException error)
        {
            return [new ValidationResult(error.Message, [error.ParamName ?? "product"])];
        }
    }
}
