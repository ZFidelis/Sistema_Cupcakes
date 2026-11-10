using System.ComponentModel.DataAnnotations;
using Cupcakes.Api.Domain;

namespace Cupcakes.Api.Contracts;

public sealed class CheckoutPreviewRequest
{
    [Required]
    public CheckoutAddressRequest Address { get; init; } = null!;
}

public sealed class CheckoutAddressRequest : IValidatableObject
{
    public string Recipient { get; init; } = "";
    public string Street { get; init; } = "";
    public string Number { get; init; } = "";
    public string Neighborhood { get; init; } = "";
    public string City { get; init; } = "";
    public string State { get; init; } = "";
    public string PostalCode { get; init; } = "";
    public string? Complement { get; init; }

    public OrderAddress ToDomain() => new(Recipient, Street, Number, Neighborhood, City, State, PostalCode, Complement);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        try
        {
            _ = ToDomain();
            return [];
        }
        catch (ArgumentException error)
        {
            return [new ValidationResult(error.Message, [error.ParamName ?? "address"])];
        }
    }
}
