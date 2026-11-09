using Cupcakes.Api.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Cupcakes.Api.Controllers;

[ApiController]
[Route("api/status")]
public sealed class StatusController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiStatusResponse>(StatusCodes.Status200OK)]
    public ActionResult<ApiStatusResponse> Get() => Ok(new ApiStatusResponse("ok"));
}
