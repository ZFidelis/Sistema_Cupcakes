using System.Net;
using System.Net.Http.Json;
using System.Text;
using Cupcakes.Api.Contracts;
using Cupcakes.Api.Data;
using Cupcakes.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Cupcakes.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class CartTests
{
    private static TestApplication app = null!;
    private const string Password = "CarrinhoTeste123";

    [ClassInitialize]
    public static async Task Initialize(TestContext _)
    {
        app = await TestApplication.CreateAsync();
        using var scope = app.Services.CreateScope();
        Assert.IsFalse(scope.ServiceProvider.GetRequiredService<CupcakesDbContext>().Database.HasPendingModelChanges());
    }

    [ClassCleanup]
    public static async Task Cleanup()
    {
        if (app is not null) await app.DisposeAsync();
    }

    private static async Task TokenAsync(HttpClient client)
    {
        var token = await client.GetFromJsonAsync<AntiforgeryResponse>("/api/auth/antiforgery");
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token!.RequestToken);
    }

    private static async Task<string> LoginAsync(HttpClient client)
    {
        await TokenAsync(client);
        var email = $"cart-{Guid.NewGuid():N}@example.invalid";
        Assert.AreEqual(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password })).StatusCode);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);
        await TokenAsync(client);
        return (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Id;
    }

    private static async Task<Product> ProductAsync(int stock = 10, bool active = true, decimal price = 12.50m, decimal? promotion = null)
    {
        var product = new Product("Teste " + Guid.NewGuid().ToString("N"), "Chocolate", "Produto de teste",
            Product.DemoImagePath, price, promotion, stock, active);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product;
    }

    private static async Task<CartResponse> ReadAsync(HttpClient client) => (await client.GetFromJsonAsync<CartResponse>("/api/cart"))!;

    private static async Task<CartResponse> AddAsync(HttpClient client, Guid productId, int quantity)
    {
        var response = await client.PostAsJsonAsync("/api/cart/items", new { productId, quantity });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            (await response.Content.ReadAsStringAsync()) + "\n" + app.LoggedExceptions.LastOrDefault());
        return (await response.Content.ReadFromJsonAsync<CartResponse>())!;
    }

    [TestMethod]
    public async Task AnonymousCannotReadOrChangeCart()
    {
        using var client = app.CreateBrowserClient();
        var id = Guid.NewGuid();
        var responses = new[] {
            await client.GetAsync("/api/cart"),
            await client.PostAsJsonAsync("/api/cart/items", new { productId = id, quantity = 1 }),
            await client.PutAsJsonAsync($"/api/cart/items/{id}", new { quantity = 1 }),
            await client.DeleteAsync($"/api/cart/items/{id}")
        };
        foreach (var response in responses) Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task NewAccountGetsEmptyCartWithoutCreatingDatabaseRows()
    {
        using var client = app.CreateBrowserClient();
        var userId = await LoginAsync(client);
        var cart = await ReadAsync(client);
        Assert.HasCount(0, cart.Items);
        Assert.AreEqual(0L, cart.ItemCount);
        Assert.AreEqual(0m, cart.Total);
        Assert.AreEqual(0m, cart.Discount);
        Assert.AreEqual(0m, cart.Subtotal);
        Assert.IsFalse(cart.HasUnavailableItems);
        StringAssert.Contains(cart.PricingNotice, "nao sao calculados");
        using var scope = app.Services.CreateScope();
        Assert.IsFalse(await scope.ServiceProvider.GetRequiredService<CupcakesDbContext>().Carts.AnyAsync(x => x.UserId == userId));
    }

    [TestMethod]
    public async Task AddSumsQuantityUpdateReplacesItAndRemoveClearsItemWithoutChangingStock()
    {
        using var client = app.CreateBrowserClient();
        await LoginAsync(client);
        var product = await ProductAsync();
        await AddAsync(client, product.Id, 2);
        var added = await AddAsync(client, product.Id, 3);
        Assert.HasCount(1, added.Items);
        Assert.AreEqual(5, added.Items[0].Quantity);
        Assert.AreEqual(62.50m, added.Total);
        var updated = await client.PutAsJsonAsync($"/api/cart/items/{product.Id}", new { quantity = 1 });
        Assert.AreEqual(HttpStatusCode.OK, updated.StatusCode);
        var cart = (await updated.Content.ReadFromJsonAsync<CartResponse>())!;
        Assert.AreEqual(1, cart.Items.Single().Quantity);
        Assert.AreEqual(12.50m, cart.Total);
        Assert.AreEqual(HttpStatusCode.OK, (await client.DeleteAsync($"/api/cart/items/{product.Id}")).StatusCode);
        Assert.HasCount(0, (await ReadAsync(client)).Items);
        using var scope = app.Services.CreateScope();
        Assert.AreEqual(10, await scope.ServiceProvider.GetRequiredService<CupcakesDbContext>().Products.Where(x => x.Id == product.Id).Select(x => x.Stock).SingleAsync());
    }

    [TestMethod]
    public async Task TotalsUseCurrentDatabasePricesAndIgnorePricesOrUserIdsSentByClient()
    {
        using var client = app.CreateBrowserClient();
        await LoginAsync(client);
        var regular = await ProductAsync();
        var promotion = await ProductAsync(price: 14m, promotion: 11.90m);
        await AddAsync(client, regular.Id, 2);
        var response = await client.PostAsJsonAsync("/api/cart/items", new {
            productId = promotion.Id, quantity = 3, price = 0.01m, effectivePrice = 0.01m, userId = "outro-usuario"
        });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var cart = (await response.Content.ReadFromJsonAsync<CartResponse>())!;
        Assert.AreEqual(5L, cart.ItemCount);
        Assert.AreEqual(67m, cart.Subtotal);
        Assert.AreEqual(6.30m, cart.Discount);
        Assert.AreEqual(60.70m, cart.Total);
        Assert.AreEqual(35.70m, cart.Items.Single(x => x.ProductId == promotion.Id).Total);
        Assert.IsFalse(cart.HasUnavailableItems);
    }

    [TestMethod]
    public async Task ItemsPersistAfterLogoutAndLoginAgain()
    {
        using var client = app.CreateBrowserClient();
        await LoginAsync(client);
        var me = (await client.GetFromJsonAsync<SessionResponse>("/api/auth/me"))!;
        var product = await ProductAsync();
        await AddAsync(client, product.Id, 2);
        Assert.AreEqual(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        await TokenAsync(client);
        Assert.AreEqual(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email = me.Email, password = Password })).StatusCode);
        Assert.AreEqual(2, (await ReadAsync(client)).Items.Single().Quantity);
    }

    [TestMethod]
    public async Task AnotherUserCannotReadUpdateOrRemoveOwnersItems()
    {
        using var owner = app.CreateBrowserClient();
        using var other = app.CreateBrowserClient();
        var ownerId = await LoginAsync(owner);
        await LoginAsync(other);
        var product = await ProductAsync();
        await AddAsync(owner, product.Id, 2);
        var response = await other.GetFromJsonAsync<CartResponse>($"/api/cart?userId={ownerId}");
        Assert.HasCount(0, response!.Items);
        Assert.AreEqual(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/cart/items/{product.Id}", new { quantity = 1 })).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/cart/items/{product.Id}")).StatusCode);
        await AddAsync(other, product.Id, 1);
        Assert.AreEqual(2, (await ReadAsync(owner)).Items.Single().Quantity);
        Assert.AreEqual(1, (await ReadAsync(other)).Items.Single().Quantity);
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("-1")]
    [DataRow("1.5")]
    [DataRow("null")]
    public async Task InvalidQuantitiesAreRejectedForAddAndUpdate(string quantity)
    {
        using var client = app.CreateBrowserClient();
        await LoginAsync(client);
        var product = await ProductAsync();
        await AddAsync(client, product.Id, 1);
        var json = "{\"productId\":\"" + product.Id + "\",\"quantity\":" + quantity + "}";
        var add = await client.PostAsync("/api/cart/items", new StringContent(json, Encoding.UTF8, "application/json"));
        var update = await client.PutAsync($"/api/cart/items/{product.Id}", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.BadRequest, add.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, update.StatusCode);
        Assert.AreEqual(1, (await ReadAsync(client)).Items.Single().Quantity);
    }

    [TestMethod]
    public async Task ExcessQuantityAndIntegerOverflowDoNotChangeExistingItem()
    {
        using var client = app.CreateBrowserClient();
        await LoginAsync(client);
        var product = await ProductAsync(stock: int.MaxValue);
        await AddAsync(client, product.Id, int.MaxValue);
        Assert.AreEqual(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/cart/items", new { productId = product.Id, quantity = 1 })).StatusCode);
        Assert.AreEqual(int.MaxValue, (await ReadAsync(client)).Items.Single().Quantity);
        var smallStock = await ProductAsync(stock: 2);
        await AddAsync(client, smallStock.Id, 1);
        Assert.AreEqual(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/cart/items/{smallStock.Id}", new { quantity = 3 })).StatusCode);
        Assert.AreEqual(1, (await ReadAsync(client)).Items.Single(x => x.ProductId == smallStock.Id).Quantity);
    }

    [TestMethod]
    [DataRow(0, true)]
    [DataRow(5, false)]
    public async Task UnavailableProductsCannotBeAdded(int stock, bool active)
    {
        using var client = app.CreateBrowserClient();
        await LoginAsync(client);
        var product = await ProductAsync(stock, active);
        Assert.AreEqual(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/cart/items", new { productId = product.Id, quantity = 1 })).StatusCode);
        Assert.HasCount(0, (await ReadAsync(client)).Items);
    }

    [TestMethod]
    public async Task UnknownProductOrMissingCartItemReturnsNotFound()
    {
        using var client = app.CreateBrowserClient();
        await LoginAsync(client);
        var id = Guid.NewGuid();
        Assert.AreEqual(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/cart/items", new { productId = id, quantity = 1 })).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/cart/items/{id}", new { quantity = 1 })).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/cart/items/{id}")).StatusCode);
    }

    [TestMethod]
    public async Task PriceAndStockChangesAreReflectedWithoutDeletingCartItems()
    {
        using var client = app.CreateBrowserClient();
        await LoginAsync(client);
        var product = await ProductAsync();
        await AddAsync(client, product.Id, 3);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        await db.Products.Where(x => x.Id == product.Id).ExecuteUpdateAsync(set => set
            .SetProperty(x => x.Price, 15m).SetProperty(x => x.PromotionalPrice, (decimal?)10.01m).SetProperty(x => x.Stock, 1));
        var cart = await ReadAsync(client);
        Assert.AreEqual(45m, cart.Subtotal);
        Assert.AreEqual(14.97m, cart.Discount);
        Assert.AreEqual(30.03m, cart.Total);
        Assert.IsTrue(cart.HasUnavailableItems);
        Assert.IsNotNull(cart.Items.Single().AvailabilityMessage);
        Assert.AreEqual(3, cart.Items.Single().Quantity);
        Assert.AreEqual(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/cart/items/{product.Id}", new { quantity = 1 })).StatusCode);
        await db.Products.Where(x => x.Id == product.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.IsActive, false));
        Assert.IsTrue((await ReadAsync(client)).HasUnavailableItems);
        Assert.AreEqual(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/cart/items/{product.Id}", new { quantity = 1 })).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await client.DeleteAsync($"/api/cart/items/{product.Id}")).StatusCode);
    }

    [TestMethod]
    [DataRow(10, 2, 2)]
    [DataRow(3, 1, 1)]
    public async Task ConcurrentAddsAreSerializedIncludingFirstCartCreation(int stock, int successful, int expectedItems)
    {
        using var client = app.CreateBrowserClient();
        var userId = await LoginAsync(client);
        var product = await ProductAsync(stock);
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/api/cart/items", new { productId = product.Id, quantity = 2 }),
            client.PostAsJsonAsync("/api/cart/items", new { productId = product.Id, quantity = 2 }));
        Assert.AreEqual(successful, responses.Count(x => x.StatusCode == HttpStatusCode.OK));
        Assert.IsTrue(responses.All(x => x.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict));
        Assert.AreEqual(expectedItems * 2, (await ReadAsync(client)).Items.Single().Quantity);
        using var scope = app.Services.CreateScope();
        Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<CupcakesDbContext>().Carts.CountAsync(x => x.UserId == userId));
    }

    [TestMethod]
    [DataRow("quantity")]
    [DataRow("duplicate-item")]
    [DataRow("duplicate-cart")]
    public async Task DatabaseProtectsQuantityAndUniqueness(string violation)
    {
        using var client = app.CreateBrowserClient();
        var userId = await LoginAsync(client);
        var product = await ProductAsync();
        await AddAsync(client, product.Id, 1);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        var cart = await db.Carts.SingleAsync(x => x.UserId == userId);
        Func<Task> action = violation switch
        {
            "quantity" => async () => { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"CartItems\" SET \"Quantity\" = 0 WHERE \"CartId\" = {cart.Id}"); },
            "duplicate-item" => async () => { await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"CartItems\" (\"CartId\",\"ProductId\",\"Quantity\") VALUES ({cart.Id},{product.Id},1)"); },
            _ => async () => { await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Carts\" (\"Id\",\"UserId\") VALUES ({Guid.NewGuid()},{userId})"); }
        };
        var error = await Assert.ThrowsAsync<PostgresException>(action);
        Assert.AreEqual(violation == "quantity" ? PostgresErrorCodes.CheckViolation : PostgresErrorCodes.UniqueViolation, error.SqlState);
    }

    [TestMethod]
    public async Task MissingWriteTokenDoesNotChangeCart()
    {
        using var client = app.CreateBrowserClient();
        await LoginAsync(client);
        var product = await ProductAsync();
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/cart/items", new { productId = product.Id, quantity = 1 })).StatusCode);
        Assert.HasCount(0, (await ReadAsync(client)).Items);
    }
}
