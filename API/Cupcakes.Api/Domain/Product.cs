namespace Cupcakes.Api.Domain;

public sealed class Product
{
    public const string DemoImagePath = "/images/products/cupcake.svg";
    public const decimal MaximumPrice = 9999999999.99m;

    private Product() { }

    public Product(string name, string flavor, string description, string imagePath,
        decimal price, decimal? promotionalPrice, int stock, bool isActive = true)
    {
        Update(name, flavor, description, imagePath, price, promotionalPrice, stock);
        Id = Guid.NewGuid();
        IsActive = isActive;
    }

    public void Update(string name, string flavor, string description, string imagePath,
        decimal price, decimal? promotionalPrice, int stock)
    {
        var validName = RequiredText(name, 100, nameof(name));
        var validFlavor = RequiredText(flavor, 60, nameof(flavor));
        var validDescription = RequiredText(description, 1000, nameof(description));
        if (imagePath != DemoImagePath)
            throw new ArgumentException("Escolha uma imagem do conjunto estatico permitido.", nameof(imagePath));
        ValidatePrice(price, nameof(price));
        if (promotionalPrice is { } promotion)
        {
            ValidatePrice(promotion, nameof(promotionalPrice));
            if (promotion >= price)
                throw new ArgumentException("O preco promocional deve ser menor que o preco normal.", nameof(promotionalPrice));
        }
        if (stock < 0) throw new ArgumentOutOfRangeException(nameof(stock));

        Name = validName;
        Flavor = validFlavor;
        Description = validDescription;
        ImagePath = imagePath;
        Price = price;
        PromotionalPrice = promotionalPrice;
        Stock = stock;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string Flavor { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public string ImagePath { get; private set; } = null!;
    public decimal Price { get; private set; }
    public decimal? PromotionalPrice { get; private set; }
    public int Stock { get; private set; }
    public bool IsActive { get; private set; }
    public decimal EffectivePrice => PromotionalPrice ?? Price;

    public void Deactivate() => IsActive = false;

    private static string RequiredText(string value, int maxLength, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maxLength)
            throw new ArgumentException($"Informe um texto de 1 a {maxLength} caracteres.", parameter);
        return value.Trim();
    }

    private static void ValidatePrice(decimal value, string parameter)
    {
        if (value <= 0 || value > MaximumPrice || decimal.Round(value, 2) != value)
            throw new ArgumentOutOfRangeException(parameter, "Informe um preco positivo com ate duas casas decimais.");
    }
}
