using Microsoft.EntityFrameworkCore;

namespace Cupcakes.Api.Data;

public sealed class CupcakesDbContext(DbContextOptions<CupcakesDbContext> options)
    : DbContext(options)
{
}
