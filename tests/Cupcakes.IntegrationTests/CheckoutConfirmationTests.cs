using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cupcakes.Api.Contracts;
using Cupcakes.Api.Data;
using Cupcakes.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Cupcakes.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class CheckoutConfirmationTests
{
    private static TestApplication app = null!;
    private const string Path = "/api/checkout/confirm";
    private const string Password = "CompraSimulada123";

    [ClassInitialize]
    public static async Task Initialize(TestContext _) => app = await TestApplication.CreateAsync();

    [ClassCleanup]
    public static async Task Cleanup()
    {
        if (app is not null) await app.DisposeAsync();
    }

    private static CheckoutAddressRequest Address() => new() {
        Recipient = " Pessoa Ficticia ", Street = "Rua de Teste", Number = "s/n", Neighborhood = "Bairro",
        City = "Cidade", State = "sp", PostalCode = "01234567"
    };

    private static async Task TokenAsync(HttpClient client)
    {
        var token = await client.GetFromJsonAsync<AntiforgeryResponse>("/api/auth/antiforgery");
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token!.RequestToken);
    }

    private static async Task<string> LoginAsync(HttpClient client)
    {
        await TokenAsync(client);
        var email = $"confirm-{Guid.NewGuid():N}@example.invalid";
        Assert.AreEqual(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password })).StatusCode);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);
        await TokenAsync(client);
        return (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Id;
    }

    private static async Task<Product> ProductAsync(int stock = 10, decimal price = 12.50m, decimal? promotion = null)
    {
        var product = new Product("Teste " + Guid.NewGuid().ToString("N"), "Chocolate", "Produto ficticio",
            Product.DemoImagePath, price, promotion, stock);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product;
    }

    private static async Task AddAsync(HttpClient client, Product product, int quantity = 2) =>
        Assert.AreEqual(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/cart/items", new { productId = product.Id, quantity })).StatusCode);

    private static async Task<CheckoutConfirmationRequest> RequestAsync(HttpClient client, string method = "Pix", string scenario = "Aprovar", Guid? key = null)
    {
        var preview = await client.PostAsJsonAsync("/api/checkout/preview", new { address = Address() });
        Assert.AreEqual(HttpStatusCode.OK, preview.StatusCode);
        var cart = (await preview.Content.ReadFromJsonAsync<CheckoutPreviewResponse>())!.Cart;
        return new() { Address = Address(), ConfirmationId = key ?? Guid.NewGuid(), PaymentMethod = method, Simulation = scenario,
            ExpectedItems = cart.Items.Select(item => new CheckoutExpectedItem {
                ProductId = item.ProductId, Quantity = item.Quantity, Price = item.Price, EffectivePrice = item.EffectivePrice
            }).ToList() };
    }

    private static async Task<CheckoutConfirmationResponse> ConfirmAsync(HttpClient client, CheckoutConfirmationRequest request, HttpStatusCode expected)
    {
        var response = await client.PostAsJsonAsync(Path, request);
        Assert.AreEqual(expected, response.StatusCode,
            (await response.Content.ReadAsStringAsync()) + "\n" + app.LoggedExceptions.LastOrDefault());
        StringAssert.Contains(response.Headers.CacheControl!.ToString(), "no-store");
        return (await response.Content.ReadFromJsonAsync<CheckoutConfirmationResponse>())!;
    }

    private static async Task<int> StockAsync(Guid productId)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CupcakesDbContext>().Products.Where(x => x.Id == productId).Select(x => x.Stock).SingleAsync();
    }

    private static async Task AssertNoOrderAsync(string userId)
    {
        using var scope = app.Services.CreateScope();
        Assert.AreEqual(0, await scope.ServiceProvider.GetRequiredService<CupcakesDbContext>().Orders.CountAsync(x => x.UserId == userId));
    }

    [TestMethod]
    public async Task AnonymousCannotConfirm()
    {
        using var client = app.CreateBrowserClient();
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Path, new { })).StatusCode);
    }

    [TestMethod]
    public async Task ExistingWriteTokenIsRequired()
    {
        using var client = app.CreateBrowserClient();
        var userId = await LoginAsync(client);
        var product = await ProductAsync(); await AddAsync(client, product);
        var request = await RequestAsync(client);
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Path, request)).StatusCode);
        await AssertNoOrderAsync(userId);
        Assert.AreEqual(10, await StockAsync(product.Id));
    }

    [TestMethod]
    [DataRow("Pix")]
    [DataRow("Cartao")]
    public async Task ApprovalCreatesCompleteHistoricalOrderReducesStockAndClearsOnlyOwnCart(string method)
    {
        using var client = app.CreateBrowserClient();
        var userId = await LoginAsync(client);
        var regular = await ProductAsync(); var promotion = await ProductAsync(price: 14m, promotion: 11.90m);
        await AddAsync(client, regular); await AddAsync(client, promotion, 3);
        var result = await ConfirmAsync(client, await RequestAsync(client, method), HttpStatusCode.Created);
        Assert.IsTrue(result.IsSimulation); Assert.AreEqual("approved", result.Code);
        var receipt = result.Order!;
        Assert.AreEqual(67m, receipt.Subtotal); Assert.AreEqual(6.30m, receipt.Discount); Assert.AreEqual(60.70m, receipt.Total);
        Assert.AreEqual(method, receipt.PaymentMethod); Assert.AreEqual("Aprovado", receipt.PaymentResult);
        Assert.AreEqual("Recebido", receipt.Status); Assert.AreEqual("Pessoa Ficticia", receipt.Address.Recipient);
        Assert.AreEqual("01234567", receipt.Address.PostalCode);
        Assert.AreEqual(8, await StockAsync(regular.Id)); Assert.AreEqual(7, await StockAsync(promotion.Id));
        Assert.HasCount(0, (await client.GetFromJsonAsync<CartResponse>("/api/cart"))!.Items);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        var order = await db.Orders.Include(x => x.Items).Include(x => x.Address).Include(x => x.StatusHistory).SingleAsync(x => x.Id == receipt.Id);
        Assert.AreEqual(userId, order.UserId); Assert.HasCount(2, order.Items); Assert.HasCount(1, order.StatusHistory);
        Assert.AreEqual(OrderStatus.Recebido, order.StatusHistory.Single().Status);
        Assert.AreEqual(order.CreatedAt, order.StatusHistory.Single().ChangedAt);
        Assert.AreEqual(11.90m, order.Items.Single(x => x.ProductId == promotion.Id).AppliedUnitPrice);
    }

    [TestMethod]
    [DataRow("Pix")]
    [DataRow("Cartao")]
    public async Task DeclinePreservesStockAndCartWithoutCreatingOrder(string method)
    {
        using var client = app.CreateBrowserClient(); var userId = await LoginAsync(client);
        var product = await ProductAsync(); await AddAsync(client, product);
        var response = await ConfirmAsync(client, await RequestAsync(client, method, "Recusar"), HttpStatusCode.OK);
        Assert.AreEqual("declined", response.Code); Assert.IsTrue(response.IsSimulation); Assert.IsNull(response.Order);
        Assert.AreEqual(10, await StockAsync(product.Id));
        Assert.AreEqual(2, (await client.GetFromJsonAsync<CartResponse>("/api/cart"))!.Items.Single().Quantity);
        await AssertNoOrderAsync(userId);
    }

    [TestMethod]
    public async Task ReplayReturnsOriginalOrderEvenAfterRefillingCartWithoutSecondStockReduction()
    {
        using var client = app.CreateBrowserClient(); await LoginAsync(client);
        var product = await ProductAsync(); await AddAsync(client, product);
        var request = await RequestAsync(client);
        var first = await ConfirmAsync(client, request, HttpStatusCode.Created);
        var repeated = await ConfirmAsync(client, request, HttpStatusCode.OK);
        Assert.AreEqual(first.Order!.Id, repeated.Order!.Id);
        await AddAsync(client, product, 1);
        repeated = await ConfirmAsync(client, request, HttpStatusCode.OK);
        Assert.AreEqual(first.Order.Id, repeated.Order!.Id);
        Assert.AreEqual(8, await StockAsync(product.Id));
        Assert.AreEqual(1, (await client.GetFromJsonAsync<CartResponse>("/api/cart"))!.Items.Single().Quantity);
    }

    [TestMethod]
    [DataRow("price")]
    [DataRow("promotion")]
    [DataRow("quantity")]
    [DataRow("add")]
    [DataRow("remove")]
    [DataRow("replace")]
    [DataRow("normal-price-same-total")]
    public async Task ChangedSnapshotRequiresReviewAndDoesNotCreateOrder(string change)
    {
        using var client = app.CreateBrowserClient(); var userId = await LoginAsync(client);
        var product = await ProductAsync(); await AddAsync(client, product);
        var request = await RequestAsync(client);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        if (change == "price") await db.Products.Where(x => x.Id == product.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.Price, 15m));
        if (change == "promotion") await db.Products.Where(x => x.Id == product.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.PromotionalPrice, (decimal?)10m));
        if (change == "normal-price-same-total") await db.Products.Where(x => x.Id == product.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.Price, 20m).SetProperty(x => x.PromotionalPrice, (decimal?)12.50m));
        if (change == "quantity") Assert.AreEqual(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/cart/items/{product.Id}", new { quantity = 1 })).StatusCode);
        if (change is "remove" or "replace") Assert.AreEqual(HttpStatusCode.OK, (await client.DeleteAsync($"/api/cart/items/{product.Id}")).StatusCode);
        if (change is "add" or "replace") await AddAsync(client, await ProductAsync());
        var result = await ConfirmAsync(client, request, HttpStatusCode.Conflict);
        Assert.IsNull(result.Order); Assert.IsNotNull(result.Preview);
        Assert.AreEqual(10, await StockAsync(product.Id)); await AssertNoOrderAsync(userId);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task InactiveOrInsufficientProductRejectsConfirmation(bool inactive)
    {
        using var client = app.CreateBrowserClient(); var userId = await LoginAsync(client);
        var product = await ProductAsync(); await AddAsync(client, product);
        var request = await RequestAsync(client);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        if (inactive) await db.Products.Where(x => x.Id == product.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.IsActive, false));
        else await db.Products.Where(x => x.Id == product.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.Stock, 1));
        var result = await ConfirmAsync(client, request, HttpStatusCode.Conflict);
        Assert.AreEqual("checkout_unavailable", result.Code); Assert.IsTrue(result.Preview!.Cart.HasUnavailableItems);
        await AssertNoOrderAsync(userId); Assert.AreEqual(inactive ? 10 : 1, await StockAsync(product.Id));
    }

    [TestMethod]
    public async Task SameIdentifierWithDifferentContentIsRejected()
    {
        using var client = app.CreateBrowserClient(); await LoginAsync(client);
        var product = await ProductAsync(); await AddAsync(client, product);
        var request = await RequestAsync(client);
        var original = await ConfirmAsync(client, request, HttpStatusCode.Created);
        var altered = JsonSerializer.SerializeToNode(request, JsonSerializerOptions.Web)!;
        altered["address"]!["number"] = "999";
        var response = await client.PostAsJsonAsync(Path, altered);
        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        Assert.AreEqual("confirmation_reused", (await response.Content.ReadFromJsonAsync<CheckoutConfirmationResponse>())!.Code);
        Assert.AreEqual(8, await StockAsync(product.Id));
        using var scope = app.Services.CreateScope();
        Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<CupcakesDbContext>().Orders.CountAsync(x => x.Id == original.Order!.Id));
    }

    [TestMethod]
    public async Task AnotherUserCannotRetrieveOwnersOrderUsingItsConfirmationId()
    {
        using var owner = app.CreateBrowserClient(); using var other = app.CreateBrowserClient();
        await LoginAsync(owner); var otherId = await LoginAsync(other);
        var product = await ProductAsync(); await AddAsync(owner, product);
        var request = await RequestAsync(owner);
        await ConfirmAsync(owner, request, HttpStatusCode.Created);
        var result = await ConfirmAsync(other, request, HttpStatusCode.Conflict);
        Assert.IsNull(result.Order); Assert.AreEqual("checkout_unavailable", result.Code);
        await AssertNoOrderAsync(otherId); Assert.AreEqual(8, await StockAsync(product.Id));
    }

    [TestMethod]
    public async Task TwoUsersMayUseSameIdentifierForTheirOwnPurchases()
    {
        using var first = app.CreateBrowserClient(); using var second = app.CreateBrowserClient();
        await LoginAsync(first); await LoginAsync(second);
        var product = await ProductAsync(); await AddAsync(first, product); await AddAsync(second, product);
        var key = Guid.NewGuid();
        var one = await ConfirmAsync(first, await RequestAsync(first, key: key), HttpStatusCode.Created);
        var two = await ConfirmAsync(second, await RequestAsync(second, key: key), HttpStatusCode.Created);
        Assert.AreNotEqual(one.Order!.Id, two.Order!.Id); Assert.AreEqual(6, await StockAsync(product.Id));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ConcurrentSameUserConfirmationsCreateExactlyOneOrder(bool sameIdentifier)
    {
        using var client = app.CreateBrowserClient(); var userId = await LoginAsync(client);
        var product = await ProductAsync(); await AddAsync(client, product);
        var first = await RequestAsync(client);
        var second = await RequestAsync(client, key: sameIdentifier ? first.ConfirmationId : null);
        var responses = await Task.WhenAll(client.PostAsJsonAsync(Path, first), client.PostAsJsonAsync(Path, second));
        Assert.AreEqual(1, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
        Assert.AreEqual(1, responses.Count(response => response.StatusCode == (sameIdentifier ? HttpStatusCode.OK : HttpStatusCode.Conflict)));
        Assert.AreEqual(8, await StockAsync(product.Id));
        using var scope = app.Services.CreateScope();
        Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<CupcakesDbContext>().Orders.CountAsync(x => x.UserId == userId));
    }

    [TestMethod]
    public async Task ConcurrentBuyersCannotOversellLastUnits()
    {
        using var first = app.CreateBrowserClient(); using var second = app.CreateBrowserClient();
        var firstId = await LoginAsync(first); var secondId = await LoginAsync(second);
        var product = await ProductAsync(stock: 2); await AddAsync(first, product); await AddAsync(second, product);
        var one = await RequestAsync(first); var two = await RequestAsync(second);
        var responses = await Task.WhenAll(first.PostAsJsonAsync(Path, one), second.PostAsJsonAsync(Path, two));
        Assert.AreEqual(1, responses.Count(x => x.StatusCode == HttpStatusCode.Created));
        Assert.AreEqual(1, responses.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        Assert.AreEqual(0, await StockAsync(product.Id));
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        Assert.AreEqual(1, await db.Orders.CountAsync(x => x.UserId == firstId || x.UserId == secondId));
        Assert.AreEqual(2, await db.CartItems.Where(item => db.Carts.Any(cart => cart.Id == item.CartId && (cart.UserId == firstId || cart.UserId == secondId))).SumAsync(x => x.Quantity));
    }

    [TestMethod]
    public async Task SaveFailureRollsBackEveryStockChangeCartRemovalAndOrderThenAllowsRetry()
    {
        using var client = app.CreateBrowserClient(); var userId = await LoginAsync(client);
        var first = await ProductAsync(); var second = await ProductAsync();
        await AddAsync(client, first); await AddAsync(client, second);
        var request = await RequestAsync(client);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION fail_checkout_order() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Falha controlada no banco temporario de teste'; END; $$;
            CREATE TRIGGER fail_checkout_order BEFORE INSERT ON "Orders" FOR EACH ROW EXECUTE FUNCTION fail_checkout_order();
            """);
        try
        {
            Assert.AreEqual(HttpStatusCode.InternalServerError, (await client.PostAsJsonAsync(Path, request)).StatusCode);
            await AssertNoOrderAsync(userId);
            Assert.AreEqual(10, await StockAsync(first.Id)); Assert.AreEqual(10, await StockAsync(second.Id));
            Assert.HasCount(2, (await client.GetFromJsonAsync<CartResponse>("/api/cart"))!.Items);
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_checkout_order ON \"Orders\"; DROP FUNCTION fail_checkout_order();");
        }
        await ConfirmAsync(client, request, HttpStatusCode.Created);
        Assert.AreEqual(8, await StockAsync(first.Id)); Assert.AreEqual(8, await StockAsync(second.Id));
    }

    [TestMethod]
    [DataRow("method")]
    [DataRow("scenario")]
    [DataRow("address")]
    [DataRow("identifier")]
    [DataRow("items")]
    [DataRow("duplicate")]
    [DataRow("null-item")]
    [DataRow("quantity")]
    [DataRow("price")]
    public async Task InvalidConfirmationIsRejectedBeforeAnyMutation(string invalid)
    {
        using var client = app.CreateBrowserClient(); var userId = await LoginAsync(client);
        var product = await ProductAsync(); await AddAsync(client, product);
        var request = JsonSerializer.SerializeToNode(await RequestAsync(client), JsonSerializerOptions.Web)!;
        switch (invalid)
        {
            case "method": request["paymentMethod"] = "Real"; break;
            case "scenario": request["simulation"] = "Cobrar"; break;
            case "address": request["address"]!["postalCode"] = "123"; break;
            case "identifier": request["confirmationId"] = Guid.Empty.ToString(); break;
            case "items": request["expectedItems"] = new JsonArray(); break;
            case "duplicate": request["expectedItems"]!.AsArray().Add(request["expectedItems"]![0]!.DeepClone()); break;
            case "null-item": request["expectedItems"]!.AsArray().Add((JsonNode?)null); break;
            case "quantity": request["expectedItems"]![0]!["quantity"] = 0; break;
            case "price": request["expectedItems"]![0]!["effectivePrice"] = 1.001m; break;
        }
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Path, request)).StatusCode);
        await AssertNoOrderAsync(userId); Assert.AreEqual(10, await StockAsync(product.Id));
    }

    [TestMethod]
    public async Task DatabaseRejectsDuplicateConfirmationForSameOwner()
    {
        using var client = app.CreateBrowserClient(); await LoginAsync(client);
        var product = await ProductAsync(); await AddAsync(client, product);
        var approved = (await ConfirmAsync(client, await RequestAsync(client), HttpStatusCode.Created)).Order!;
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Orders" ("Id","UserId","ConfirmationId","CreatedAt","Status","PaymentMethod","PaymentResult","Subtotal","Discount","Total")
            SELECT {Guid.NewGuid()},"UserId","ConfirmationId","CreatedAt","Status","PaymentMethod","PaymentResult","Subtotal","Discount","Total"
            FROM "Orders" WHERE "Id"={approved.Id}
            """));
        Assert.AreEqual(PostgresErrorCodes.UniqueViolation, error.SqlState);
    }
}
