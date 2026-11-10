namespace Cupcakes.Api.Contracts;

public sealed record OrderSummaryResponse(Guid Id, DateTimeOffset CreatedAt, string Status,
    string PaymentMethod, string PaymentResult, long ItemCount, decimal Subtotal, decimal Discount,
    decimal Total, bool IsSimulation);

public sealed record OrderStatusResponse(string Status, DateTimeOffset ChangedAt);

public sealed record OrderDetailResponse(OrderConfirmationResponse Order, OrderStatusResponse[] History,
    bool IsSimulation, string Notice);
