using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Cupcakes.Api.Contracts;
using Cupcakes.Api.Data;
using Cupcakes.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Cupcakes.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class ProductTests
{
    private static TestApplication app = null!;

    [ClassInitialize]
    public static async Task Initialize(TestContext _)
    {
        app = await TestApplication.CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        Assert.AreEqual(3, await db.Products.CountAsync(), "A migracao deve incluir os tres produtos demonstrativos.");
        Assert.IsFalse(db.Database.HasPendingModelChanges());
    }

    [ClassCleanup]
    public static async Task Cleanup()
    {
        if (app is not null) await app.DisposeAsync();
    }

    [TestInitialize]
    public async Task ClearProducts()
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<CupcakesDbContext>().Products.ExecuteDeleteAsync();
    }

    private static Product Create(string name = "Cupcake", decimal price = 12.50m,
        decimal? promotion = null, int stock = 5, bool active = true, string flavor = "Chocolate") =>
        new(name, flavor, "Descricao de teste", Product.DemoImagePath, price, promotion, stock, active);

    [TestMethod]
    [DataRow("flavor=%20cHoCoLaTe%20", "AB")]
    [DataRow("flavor=Choco", "")]
    [DataRow("flavor=%25", "")]
    [DataRow("flavor=%20%20", "ABC")]
    [DataRow("minPrice=10&maxPrice=10", "B")]
    [DataRow("minPrice=12.50", "AC")]
    [DataRow("maxPrice=10", "B")]
    [DataRow("minPrice=0&maxPrice=9999999999.99", "ABC")]
    [DataRow("onPromotion=true", "BC")]
    [DataRow("onPromotion=false", "A")]
    [DataRow("flavor=chocolate&minPrice=10&maxPrice=12&onPromotion=true", "B")]
    [DataRow("flavor=Morango&onPromotion=false", "")]
    public async Task AnonymousFiltersCombineUsingEffectivePriceAndOnlyActiveProducts(string query, string expected)
    {
        var products = new[]
        {
            Create("A", price: 12.50m),
            Create("B", price: 20m, promotion: 10m, stock: 0),
            Create("C", price: 30m, promotion: 15m, flavor: "Morango"),
            Create("D", price: 20m, promotion: 10m, active: false)
        };
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        db.Products.AddRange(products);
        await db.SaveChangesAsync();
        using var client = app.CreateBrowserClient();
        var response = await client.GetAsync("/api/products?" + query);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ProductResponse[]>())!;
        Assert.AreEqual(expected, string.Concat(result.Select(product => product.Name)));
        if (result.Any(product => product.Name == "B"))
        {
            var item = result.Single(product => product.Name == "B");
            Assert.AreEqual(10m, item.EffectivePrice);
            Assert.IsFalse(item.IsAvailable);
        }
        Assert.AreEqual(4, await db.Products.CountAsync());
        CollectionAssert.AreEqual(new[] { 5, 0, 5, 5 }, await db.Products.OrderBy(x => x.Name).Select(x => x.Stock).ToArrayAsync());
    }

    [TestMethod]
    [DataRow("minPrice=-0.01")]
    [DataRow("maxPrice=-1")]
    [DataRow("minPrice=1.001")]
    [DataRow("maxPrice=1.001")]
    [DataRow("minPrice=10000000000")]
    [DataRow("maxPrice=10000000000")]
    [DataRow("minPrice=20&maxPrice=10")]
    [DataRow("minPrice=abc")]
    [DataRow("maxPrice=999999999999999999999999999999999999")]
    [DataRow("onPromotion=maybe")]
    [DataRow("flavor=abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz123456789")]
    public async Task InvalidFiltersReturnValidationProblem(string query)
    {
        using var client = app.CreateBrowserClient();
        var response = await client.GetAsync("/api/products?" + query);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>();
        Assert.IsNotNull(problem);
        Assert.AreEqual(400, problem.Status);
        Assert.IsNotEmpty(problem.Errors);
    }

    [TestMethod]
    public async Task AnonymousCatalogReturnsOnlyActiveProductsWithCurrentPricesAndAvailability()
    {
        var normal = Create("Chocolate", stock: 0);
        var promotion = Create("Baunilha", 14m, 11.90m);
        var inactive = Create("Inativo", active: false);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
            db.Products.AddRange(normal, promotion, inactive);
            await db.SaveChangesAsync();
        }
        using var client = app.CreateBrowserClient();
        var response = await client.GetAsync("/api/products");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var products = (await response.Content.ReadFromJsonAsync<ProductResponse[]>())!;
        CollectionAssert.AreEqual(new[] { promotion.Id, normal.Id }, products.Select(x => x.Id).ToArray());
        Assert.AreEqual(14m, products[0].Price);
        Assert.AreEqual(11.90m, products[0].PromotionalPrice);
        Assert.AreEqual(11.90m, products[0].EffectivePrice);
        Assert.IsTrue(products[0].IsAvailable);
        Assert.AreEqual(12.50m, products[1].EffectivePrice);
        Assert.IsNull(products[1].PromotionalPrice);
        Assert.AreEqual(0, products[1].Stock);
        Assert.IsFalse(products[1].IsAvailable);
        Assert.AreEqual(Product.DemoImagePath, products[0].ImagePath);
        Assert.AreEqual("Baunilha", products[0].Name);
        Assert.AreEqual("Chocolate", products[0].Flavor);
        Assert.AreEqual("Descricao de teste", products[0].Description);
    }

    [TestMethod]
    public async Task CatalogReturnsEmptyArrayWhenNoActiveProductsExist()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        db.Products.Add(Create(active: false));
        await db.SaveChangesAsync();
        using var client = app.CreateBrowserClient();
        var products = await client.GetFromJsonAsync<ProductResponse[]>("/api/products");
        Assert.IsNotNull(products);
        Assert.HasCount(0, products);
    }

    [TestMethod]
    public async Task UpdatedPriceIsReadFromDatabaseWithoutChangingStock()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        var product = Create();
        db.Products.Add(product);
        await db.SaveChangesAsync();
        await db.Products.ExecuteUpdateAsync(update => update.SetProperty(x => x.Price, 15.75m));
        using var client = app.CreateBrowserClient();
        var products = (await client.GetFromJsonAsync<ProductResponse[]>("/api/products"))!;
        Assert.AreEqual(15.75m, products.Single().EffectivePrice);
        Assert.AreEqual(5, await db.Products.Select(x => x.Stock).SingleAsync());
    }

    [TestMethod]
    [DataRow("price")]
    [DataRow("promotion")]
    [DataRow("stock")]
    [DataRow("name")]
    [DataRow("image")]
    public async Task DatabaseRejectsInvalidBusinessValues(string field)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        db.Products.Add(Create());
        await db.SaveChangesAsync();
        // Comandos fixos de teste: nao ha entrada de usuario nem interpolacao de SQL.
        var sql = field switch
        {
            "price" => "UPDATE \"Products\" SET \"Price\" = 0",
            "promotion" => "UPDATE \"Products\" SET \"PromotionalPrice\" = \"Price\"",
            "stock" => "UPDATE \"Products\" SET \"Stock\" = -1",
            "name" => "UPDATE \"Products\" SET \"Name\" = '   '",
            "image" => "UPDATE \"Products\" SET \"ImagePath\" = '/arquivo-inexistente.svg'",
            _ => throw new ArgumentException(nameof(field))
        };
        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
        Assert.AreEqual(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("-1")]
    [DataRow("1.001")]
    [DataRow("10000000000")]
    public void ProductRejectsInvalidPrice(string value)
    {
        var price = decimal.Parse(value, CultureInfo.InvariantCulture);
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(price: price));
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("-1")]
    [DataRow("12.50")]
    [DataRow("13")]
    [DataRow("1.001")]
    public void ProductRejectsInvalidPromotion(string value)
    {
        var promotion = decimal.Parse(value, CultureInfo.InvariantCulture);
        Assert.Throws<ArgumentException>(() => Create(promotion: promotion));
    }

    [TestMethod]
    public void ProductTrimsTextAndAcceptsMinimumMonetaryValue()
    {
        var product = new Product("  Cupcake  ", " Baunilha ", " Descricao ", Product.DemoImagePath, .02m, .01m, 0);
        Assert.AreEqual("Cupcake", product.Name);
        Assert.AreEqual("Baunilha", product.Flavor);
        Assert.AreEqual("Descricao", product.Description);
        Assert.AreEqual(.01m, product.EffectivePrice);
    }

    [TestMethod]
    public void ProductRejectsEmptyOrOversizedTextUnknownImageAndNegativeStock()
    {
        Assert.Throws<ArgumentException>(() => Create(name: "  "));
        Assert.Throws<ArgumentException>(() => Create(name: new string('a', 101)));
        Assert.Throws<ArgumentException>(() => new Product("Cupcake", " ", "Descricao", Product.DemoImagePath, 10m, null, 1));
        Assert.Throws<ArgumentException>(() => new Product("Cupcake", "Sabor", new string('a', 1001), Product.DemoImagePath, 10m, null, 1));
        Assert.Throws<ArgumentException>(() => new Product("Cupcake", "Sabor", "Descricao", "/inexistente.svg", 10m, null, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(stock: -1));
    }
}
