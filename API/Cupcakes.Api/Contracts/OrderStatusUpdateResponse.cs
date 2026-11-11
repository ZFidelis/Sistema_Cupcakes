namespace Cupcakes.Api.Contracts;

public sealed record OrderStatusUpdateResponse(Guid OrderId, string Status, OrderStatusResponse[] History);
