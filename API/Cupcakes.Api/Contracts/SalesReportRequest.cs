using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Cupcakes.Api.Contracts;

public sealed class SalesReportRequest : IValidatableObject
{
    [Required]
    public string Date { get; init; } = "";
    [Required]
    [RegularExpression("^(day|week)$")]
    public string Period { get; init; } = "day";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!DateOnly.TryParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            || date.Year is < 2 or > 9998)
            yield return new ValidationResult("Informe uma data ISO yyyy-MM-dd entre os anos 0002 e 9998.", [nameof(Date)]);
    }

    public DateOnly ParsedDate() => DateOnly.ParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
