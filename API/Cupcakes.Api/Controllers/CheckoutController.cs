using System.Security.Claims;
using Cupcakes.Api.Contracts;
using Cupcakes.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cupcakes.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/checkout")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CheckoutController(CheckoutPreviewService checkout) : ControllerBase
{
    [HttpPost("preview")]
    [ProducesResponseType<CheckoutPreviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<CheckoutPreviewResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CheckoutPreviewResponse>> Preview(CheckoutPreviewRequest request, CancellationToken ct)
    {
        var response = await checkout.PreviewAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, request.Address.ToDomain(), ct);
        return StatusCode(response.CanProceed ? StatusCodes.Status200OK : StatusCodes.Status409Conflict, response);
    }
}
