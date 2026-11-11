using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Cupcakes.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateOrderStatusRequest
{
    [Required]
    [RegularExpression("^(Recebido|EmPreparo|SaiuParaEntrega|Entregue)$")]
    public string Status { get; init; } = "";
}
