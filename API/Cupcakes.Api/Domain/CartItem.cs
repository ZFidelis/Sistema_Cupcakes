namespace Cupcakes.Api.Domain;

public sealed class CartItem
{
    private CartItem() { }

    public CartItem(Guid cartId, Guid productId, int quantity)
    {
        CartId = cartId;
        ProductId = productId;
        SetQuantity(quantity);
    }

    public Guid CartId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }
    public Product Product { get; private set; } = null!;

    public void SetQuantity(int quantity)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        Quantity = quantity;
    }
}
