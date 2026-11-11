using Cupcakes.Api.Contracts;
using Cupcakes.Api.Identity;
using Cupcakes.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cupcakes.Api.Controllers;

[ApiController]
[Authorize(Roles = ApplicationRoles.Admin)]
[Route("api/admin/orders")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminOrdersController(AdminOrderQueryService orders) : ControllerBase
{
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
