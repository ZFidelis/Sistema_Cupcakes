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
}
