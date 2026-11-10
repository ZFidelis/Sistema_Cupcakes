using System.Text.RegularExpressions;

namespace Cupcakes.Api.Domain;

public sealed class OrderAddress
{
    private OrderAddress() { }

    public OrderAddress(string recipient, string street, string number, string neighborhood,
        string city, string state, string postalCode, string? complement = null)
    {
        Recipient = OrderValidation.Text(recipient, 100, nameof(recipient));
        Street = OrderValidation.Text(street, 150, nameof(street));
        Number = OrderValidation.Text(number, 20, nameof(number));
        Neighborhood = OrderValidation.Text(neighborhood, 80, nameof(neighborhood));
        City = OrderValidation.Text(city, 80, nameof(city));
        State = OrderValidation.Text(state, 2, nameof(state)).ToUpperInvariant();
        if (!Regex.IsMatch(State, "^[A-Z]{2}$"))
            throw new ArgumentException("Informe UF com duas letras.", nameof(state));
        PostalCode = postalCode?.Trim() ?? "";
        if (!Regex.IsMatch(PostalCode, "^[0-9]{8}$"))
            throw new ArgumentException("Informe CEP com oito digitos, sem pontuacao.", nameof(postalCode));
        Complement = string.IsNullOrWhiteSpace(complement)
            ? null : OrderValidation.Text(complement, 100, nameof(complement));
    }

    public Guid OrderId { get; private set; }
    public string Recipient { get; private set; } = null!;
    public string Street { get; private set; } = null!;
    public string Number { get; private set; } = null!;
    public string? Complement { get; private set; }
    public string Neighborhood { get; private set; } = null!;
    public string City { get; private set; } = null!;
    public string State { get; private set; } = null!;
    public string PostalCode { get; private set; } = null!;

    internal OrderAddress ForOrder(Guid orderId) => new(Recipient, Street, Number, Neighborhood,
        City, State, PostalCode, Complement) { OrderId = orderId };
}
