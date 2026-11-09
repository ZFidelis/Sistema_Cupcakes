using Cupcakes.Api.Contracts;
using Cupcakes.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cupcakes.Api.Controllers;

[ApiController]
[Route("api/status")]
public sealed class StatusController(CupcakesDbContext dbContext) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiStatusResponse>(StatusCodes.Status200OK)]
    public ActionResult<ApiStatusResponse> Get() => Ok(new ApiStatusResponse("ok"));

    [HttpGet("database")]
    [ProducesResponseType<ApiStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiStatusResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ApiStatusResponse>> GetDatabase(CancellationToken cancellationToken)
    {
        var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

        return canConnect
            ? Ok(new ApiStatusResponse("ok"))
            : StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiStatusResponse("unavailable"));
    }
}
