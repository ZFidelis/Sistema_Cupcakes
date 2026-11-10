namespace Cupcakes.Api.Domain;

public sealed class Cart
{
    private Cart() { }

    public Cart(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("Informe o usuario.", nameof(userId));
        Id = Guid.NewGuid();
        UserId = userId;
    }

    public Guid Id { get; private set; }
    public string UserId { get; private set; } = null!;
    public ICollection<CartItem> Items { get; private set; } = new List<CartItem>();
}
