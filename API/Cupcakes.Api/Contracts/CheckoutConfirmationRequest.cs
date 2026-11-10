using System.ComponentModel.DataAnnotations;
using Cupcakes.Api.Domain;

namespace Cupcakes.Api.Contracts;

public sealed class CheckoutConfirmationRequest : IValidatableObject
{
    public Guid ConfirmationId { get; init; }
    [Required] public CheckoutAddressRequest Address { get; init; } = null!;
    [Required, RegularExpression("^(Pix|Cartao)$")]
    public string PaymentMethod { get; init; } = "";
    [Required, RegularExpression("^(Aprovar|Recusar)$")]
    public string Simulation { get; init; } = "";
    [Required, MinLength(1)] public List<CheckoutExpectedItem> ExpectedItems { get; init; } = null!;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ConfirmationId == Guid.Empty)
            yield return new("Informe um identificador de confirmacao novo para esta compra.", [nameof(ConfirmationId)]);
        if (ExpectedItems is not null && (ExpectedItems.Any(item => item is null)
            || ExpectedItems.Select(item => item.ProductId).Distinct().Count() != ExpectedItems.Count))
            yield return new("Informe produtos unicos e validos no resumo esperado.", [nameof(ExpectedItems)]);
    }
}

public sealed class CheckoutExpectedItem : IValidatableObject
{
    public Guid ProductId { get; init; }
    [Range(1, int.MaxValue)] public int Quantity { get; init; }
    public decimal Price { get; init; }
    public decimal EffectivePrice { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ProductId == Guid.Empty) return [new("Informe o produto.", [nameof(ProductId)])];
        try
        {
            OrderValidation.Money(Price, nameof(Price));
            OrderValidation.Money(EffectivePrice, nameof(EffectivePrice));
            if (EffectivePrice > Price) return [new("O preco aplicado nao pode superar o original.", [nameof(EffectivePrice)])];
            return [];
        }
        catch (ArgumentException error)
        {
            return [new(error.Message, [error.ParamName!])];
        }
    }
}
