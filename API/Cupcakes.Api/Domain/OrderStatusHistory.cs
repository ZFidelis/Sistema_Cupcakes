namespace Cupcakes.Api.Domain;

public enum OrderStatus { Recebido, EmPreparo, SaiuParaEntrega, Entregue }
public enum SimulatedPaymentMethod { Pix, Cartao }

public sealed class OrderStatusHistory
{
    private OrderStatusHistory() { }

    internal OrderStatusHistory(Guid orderId, OrderStatus status, DateTimeOffset changedAt)
    {
        Id = Guid.NewGuid();
        OrderId = orderId;
        Status = status;
        ChangedAt = changedAt.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public OrderStatus Status { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }
}
