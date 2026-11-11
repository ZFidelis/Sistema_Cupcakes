using System.ComponentModel.DataAnnotations;
using Cupcakes.Api.Domain;

namespace Cupcakes.Api.Contracts;

public sealed class ProductFilterRequest : IValidatableObject
{
    public string? Flavor { get; init; }
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public bool? OnPromotion { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Flavor?.Trim().Length > 60)
            yield return new ValidationResult("Informe um sabor com ate 60 caracteres.", [nameof(Flavor)]);

        foreach (var (name, value) in new[] { (nameof(MinPrice), MinPrice), (nameof(MaxPrice), MaxPrice) })
        {
            if (value is { } price && (price < 0 || price > Product.MaximumPrice || decimal.Round(price, 2) != price))
                yield return new ValidationResult("Informe um preco de zero a 9999999999.99 com ate duas casas decimais.", [name]);
        }

        if (MinPrice > MaxPrice)
            yield return new ValidationResult("O preco minimo nao pode superar o maximo.", [nameof(MinPrice), nameof(MaxPrice)]);
    }
}
