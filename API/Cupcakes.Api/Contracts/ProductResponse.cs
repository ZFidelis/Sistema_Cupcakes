namespace Cupcakes.Api.Contracts;

public sealed record ProductResponse(Guid Id, string Name, string Flavor, string Description,
    string ImagePath, decimal Price, decimal? PromotionalPrice, decimal EffectivePrice,
    int Stock, bool IsAvailable);
