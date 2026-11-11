namespace Cupcakes.Api.Contracts;

public sealed record AdminProductResponse(Guid Id, string Name, string Flavor, string Description,
    string ImagePath, decimal Price, decimal? PromotionalPrice, decimal EffectivePrice,
    int Stock, bool IsActive, bool IsAvailable);
