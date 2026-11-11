using Cupcakes.Api.Contracts;
using Cupcakes.Api.Domain;
using Cupcakes.Api.Identity;
using Cupcakes.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cupcakes.Api.Controllers;

[ApiController]
[Authorize(Roles = ApplicationRoles.Admin)]
[Route("api/admin/orders")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminOrdersController(AdminOrderQueryService orders, OrderStatusService statuses) : ControllerBase
{
    [HttpPut("{orderId:guid}/status")]
    [ProducesResponseType<OrderStatusUpdateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderStatusUpdateResponse>> UpdateStatus(Guid orderId, UpdateOrderStatusRequest request, CancellationToken ct)
    {
        var result = await statuses.UpdateAsync(orderId, Enum.Parse<OrderStatus>(request.Status), ct);
        return result.StatusCode switch
        {
            200 => Ok(result.Response),
            404 => Problem(statusCode: 404, title: "Pedido nao encontrado."),
            _ => Problem(statusCode: 409, title: "Transicao de status invalida.", detail: "Avance somente para a proxima etapa do pedido.")
        };
    }

    [HttpGet]
    [ProducesResponseType<AdminOrderSummaryResponse[]>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminOrderSummaryResponse[]>> List(CancellationToken ct) =>
        Ok(await orders.ListAsync(ct));

    [HttpGet("{orderId:guid}")]
    [ProducesResponseType<AdminOrderDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminOrderDetailResponse>> Get(Guid orderId, CancellationToken ct)
    {
        var order = await orders.GetAsync(orderId, ct);
        return order is null ? Problem(statusCode: 404, title: "Pedido nao encontrado.") : Ok(order);
    }
}
