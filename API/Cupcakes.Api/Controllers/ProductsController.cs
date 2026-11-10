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
    public async Task<ActionResult<ProductResponse[]>> Get(CancellationToken cancellationToken)
    {
        var products = await dbContext.Products.AsNoTracking()
            .Where(product => product.IsActive)
            .OrderBy(product => product.Name).ThenBy(product => product.Id)
            .Select(product => new ProductResponse(product.Id, product.Name, product.Flavor,
                product.Description, product.ImagePath, product.Price, product.PromotionalPrice,
                product.PromotionalPrice ?? product.Price, product.Stock, product.Stock > 0))
            .ToArrayAsync(cancellationToken);
        return Ok(products);
    }
}
