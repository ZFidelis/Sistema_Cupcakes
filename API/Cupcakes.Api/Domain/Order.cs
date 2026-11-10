namespace Cupcakes.Api.Domain;

public sealed class Order
{
    private readonly List<OrderItem> _items = [];
    private readonly List<OrderStatusHistory> _statusHistory = [];

    private Order() { }

    // Representa apenas pedido aprovado na simulacao. Nao executa pagamento nem baixa estoque.
    public Order(string userId, SimulatedPaymentMethod paymentMethod, OrderAddress address, IEnumerable<OrderItem> items,
        Guid? confirmationId = null)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("Informe o usuario.", nameof(userId));
        if (!Enum.IsDefined(paymentMethod)) throw new ArgumentOutOfRangeException(nameof(paymentMethod));
        if (confirmationId == Guid.Empty) throw new ArgumentException("Identificador de confirmacao invalido.", nameof(confirmationId));
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(items);
        var snapshot = items.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(item => item is null))
            throw new ArgumentException("O pedido deve conter itens validos.", nameof(items));
        if (snapshot.Select(item => item.ProductId).Distinct().Count() != snapshot.Length)
            throw new ArgumentException("Cada produto deve ocorrer uma unica vez no pedido.", nameof(items));

        Subtotal = snapshot.Sum(item => item.Subtotal);
        Discount = snapshot.Sum(item => item.Discount);
        Total = Subtotal - Discount;
        OrderValidation.Money(Subtotal, nameof(items));
        OrderValidation.Money(Total, nameof(items));

        Id = Guid.NewGuid();
        ConfirmationId = confirmationId;
        UserId = userId;
        CreatedAt = DateTimeOffset.UtcNow;
        Status = OrderStatus.Recebido;
        PaymentMethod = paymentMethod;
        Address = address.ForOrder(Id);
        _items.AddRange(snapshot.Select(item => item.ForOrder(Id)));
        _statusHistory.Add(new OrderStatusHistory(Id, Status, CreatedAt));
    }

    public Guid Id { get; private set; }
    public Guid? ConfirmationId { get; private set; }
    public string UserId { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public OrderStatus Status { get; private set; }
    public SimulatedPaymentMethod PaymentMethod { get; private set; }
    public string PaymentResult { get; private set; } = "Aprovado";
    public decimal Subtotal { get; private set; }
    public decimal Discount { get; private set; }
    public decimal Total { get; private set; }
    public OrderAddress Address { get; private set; } = null!;
    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();
    public IReadOnlyCollection<OrderStatusHistory> StatusHistory => _statusHistory.AsReadOnly();
}
