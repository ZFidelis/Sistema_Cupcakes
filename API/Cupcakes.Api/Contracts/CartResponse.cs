namespace Cupcakes.Api.Contracts;

public sealed record CartItemResponse(Guid ProductId, string Name, string ImagePath, int Quantity,
    decimal Price, decimal? PromotionalPrice, decimal EffectivePrice, decimal Subtotal,
    decimal Discount, decimal Total, int Stock, bool IsAvailable, string? AvailabilityMessage);

public sealed record CartResponse(CartItemResponse[] Items, long ItemCount, decimal Subtotal,
    decimal Discount, decimal Total, bool HasUnavailableItems, string PricingNotice);
