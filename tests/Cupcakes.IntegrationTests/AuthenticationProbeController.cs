using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cupcakes.IntegrationTests;

[ApiController]
[Route("__tests/authentication")]
public sealed class AuthenticationProbeController : ControllerBase
{
    [HttpGet]
    [Authorize]
    public IActionResult Get() => Ok(new
    {
        id = User.FindFirstValue(ClaimTypes.NameIdentifier),
        customer = User.IsInRole("Cliente")
    });

    [HttpGet("admin")]
    [Authorize(Roles = "Administrador")]
    public IActionResult Admin() => Ok();
}
