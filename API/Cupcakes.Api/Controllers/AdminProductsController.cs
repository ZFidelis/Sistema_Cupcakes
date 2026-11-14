using Cupcakes.Api.Contracts;
using Cupcakes.Api.Data;
using Cupcakes.Api.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cupcakes.Api.Controllers;

[ApiController]
[Authorize(Roles = ApplicationRoles.Admin)]
[Route("api/admin/products")]
public sealed class AdminProductsController(CupcakesDbContext db) : ControllerBase
{
    [HttpPost("{productId:guid}/activate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Activate(Guid productId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var product = await db.Products.FromSqlInterpolated(
            $"SELECT * FROM \"Products\" WHERE \"Id\" = {productId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (product is null) return Problem(statusCode: 404, title: "Produto nao encontrado.");
        product.Activate();
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("{productId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(Guid productId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var product = await db.Products.FromSqlInterpolated(
            $"SELECT * FROM \"Products\" WHERE \"Id\" = {productId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (product is null) return Problem(statusCode: 404, title: "Produto nao encontrado.");
        product.Deactivate();
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<AdminProductResponse[]>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminProductResponse[]>> List(CancellationToken cancellationToken)
    {
        var products = await db.Products.AsNoTracking()
            .OrderBy(product => product.Name).ThenBy(product => product.Id)
            .Select(product => new AdminProductResponse(product.Id, product.Name, product.Flavor,
                product.Description, product.ImagePath, product.Price, product.PromotionalPrice,
                product.PromotionalPrice ?? product.Price, product.Stock, product.IsActive,
                product.IsActive && product.Stock > 0))
            .ToArrayAsync(cancellationToken);
        return Ok(products);
    }

    [HttpPost]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ProductResponse>> Create(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var product = request.ToDomain();
        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new ProductResponse(product.Id, product.Name,
            product.Flavor, product.Description, product.ImagePath, product.Price, product.PromotionalPrice,
            product.EffectivePrice, product.Stock, product.Stock > 0));
    }

    [HttpPut("{productId:guid}")]
    [ProducesResponseType<AdminProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminProductResponse>> Update(Guid productId, CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var product = await db.Products.FromSqlInterpolated(
            $"SELECT * FROM \"Products\" WHERE \"Id\" = {productId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (product is null) return Problem(statusCode: 404, title: "Produto nao encontrado.");
        product.Update(request.Name, request.Flavor, request.Description, request.ImagePath,
            request.Price!.Value, request.PromotionalPrice, request.Stock!.Value);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(new AdminProductResponse(product.Id, product.Name, product.Flavor, product.Description,
            product.ImagePath, product.Price, product.PromotionalPrice, product.EffectivePrice, product.Stock,
            product.IsActive, product.IsActive && product.Stock > 0));
    }
}
