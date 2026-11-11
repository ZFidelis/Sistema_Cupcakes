using Cupcakes.Api.Contracts;
using Cupcakes.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cupcakes.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/products")]
public sealed class ProductsController(CupcakesDbContext dbContext) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ProductResponse[]>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProductResponse[]>> Get([FromQuery] ProductFilterRequest filters,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Products.AsNoTracking().Where(product => product.IsActive);
        if (!string.IsNullOrWhiteSpace(filters.Flavor))
        {
            var flavor = filters.Flavor.Trim().ToLowerInvariant();
            query = query.Where(product => product.Flavor.ToLower() == flavor);
        }
        if (filters.MinPrice is { } minimum)
            query = query.Where(product => (product.PromotionalPrice ?? product.Price) >= minimum);
        if (filters.MaxPrice is { } maximum)
            query = query.Where(product => (product.PromotionalPrice ?? product.Price) <= maximum);
        if (filters.OnPromotion is { } promotion)
            query = query.Where(product => product.PromotionalPrice.HasValue == promotion);

        var products = await query
            .OrderBy(product => product.Name).ThenBy(product => product.Id)
            .Select(product => new ProductResponse(product.Id, product.Name, product.Flavor,
                product.Description, product.ImagePath, product.Price, product.PromotionalPrice,
                product.PromotionalPrice ?? product.Price, product.Stock, product.Stock > 0))
            .ToArrayAsync(cancellationToken);
        return Ok(products);
    }
}
