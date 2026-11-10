using System.ComponentModel.DataAnnotations;

namespace Cupcakes.Api.Contracts;

public sealed record AddCartItemRequest(
    Guid ProductId,
    [Range(1, int.MaxValue)] int Quantity);

public sealed record UpdateCartItemRequest([Range(1, int.MaxValue)] int Quantity);
