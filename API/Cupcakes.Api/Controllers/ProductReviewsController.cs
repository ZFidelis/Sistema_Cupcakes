using System.Security.Claims;
using Cupcakes.Api.Contracts;
using Cupcakes.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cupcakes.Api.Controllers;

[ApiController]
[Route("api/products/{productId:guid}/reviews")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProductReviewsController(ProductReviewService reviews) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<ProductReviewResponse[]>> List(Guid productId, CancellationToken ct)
    {
        var result = await reviews.ListAsync(productId, ct);
        return result is null ? Problem(statusCode: 404, title: "Produto nao encontrado.") : Ok(result);
    }

    [HttpGet("mine")]
    [Authorize]
    public async Task<ActionResult<ProductReviewResponse>> Mine(Guid productId, CancellationToken ct)
    {
        var result = await reviews.MineAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, productId, ct);
        return result is null ? Problem(statusCode: 404, title: "Avaliacao nao encontrada.") : Ok(result);
    }

    [HttpPut("mine")]
    [Authorize]
    public async Task<ActionResult<ProductReviewResponse>> Save(Guid productId, UpdateProductReviewRequest request, CancellationToken ct)
    {
        var result = await reviews.SaveAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, productId, request, ct);
        if (result.Review is not null) return StatusCode(result.StatusCode, result.Review);
        return result.StatusCode switch
        {
            401 => Unauthorized(),
            404 => Problem(statusCode: 404, title: "Produto nao encontrado."),
            _ => Problem(statusCode: 409, title: "Produto inativo. Nao e possivel avaliar.")
        };
    }
}
