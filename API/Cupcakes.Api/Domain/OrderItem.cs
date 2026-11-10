namespace Cupcakes.Api.Domain;

public sealed class OrderItem
{
    private OrderItem() { }

    public OrderItem(Guid productId, string productName, decimal originalUnitPrice, decimal appliedUnitPrice, int quantity)
    {
        if (productId == Guid.Empty) throw new ArgumentException("Informe o produto.", nameof(productId));
        ProductId = productId;
        ProductName = OrderValidation.Text(productName, 100, nameof(productName));
        OrderValidation.Money(originalUnitPrice, nameof(originalUnitPrice));
        OrderValidation.Money(appliedUnitPrice, nameof(appliedUnitPrice));
        if (appliedUnitPrice > originalUnitPrice)
            throw new ArgumentException("Preco aplicado nao pode superar o original.", nameof(appliedUnitPrice));
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        OriginalUnitPrice = originalUnitPrice;
        AppliedUnitPrice = appliedUnitPrice;
        Quantity = quantity;
    }

    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductName { get; private set; } = null!;
    public decimal OriginalUnitPrice { get; private set; }
    public decimal AppliedUnitPrice { get; private set; }
    public int Quantity { get; private set; }
    public decimal Subtotal => OriginalUnitPrice * Quantity;
    public decimal Total => AppliedUnitPrice * Quantity;
    public decimal Discount => Subtotal - Total;

    internal OrderItem ForOrder(Guid orderId) => new(ProductId, ProductName, OriginalUnitPrice,
        AppliedUnitPrice, Quantity) { OrderId = orderId };
}
