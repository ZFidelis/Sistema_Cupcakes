namespace Cupcakes.Api.Contracts;

public sealed record CheckoutAddressResponse(string Recipient, string Street, string Number,
    string Neighborhood, string City, string State, string PostalCode, string? Complement);

public sealed record CheckoutIssue(string Code, string Message, Guid? ProductId = null);

public sealed record CheckoutPreviewResponse(CheckoutAddressResponse Address, CartResponse Cart,
    bool CanProceed, CheckoutIssue[] Issues, string Notice);
