using System.Security.Claims;
using Cupcakes.Api.Contracts;
using Cupcakes.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cupcakes.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/orders")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OrdersController(OrderQueryService orders) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<OrderSummaryResponse[]>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<OrderSummaryResponse[]>> List(CancellationToken ct) =>
        Ok(await orders.ListAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct));

    [HttpGet("{orderId:guid}")]
    [ProducesResponseType<OrderDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<OrderDetailResponse>> Get(Guid orderId, CancellationToken ct)
    {
        var order = await orders.GetAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, orderId, ct);
        return order is null ? Problem(statusCode: 404, title: "Pedido nao encontrado.") : Ok(order);
    }
}
