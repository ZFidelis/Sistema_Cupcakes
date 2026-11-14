using System.ComponentModel.DataAnnotations;

namespace Cupcakes.Api.Contracts;

public class OrderFilterRequest : IValidatableObject
{
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    [RegularExpression("^(Recebido|EmPreparo|SaiuParaEntrega|Entregue)$")]
    public string? Status { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartDate > EndDate)
            yield return new("A data inicial não pode ser posterior à final.", [nameof(StartDate), nameof(EndDate)]);
        if (StartDate?.Year is < 2 or > 9998 || EndDate?.Year is < 2 or > 9998)
            yield return new("Informe datas entre os anos 0002 e 9998.", [nameof(StartDate), nameof(EndDate)]);
    }
}

public sealed class AdminOrderFilterRequest : OrderFilterRequest
{
    public Guid? OrderId { get; init; }
}
