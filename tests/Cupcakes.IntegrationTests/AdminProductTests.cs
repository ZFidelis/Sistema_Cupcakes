using System.Net;
using System.Net.Http.Json;
using Cupcakes.Api.Contracts;
using Cupcakes.Api.Data;
using Cupcakes.Api.Domain;
using Cupcakes.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cupcakes.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class AdminProductTests
{
    private static TestApplication app = null!;
    private const string Password = "CupcakeAdmin123";
    private const string AdminEmail = "products-admin@example.invalid";
    private const string CustomerEmail = "products-customer@example.invalid";

    [ClassInitialize]
    public static async Task Initialize(TestContext _)
    {
        app = await TestApplication.CreateAsync();
        using var scope = app.Services.CreateScope();
        Assert.IsTrue((await scope.ServiceProvider.GetRequiredService<AdminProvisioningService>()
            .ProvisionAsync(AdminEmail, Password)).Succeeded);
        Assert.IsTrue((await scope.ServiceProvider.GetRequiredService<RegistrationService>()
            .RegisterAsync(new RegisterRequest { Email = CustomerEmail, Password = Password }, default)).Result.Succeeded);
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
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        await db.Orders.ExecuteDeleteAsync();
        await db.Products.ExecuteDeleteAsync();
    }

    private static Dictionary<string, object?> Payload() => new()
    {
        ["name"] = "  Cupcake de teste  ", ["flavor"] = "  Chocolate  ",
        ["description"] = "  Descricao demonstrativa  ", ["imagePath"] = Product.DemoImagePath,
        ["price"] = 15.50m, ["promotionalPrice"] = null, ["stock"] = 7
    };

    private static async Task RefreshTokenAsync(HttpClient client)
    {
        var token = await client.GetFromJsonAsync<AntiforgeryResponse>("/api/auth/antiforgery");
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token!.RequestToken);
    }

    private static async Task<HttpClient> LoginAsync(string email)
    {
        var client = app.CreateBrowserClient();
        await RefreshTokenAsync(client);
        Assert.AreEqual(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password })).StatusCode);
        await RefreshTokenAsync(client);
        return client;
    }

    private static async Task<Product> ExistingProductAsync(bool active = true)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        var product = new Product("Original", "Morango", "Descricao original", Product.DemoImagePath, 20m, 18m, 9, active);
        db.Products.Add(product); await db.SaveChangesAsync();
        return product;
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task AdministratorEditsDetailsAndStockWithoutChangingIdOrActiveFlag(bool active)
    {
        var original = await ExistingProductAsync(active);
        using var client = await LoginAsync(AdminEmail);
        var payload = Payload(); payload["stock"] = 0;
        var response = await client.PutAsJsonAsync($"/api/admin/products/{original.Id}", payload);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var edited = (await response.Content.ReadFromJsonAsync<AdminProductResponse>())!;
        Assert.AreEqual(original.Id, edited.Id);
        Assert.AreEqual(active, edited.IsActive);
        Assert.IsFalse(edited.IsAvailable);
        Assert.AreEqual("Cupcake de teste", edited.Name);
        Assert.AreEqual("Chocolate", edited.Flavor);
        Assert.AreEqual("Descricao demonstrativa", edited.Description);
        Assert.AreEqual(15.50m, edited.Price);
        Assert.IsNull(edited.PromotionalPrice);
        Assert.AreEqual(15.50m, edited.EffectivePrice);
        Assert.AreEqual(0, edited.Stock);
        var listed = (await client.GetFromJsonAsync<AdminProductResponse[]>("/api/admin/products"))!.Single();
        Assert.AreEqual(edited, listed);
        using var visitor = app.CreateBrowserClient();
        var catalog = (await visitor.GetFromJsonAsync<ProductResponse[]>("/api/products"))!;
        Assert.HasCount(active ? 1 : 0, catalog);
        if (active) Assert.AreEqual(edited.Name, catalog.Single().Name);
        var repeated = await client.PutAsJsonAsync($"/api/admin/products/{original.Id}", payload);
        Assert.AreEqual(edited, await repeated.Content.ReadFromJsonAsync<AdminProductResponse>());
    }

    [TestMethod]
    public async Task EditingProductPreservesHistoricalOrderValues()
    {
        var product = await ExistingProductAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        var owner = await db.Users.Where(user => user.Email == CustomerEmail).Select(user => user.Id).SingleAsync();
        var order = new Order(owner, SimulatedPaymentMethod.Pix,
            new OrderAddress("Pessoa Ficticia", "Rua", "1", "Bairro", "Cidade", "SP", "01234567", null),
            [new OrderItem(product.Id, product.Name, product.Price, product.EffectivePrice, 2)]);
        db.Orders.Add(order); await db.SaveChangesAsync();
        using var client = await LoginAsync(AdminEmail);
        var payload = Payload(); payload["promotionalPrice"] = 12.90m;
        var response = await client.PutAsJsonAsync($"/api/admin/products/{product.Id}", payload);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(12.90m, (await response.Content.ReadFromJsonAsync<AdminProductResponse>())!.EffectivePrice);
        db.ChangeTracker.Clear();
        var historical = await db.Orders.Include(value => value.Items).SingleAsync();
        Assert.AreEqual(order.Id, historical.Id);
        Assert.AreEqual(36m, historical.Total);
        Assert.AreEqual("Original", historical.Items.Single().ProductName);
        Assert.AreEqual(20m, historical.Items.Single().OriginalUnitPrice);
        Assert.AreEqual(18m, historical.Items.Single().AppliedUnitPrice);
    }

    [TestMethod]
    public async Task MissingProductReturnsNotFoundWithoutCreatingOne()
    {
        using var client = await LoginAsync(AdminEmail);
        Assert.AreEqual(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/admin/products/{Guid.NewGuid()}", Payload())).StatusCode);
        using var scope = app.Services.CreateScope();
        Assert.AreEqual(0, await scope.ServiceProvider.GetRequiredService<CupcakesDbContext>().Products.CountAsync());
    }

    [TestMethod]
    [DataRow("visitor", 401)]
    [DataRow("customer", 403)]
    [DataRow("adminWithoutToken", 400)]
    public async Task EditingRequiresAdministratorAndWriteToken(string actor, int status)
    {
        var product = await ExistingProductAsync();
        using var client = actor == "visitor" ? app.CreateBrowserClient() : await LoginAsync(actor == "customer" ? CustomerEmail : AdminEmail);
        if (actor == "adminWithoutToken") client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Assert.AreEqual((HttpStatusCode)status, (await client.PutAsJsonAsync($"/api/admin/products/{product.Id}", Payload())).StatusCode);
        using var scope = app.Services.CreateScope();
        Assert.AreEqual("Original", await scope.ServiceProvider.GetRequiredService<CupcakesDbContext>().Products.Select(value => value.Name).SingleAsync());
    }

    [TestMethod]
    [DataRow("name")]
    [DataRow("price")]
    [DataRow("promotion")]
    [DataRow("stock")]
    [DataRow("missingStock")]
    [DataRow("image")]
    [DataRow("active")]
    public async Task InvalidEditDoesNotPartiallyChangeProduct(string field)
    {
        var product = await ExistingProductAsync();
        var payload = Payload();
        switch (field)
        {
            case "name": payload["name"] = " "; break;
            case "price": payload["price"] = 1.001m; break;
            case "promotion": payload["promotionalPrice"] = 16m; break;
            case "stock": payload["stock"] = -1; break;
            case "missingStock": payload.Remove("stock"); break;
            case "image": payload["imagePath"] = "/imagem-inexistente.svg"; break;
            case "active": payload["isActive"] = false; break;
        }
        using var client = await LoginAsync(AdminEmail);
        var response = await client.PutAsJsonAsync($"/api/admin/products/{product.Id}", payload);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.IsNotEmpty((await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors);
        using var scope = app.Services.CreateScope();
        var saved = await scope.ServiceProvider.GetRequiredService<CupcakesDbContext>().Products.SingleAsync();
        Assert.AreEqual(product.Id, saved.Id);
        Assert.AreEqual("Original", saved.Name);
        Assert.AreEqual(20m, saved.Price);
        Assert.AreEqual(18m, saved.PromotionalPrice);
        Assert.AreEqual(9, saved.Stock);
        Assert.IsTrue(saved.IsActive);
    }

    [TestMethod]
    public void DomainRejectsInvalidEditBeforeChangingAnyField()
    {
        var product = new Product("Original", "Morango", "Descricao", Product.DemoImagePath, 20m, 18m, 9, false);
        Assert.Throws<ArgumentOutOfRangeException>(() => product.Update("Novo", "Chocolate", "Nova descricao", Product.DemoImagePath, 10m, null, -1));
        Assert.AreEqual("Original", product.Name);
        Assert.AreEqual("Morango", product.Flavor);
        Assert.AreEqual(20m, product.Price);
        Assert.AreEqual(9, product.Stock);
        Assert.IsFalse(product.IsActive);
    }

    [TestMethod]
    public async Task AdministrativeListIncludesInactiveProductsAndOrdersByNameThenId()
    {
        var inactive = new Product("Cupcake A", "Chocolate", "Descricao inativa", Product.DemoImagePath, 20m, 15m, 9, false);
        var available = new Product("Cupcake B", "Morango", "Descricao ativa", Product.DemoImagePath, 12.50m, null, 3);
        var unavailable = new Product("Cupcake B", "Baunilha", "Sem estoque", Product.DemoImagePath, 10m, null, 0);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        db.Products.AddRange(unavailable, available, inactive);
        await db.SaveChangesAsync();
        using var client = await LoginAsync(AdminEmail);
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        var response = await client.GetAsync("/api/admin/products");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(response.Headers.CacheControl!.NoStore);
        var products = (await response.Content.ReadFromJsonAsync<AdminProductResponse[]>())!;
        var expected = new[] { inactive, available, unavailable }.OrderBy(x => x.Name).ThenBy(x => x.Id).ToArray();
        CollectionAssert.AreEqual(expected.Select(x => x.Id).ToArray(), products.Select(x => x.Id).ToArray());
        foreach (var product in expected)
        {
            var actual = products.Single(x => x.Id == product.Id);
            Assert.AreEqual(new AdminProductResponse(product.Id, product.Name, product.Flavor, product.Description,
                product.ImagePath, product.Price, product.PromotionalPrice, product.EffectivePrice, product.Stock,
                product.IsActive, product.IsActive && product.Stock > 0), actual);
        }
        Assert.IsFalse(products.Single(x => x.Id == inactive.Id).IsAvailable);
        Assert.IsFalse(products.Single(x => x.Id == unavailable.Id).IsAvailable);
        var repeated = (await client.GetFromJsonAsync<AdminProductResponse[]>("/api/admin/products"))!;
        CollectionAssert.AreEqual(products, repeated);
        Assert.AreEqual(12, await db.Products.SumAsync(x => x.Stock));
        Assert.AreEqual(3, await db.Products.CountAsync());
        using var visitor = app.CreateBrowserClient();
        var publicProducts = (await visitor.GetFromJsonAsync<ProductResponse[]>("/api/products"))!;
        Assert.HasCount(2, publicProducts);
        Assert.IsFalse(publicProducts.Any(x => x.Id == inactive.Id));
    }

    [TestMethod]
    public async Task AdministrativeListReturnsEmptyArrayWithoutProducts()
    {
        using var client = await LoginAsync(AdminEmail);
        var response = await client.GetAsync("/api/admin/products");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.HasCount(0, (await response.Content.ReadFromJsonAsync<AdminProductResponse[]>())!);
    }

    [TestMethod]
    public async Task AdministrativeListReadsCurrentPricesStockAndActiveFlag()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        db.Products.Add(new Product("Cupcake", "Chocolate", "Descricao", Product.DemoImagePath, 20m, null, 4));
        await db.SaveChangesAsync();
        using var client = await LoginAsync(AdminEmail);
        var before = (await client.GetFromJsonAsync<AdminProductResponse[]>("/api/admin/products"))!.Single();
        Assert.IsTrue(before.IsActive && before.IsAvailable);
        await db.Products.ExecuteUpdateAsync(update => update.SetProperty(x => x.Price, 30m)
            .SetProperty(x => x.PromotionalPrice, 25m).SetProperty(x => x.Stock, 2).SetProperty(x => x.IsActive, false));
        var after = (await client.GetFromJsonAsync<AdminProductResponse[]>("/api/admin/products"))!.Single();
        Assert.AreEqual(before.Id, after.Id);
        Assert.AreEqual(30m, after.Price);
        Assert.AreEqual(25m, after.PromotionalPrice);
        Assert.AreEqual(25m, after.EffectivePrice);
        Assert.AreEqual(2, after.Stock);
        Assert.IsFalse(after.IsActive || after.IsAvailable);
    }

    [TestMethod]
    [DataRow("visitor", 401)]
    [DataRow("customer", 403)]
    public async Task NonAdministratorsCannotReadAdministrativeList(string actor, int status)
    {
        using var client = actor == "visitor" ? app.CreateBrowserClient() : await LoginAsync(CustomerEmail);
        Assert.AreEqual((HttpStatusCode)status, (await client.GetAsync("/api/admin/products")).StatusCode);
    }

    [TestMethod]
    [DataRow(false, 7)]
    [DataRow(true, 7)]
    [DataRow(true, 0)]
    public async Task AdministratorCreatesActiveProductPersistedAndVisibleInPublicCatalog(bool promotion, int stock)
    {
        using var client = await LoginAsync(AdminEmail);
        var payload = Payload();
        payload["stock"] = stock;
        if (promotion) payload["promotionalPrice"] = 12.90m;
        var response = await client.PostAsJsonAsync("/api/admin/products", payload);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
        Assert.AreNotEqual(Guid.Empty, created.Id);
        Assert.AreEqual("Cupcake de teste", created.Name);
        Assert.AreEqual("Chocolate", created.Flavor);
        Assert.AreEqual("Descricao demonstrativa", created.Description);
        Assert.AreEqual(Product.DemoImagePath, created.ImagePath);
        Assert.AreEqual(15.50m, created.Price);
        Assert.AreEqual(promotion ? 12.90m : (decimal?)null, created.PromotionalPrice);
        Assert.AreEqual(promotion ? 12.90m : 15.50m, created.EffectivePrice);
        Assert.AreEqual(stock, created.Stock);
        Assert.AreEqual(stock > 0, created.IsAvailable);
        using var scope = app.Services.CreateScope();
        var persisted = await scope.ServiceProvider.GetRequiredService<CupcakesDbContext>().Products.AsNoTracking().SingleAsync();
        Assert.AreEqual(created.Id, persisted.Id);
        Assert.AreEqual(created.Price, persisted.Price);
        Assert.AreEqual(created.PromotionalPrice, persisted.PromotionalPrice);
        Assert.AreEqual(stock, persisted.Stock);
        Assert.IsTrue(persisted.IsActive);
        using var visitor = app.CreateBrowserClient();
        var catalog = (await visitor.GetFromJsonAsync<ProductResponse[]>("/api/products?flavor=Chocolate"))!;
        Assert.AreEqual(created, catalog.Single());
    }

    [TestMethod]
    [DataRow("visitor", 401)]
    [DataRow("customer", 403)]
    public async Task NonAdministratorsCannotCreateProducts(string actor, int status)
    {
        using var client = actor == "visitor" ? app.CreateBrowserClient() : await LoginAsync(CustomerEmail);
        await RefreshTokenAsync(client);
        var response = await client.PostAsJsonAsync("/api/admin/products", Payload());
        Assert.AreEqual((HttpStatusCode)status, response.StatusCode);
        Assert.IsNull(response.Headers.Location);
        using var scope = app.Services.CreateScope();
        Assert.AreEqual(0, await scope.ServiceProvider.GetRequiredService<CupcakesDbContext>().Products.CountAsync());
    }

    [TestMethod]
    public async Task AdministratorNeedsExistingAntiforgeryToken()
    {
        using var client = await LoginAsync(AdminEmail);
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/admin/products", Payload())).StatusCode);
        using var scope = app.Services.CreateScope();
        Assert.AreEqual(0, await scope.ServiceProvider.GetRequiredService<CupcakesDbContext>().Products.CountAsync());
    }

    [TestMethod]
    [DataRow("emptyName")]
    [DataRow("longName")]
    [DataRow("nullName")]
    [DataRow("emptyFlavor")]
    [DataRow("longFlavor")]
    [DataRow("emptyDescription")]
    [DataRow("longDescription")]
    [DataRow("image")]
    [DataRow("missingPrice")]
    [DataRow("priceZero")]
    [DataRow("priceNegative")]
    [DataRow("pricePrecision")]
    [DataRow("priceOverflow")]
    [DataRow("promotionZero")]
    [DataRow("promotionEqual")]
    [DataRow("promotionGreater")]
    [DataRow("promotionPrecision")]
    [DataRow("missingStock")]
    [DataRow("negativeStock")]
    [DataRow("fractionalStock")]
    [DataRow("stockOverflow")]
    [DataRow("extraActive")]
    public async Task InvalidPayloadReturnsValidationProblemWithoutPersistingProduct(string invalid)
    {
        var payload = Payload();
        switch (invalid)
        {
            case "emptyName": payload["name"] = "  "; break;
            case "longName": payload["name"] = new string('a', 101); break;
            case "nullName": payload["name"] = null; break;
            case "emptyFlavor": payload["flavor"] = "  "; break;
            case "longFlavor": payload["flavor"] = new string('a', 61); break;
            case "emptyDescription": payload["description"] = "  "; break;
            case "longDescription": payload["description"] = new string('a', 1001); break;
            case "image": payload["imagePath"] = "/imagem-inexistente.svg"; break;
            case "missingPrice": payload.Remove("price"); break;
            case "priceZero": payload["price"] = 0; break;
            case "priceNegative": payload["price"] = -1; break;
            case "pricePrecision": payload["price"] = 1.001m; break;
            case "priceOverflow": payload["price"] = 10000000000m; break;
            case "promotionZero": payload["promotionalPrice"] = 0; break;
            case "promotionEqual": payload["promotionalPrice"] = 15.50m; break;
            case "promotionGreater": payload["promotionalPrice"] = 16; break;
            case "promotionPrecision": payload["promotionalPrice"] = 1.001m; break;
            case "missingStock": payload.Remove("stock"); break;
            case "negativeStock": payload["stock"] = -1; break;
            case "fractionalStock": payload["stock"] = 1.5m; break;
            case "stockOverflow": payload["stock"] = 2147483648L; break;
            case "extraActive": payload["isActive"] = false; break;
            default: throw new ArgumentException(nameof(invalid));
        }
        using var client = await LoginAsync(AdminEmail);
        var response = await client.PostAsJsonAsync("/api/admin/products", payload);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.IsNotNull(problem);
        Assert.AreEqual(400, problem.Status);
        Assert.IsNotEmpty(problem.Errors);
        using var scope = app.Services.CreateScope();
        Assert.AreEqual(0, await scope.ServiceProvider.GetRequiredService<CupcakesDbContext>().Products.CountAsync());
    }
}
