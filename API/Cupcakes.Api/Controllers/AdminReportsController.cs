using Cupcakes.Api.Contracts;
using Cupcakes.Api.Identity;
using Cupcakes.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cupcakes.Api.Controllers;

[ApiController]
[Authorize(Roles = ApplicationRoles.Admin)]
[Route("api/admin/reports")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminReportsController(SalesReportService reports) : ControllerBase
{
    [HttpGet("sales")]
    [ProducesResponseType<SalesReportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SalesReportResponse>> GetSales([FromQuery] SalesReportRequest request, CancellationToken ct) =>
        Ok(await reports.GetAsync(request.ParsedDate(), request.Period == "week", ct));
}
