namespace Cupcakes.Api.Contracts;

public sealed record OrderConfirmationItem(Guid ProductId, string ProductName, int Quantity,
    decimal OriginalUnitPrice, decimal AppliedUnitPrice, decimal Subtotal, decimal Discount, decimal Total);

public sealed record OrderConfirmationResponse(Guid Id, Guid? ConfirmationId, DateTimeOffset CreatedAt,
    string Status, string PaymentMethod, string PaymentResult, CheckoutAddressResponse Address,
    OrderConfirmationItem[] Items, decimal Subtotal, decimal Discount, decimal Total);

public sealed record CheckoutConfirmationResponse(string Code, string Message, bool IsSimulation,
    OrderConfirmationResponse? Order = null, CheckoutPreviewResponse? Preview = null);
