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

    [HttpGet("sales/export.pdf")]
    [Produces(SalesReportPdfExporter.ContentType)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportSalesPdf([FromQuery] SalesReportRequest request, CancellationToken ct)
    {
        var report = await reports.GetAsync(request.ParsedDate(), request.Period == "week", ct);
        var period = report.Period == "week" ? "semanal" : "diario";
        var filename = FormattableString.Invariant($"vendas-simuladas-{period}-{report.StartDate:yyyy-MM-dd}-{report.EndDate:yyyy-MM-dd}.pdf");
        return File(SalesReportPdfExporter.Export(report), SalesReportPdfExporter.ContentType, filename);
    }

    [HttpGet("sales/export.xlsx")]
    [Produces(SalesReportXlsxExporter.ContentType)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportSales([FromQuery] SalesReportRequest request, CancellationToken ct)
    {
        var report = await reports.GetAsync(request.ParsedDate(), request.Period == "week", ct);
        var period = report.Period == "week" ? "semanal" : "diario";
        var filename = FormattableString.Invariant($"vendas-simuladas-{period}-{report.StartDate:yyyy-MM-dd}-{report.EndDate:yyyy-MM-dd}.xlsx");
        return File(SalesReportXlsxExporter.Export(report), SalesReportXlsxExporter.ContentType, filename);
    }
}
