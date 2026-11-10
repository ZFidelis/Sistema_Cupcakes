namespace Cupcakes.Api.Domain;

internal static class OrderValidation
{
    internal static string Text(string value, int limit, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > limit)
            throw new ArgumentException($"Informe um texto de 1 a {limit} caracteres.", parameter);
        return value.Trim();
    }

    internal static void Money(decimal value, string parameter)
    {
        if (value <= 0 || value > Product.MaximumPrice || decimal.Round(value, 2) != value)
            throw new ArgumentOutOfRangeException(parameter, "Informe um valor positivo com ate duas casas decimais.");
    }
}
