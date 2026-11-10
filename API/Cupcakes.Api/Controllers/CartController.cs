using System.Security.Claims;
using Cupcakes.Api.Contracts;
using Cupcakes.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cupcakes.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/cart")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CartController(CartService carts) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    [ProducesResponseType<CartResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CartResponse>> Get(CancellationToken ct) => Ok(await carts.GetAsync(UserId, ct));

    [HttpPost("items")]
    public async Task<ActionResult<CartResponse>> Add(AddCartItemRequest request, CancellationToken ct) =>
        ToResponse(await carts.AddAsync(UserId, request.ProductId, request.Quantity, ct));

    [HttpPut("items/{productId:guid}")]
    public async Task<ActionResult<CartResponse>> Update(Guid productId, UpdateCartItemRequest request, CancellationToken ct) =>
        ToResponse(await carts.UpdateAsync(UserId, productId, request.Quantity, ct));

    [HttpDelete("items/{productId:guid}")]
    public async Task<ActionResult<CartResponse>> Remove(Guid productId, CancellationToken ct) =>
        ToResponse(await carts.RemoveAsync(UserId, productId, ct));

    private ActionResult<CartResponse> ToResponse(CartChangeResult result) => result.Cart is not null
        ? Ok(result.Cart)
        : Problem(statusCode: result.StatusCode, title: result.Error);
}
