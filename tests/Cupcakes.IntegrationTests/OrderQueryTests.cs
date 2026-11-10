using System.Net;
using System.Net.Http.Json;
using Cupcakes.Api.Contracts;
using Cupcakes.Api.Data;
using Cupcakes.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cupcakes.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class OrderQueryTests
{
    private static TestApplication app = null!;
    private const string Password = "ConsultarPedido123";

    [ClassInitialize]
    public static async Task Initialize(TestContext _) => app = await TestApplication.CreateAsync();

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
        var email = $"order-query-{Guid.NewGuid():N}@example.invalid";
        Assert.AreEqual(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password })).StatusCode);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);
        await TokenAsync(client);
        return (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Id;
    }

    private static async Task<CheckoutConfirmationResponse> PurchaseAsync(HttpClient client, string scenario = "Aprovar")
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        var first = new Product("Chocolate historico", "Chocolate", "Produto ficticio", Product.DemoImagePath, 12.50m, null, 10);
        var second = new Product("Morango historico", "Morango", "Produto ficticio", Product.DemoImagePath, 14m, 11.90m, 10);
        db.Products.AddRange(first, second); await db.SaveChangesAsync();
        foreach (var pair in new[] { (first.Id, Quantity: 2), (second.Id, Quantity: 3) })
            Assert.AreEqual(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/cart/items", new { productId = pair.Id, quantity = pair.Quantity })).StatusCode);
        var address = new CheckoutAddressRequest { Recipient = "Pessoa Ficticia", Street = "Rua de Teste", Number = "s/n",
            Neighborhood = "Bairro", City = "Cidade", State = "SP", PostalCode = "01234567", Complement = "Casa" };
        var previewResponse = await client.PostAsJsonAsync("/api/checkout/preview", new { address });
        Assert.AreEqual(HttpStatusCode.OK, previewResponse.StatusCode);
        var cart = (await previewResponse.Content.ReadFromJsonAsync<CheckoutPreviewResponse>())!.Cart;
        var confirmation = await client.PostAsJsonAsync("/api/checkout/confirm", new CheckoutConfirmationRequest {
            ConfirmationId = Guid.NewGuid(), Address = address, PaymentMethod = "Pix", Simulation = scenario,
            ExpectedItems = cart.Items.Select(item => new CheckoutExpectedItem { ProductId = item.ProductId,
                Quantity = item.Quantity, Price = item.Price, EffectivePrice = item.EffectivePrice }).ToList()
        });
        Assert.AreEqual(scenario == "Aprovar" ? HttpStatusCode.Created : HttpStatusCode.OK, confirmation.StatusCode);
        return (await confirmation.Content.ReadFromJsonAsync<CheckoutConfirmationResponse>())!;
    }

    private static async Task<OrderDetailResponse> DetailAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"/api/orders/{id}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            (await response.Content.ReadAsStringAsync()) + "\n" + app.LoggedExceptions.LastOrDefault());
        StringAssert.Contains(response.Headers.CacheControl!.ToString(), "no-store");
        return (await response.Content.ReadFromJsonAsync<OrderDetailResponse>())!;
    }

    [TestMethod]
    public async Task AnonymousCannotListOrReadOrders()
    {
        using var client = app.CreateBrowserClient();
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/orders")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/orders/{Guid.NewGuid()}")).StatusCode);
    }

    [TestMethod]
    public async Task NewCustomerGetsEmptyListWithoutCache()
    {
        using var client = app.CreateBrowserClient(); await LoginAsync(client);
        var response = await client.GetAsync("/api/orders");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        StringAssert.Contains(response.Headers.CacheControl!.ToString(), "no-store");
        Assert.HasCount(0, (await response.Content.ReadFromJsonAsync<OrderSummaryResponse[]>())!);
    }

    [TestMethod]
    public async Task ListContainsOnlyOwnOrdersSortedByDateThenIdAndCountsQuantities()
    {
        using var owner = app.CreateBrowserClient(); using var other = app.CreateBrowserClient();
        var ownerId = await LoginAsync(owner); var otherId = await LoginAsync(other);
        var first = (await PurchaseAsync(owner)).Order!;
        var second = (await PurchaseAsync(owner)).Order!;
        var third = (await PurchaseAsync(owner)).Order!;
        var foreign = (await PurchaseAsync(other)).Order!;
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        var earlier = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var later = earlier.AddDays(1);
        // Datas controladas somente no banco temporario para testar ordenacao e desempate.
        await db.Orders.Where(x => x.Id == first.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.CreatedAt, earlier));
        await db.Orders.Where(x => x.Id == second.Id || x.Id == third.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.CreatedAt, later));
        var response = await owner.GetAsync($"/api/orders?userId={otherId}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var listed = (await response.Content.ReadFromJsonAsync<OrderSummaryResponse[]>())!;
        var expected = new[] { second.Id, third.Id }.OrderByDescending(id => id).Append(first.Id).ToArray();
        CollectionAssert.AreEqual(expected, listed.Select(x => x.Id).ToArray());
        Assert.IsFalse(listed.Any(x => x.Id == foreign.Id));
        Assert.IsTrue(listed.All(x => x.IsSimulation && x.ItemCount == 5 && x.Total == 60.70m && x.Discount == 6.30m && x.Subtotal == 67m));
        var json = await response.Content.ReadAsStringAsync();
        Assert.IsFalse(json.Contains("recipient", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains(ownerId, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task DetailPreservesReceiptAddressItemsAndTotalsAfterCatalogChanges()
    {
        using var client = app.CreateBrowserClient(); await LoginAsync(client);
        var receipt = (await PurchaseAsync(client)).Order!;
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        var ids = receipt.Items.Select(x => x.ProductId).ToArray();
        await db.Products.Where(x => ids.Contains(x.Id)).ExecuteUpdateAsync(set => set
            .SetProperty(x => x.Name, "Nome novo").SetProperty(x => x.Price, 30m)
            .SetProperty(x => x.PromotionalPrice, (decimal?)null).SetProperty(x => x.IsActive, false));
        var detail = await DetailAsync(client, receipt.Id);
        Assert.IsTrue(detail.IsSimulation); StringAssert.Contains(detail.Notice, "simulado");
        Assert.AreEqual(receipt.ConfirmationId, detail.Order.ConfirmationId);
        Assert.AreEqual("Recebido", detail.Order.Status); Assert.AreEqual("Pix", detail.Order.PaymentMethod);
        Assert.AreEqual("Aprovado", detail.Order.PaymentResult);
        Assert.AreEqual(67m, detail.Order.Subtotal); Assert.AreEqual(6.30m, detail.Order.Discount); Assert.AreEqual(60.70m, detail.Order.Total);
        Assert.AreEqual("Pessoa Ficticia", detail.Order.Address.Recipient);
        Assert.AreEqual("Casa", detail.Order.Address.Complement); Assert.AreEqual("01234567", detail.Order.Address.PostalCode);
        Assert.AreEqual(receipt.CreatedAt.UtcTicks / 10, detail.Order.CreatedAt.UtcTicks / 10);
        CollectionAssert.AreEqual(new[] { "Chocolate historico", "Morango historico" }, detail.Order.Items.Select(x => x.ProductName).ToArray());
        Assert.AreEqual(11.90m, detail.Order.Items[1].AppliedUnitPrice);
        Assert.AreEqual(14m, detail.Order.Items[1].OriginalUnitPrice);
        Assert.HasCount(1, detail.History); Assert.AreEqual("Recebido", detail.History[0].Status);
        Assert.AreEqual(detail.Order.CreatedAt, detail.History[0].ChangedAt);
        Assert.AreEqual(TimeSpan.Zero, detail.History[0].ChangedAt.Offset);
    }

    [TestMethod]
    public async Task ForeignAndUnknownOrdersReturnSameNotFoundWithoutHistoricalData()
    {
        using var owner = app.CreateBrowserClient(); using var other = app.CreateBrowserClient();
        var ownerId = await LoginAsync(owner); await LoginAsync(other);
        var receipt = (await PurchaseAsync(owner)).Order!;
        var foreign = await other.GetAsync($"/api/orders/{receipt.Id}?userId={ownerId}");
        var unknown = await other.GetAsync($"/api/orders/{Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.NotFound, foreign.StatusCode); Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);
        var foreignError = (await foreign.Content.ReadFromJsonAsync<ProblemDetails>())!;
        var unknownError = (await unknown.Content.ReadFromJsonAsync<ProblemDetails>())!;
        Assert.AreEqual(unknownError.Title, foreignError.Title);
        Assert.IsFalse((await foreign.Content.ReadAsStringAsync()).Contains("Pessoa Ficticia", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task DetailSortsExistingHistoryChronologicallyAndReadsCurrentStatus()
    {
        using var client = app.CreateBrowserClient(); await LoginAsync(client);
        var receipt = (await PurchaseAsync(client)).Order!;
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        // Dados controlados representam historico futuro; nao ha API administrativa nesta tarefa.
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"OrderStatusHistory\" (\"Id\", \"OrderId\", \"Status\", \"ChangedAt\") VALUES ({Guid.NewGuid()}, {receipt.Id}, 'SaiuParaEntrega', {receipt.CreatedAt.AddMinutes(2)})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"OrderStatusHistory\" (\"Id\", \"OrderId\", \"Status\", \"ChangedAt\") VALUES ({Guid.NewGuid()}, {receipt.Id}, 'EmPreparo', {receipt.CreatedAt.AddMinutes(1)})");
        await db.Orders.Where(x => x.Id == receipt.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, OrderStatus.SaiuParaEntrega));
        var detail = await DetailAsync(client, receipt.Id);
        Assert.AreEqual("SaiuParaEntrega", detail.Order.Status);
        CollectionAssert.AreEqual(new[] { "Recebido", "EmPreparo", "SaiuParaEntrega" }, detail.History.Select(x => x.Status).ToArray());
        var listed = (await client.GetFromJsonAsync<OrderSummaryResponse[]>("/api/orders"))!;
        Assert.AreEqual("SaiuParaEntrega", listed.Single().Status);
    }

    [TestMethod]
    public async Task DeclinedSimulationDoesNotAppearAsOrder()
    {
        using var client = app.CreateBrowserClient(); await LoginAsync(client);
        Assert.AreEqual("declined", (await PurchaseAsync(client, "Recusar")).Code);
        Assert.HasCount(0, (await client.GetFromJsonAsync<OrderSummaryResponse[]>("/api/orders"))!);
    }

    [TestMethod]
    public async Task InvalidIdentifierDoesNotServeAngularFallback()
    {
        using var client = app.CreateBrowserClient(); await LoginAsync(client);
        var response = await client.GetAsync("/api/orders/identificador-invalido");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.IsFalse((await response.Content.ReadAsStringAsync()).Contains("<app-root", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ReadOperationsNeedNoWriteTokenAndDoNotModifyStockCartOrOrder()
    {
        using var client = app.CreateBrowserClient(); var userId = await LoginAsync(client);
        var receipt = (await PurchaseAsync(client)).Order!;
        var productId = receipt.Items[0].ProductId;
        Assert.AreEqual(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/cart/items", new { productId, quantity = 1 })).StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        var stockBefore = await db.Products.Where(x => x.Id == productId).Select(x => x.Stock).SingleAsync();
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        for (var count = 0; count < 2; count++)
        {
            Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync("/api/orders")).StatusCode);
            await DetailAsync(client, receipt.Id);
        }
        Assert.AreEqual(stockBefore, await db.Products.Where(x => x.Id == productId).Select(x => x.Stock).SingleAsync());
        Assert.AreEqual(1L, (await client.GetFromJsonAsync<CartResponse>("/api/cart"))!.ItemCount);
        Assert.AreEqual(1, await db.Orders.CountAsync(x => x.UserId == userId));
        Assert.AreEqual(1, await db.OrderStatusHistory.CountAsync(x => x.OrderId == receipt.Id));
    }
}
