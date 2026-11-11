namespace Cupcakes.Api.Contracts;

public sealed record AdminOrderSummaryResponse(string CustomerEmail, OrderSummaryResponse Order);

public sealed record AdminOrderDetailResponse(string CustomerEmail, OrderDetailResponse Detail);
