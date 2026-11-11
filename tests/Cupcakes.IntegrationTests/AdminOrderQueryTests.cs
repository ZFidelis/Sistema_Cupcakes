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
public sealed class AdminOrderQueryTests
{
    private static TestApplication app = null!;
    private const string Password = "ConsultaAdmin123";
    private const string Admin = "order-admin@example.invalid";
    private const string First = "order-first@example.invalid";
    private const string Second = "order-second@example.invalid";

    [ClassInitialize]
    public static async Task Initialize(TestContext _)
    {
        app = await TestApplication.CreateAsync();
        using var scope = app.Services.CreateScope();
        Assert.IsTrue((await scope.ServiceProvider.GetRequiredService<AdminProvisioningService>().ProvisionAsync(Admin, Password)).Succeeded);
        foreach (var email in new[] { First, Second })
            Assert.IsTrue((await scope.ServiceProvider.GetRequiredService<RegistrationService>()
                .RegisterAsync(new RegisterRequest { Email = email, Password = Password }, default)).Result.Succeeded);
    }

    [ClassCleanup]
    public static async Task Cleanup()
    {
        if (app is not null) await app.DisposeAsync();
    }

    [TestInitialize]
    public async Task ClearOrders()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        await db.Orders.ExecuteDeleteAsync(); await db.Carts.ExecuteDeleteAsync(); await db.Products.ExecuteDeleteAsync();
    }

    private static async Task<HttpClient> LoginAsync(string email)
    {
        var client = app.CreateBrowserClient();
        var token = await client.GetFromJsonAsync<AntiforgeryResponse>("/api/auth/antiforgery");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token!.RequestToken);
        Assert.AreEqual(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password })).StatusCode);
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        return client;
    }

    private static async Task<Order> SeedOrderAsync(string email)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        var userId = await db.Users.Where(user => user.Email == email).Select(user => user.Id).SingleAsync();
        var product = new Product("Morango historico", "Morango", "Ficticio", Product.DemoImagePath, 14m, 11.90m, 10);
        db.Products.Add(product);
        var order = new Order(userId, SimulatedPaymentMethod.Pix,
            new OrderAddress("Pessoa Ficticia", "Rua de Teste", "s/n", "Bairro", "Cidade", "SP", "01234567", "Casa"),
            [new OrderItem(product.Id, product.Name, product.Price, product.EffectivePrice, 2)]);
        db.Orders.Add(order); await db.SaveChangesAsync();
        return order;
    }

    private static async Task<HttpClient> StatusClientAsync(string email = Admin)
    {
        var client = await LoginAsync(email);
        var token = await client.GetFromJsonAsync<AntiforgeryResponse>("/api/auth/antiforgery");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token!.RequestToken);
        return client;
    }

    [TestMethod]
    public async Task StatusProgressesOneStepAndRepeatsWithoutChangingHistoryOrPurchaseData()
    {
        var order = await SeedOrderAsync(First);
        using var client = await StatusClientAsync();
        var states = new[] { "Recebido", "EmPreparo", "SaiuParaEntrega", "Entregue" };
        for (var index = 0; index < states.Length; index++)
        {
            var response = await client.PutAsJsonAsync($"/api/admin/orders/{order.Id}/status", new { status = states[index] });
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var result = (await response.Content.ReadFromJsonAsync<OrderStatusUpdateResponse>())!;
            Assert.AreEqual(order.Id, result.OrderId); Assert.AreEqual(states[index], result.Status);
            Assert.HasCount(index + 1, result.History);
            CollectionAssert.AreEqual(states.Take(index + 1).ToArray(), result.History.Select(entry => entry.Status).ToArray());
            Assert.IsTrue(result.History.All(entry => entry.ChangedAt.Offset == TimeSpan.Zero));
            var repeated = await client.PutAsJsonAsync($"/api/admin/orders/{order.Id}/status", new { status = states[index] });
            Assert.AreEqual(HttpStatusCode.OK, repeated.StatusCode);
            CollectionAssert.AreEqual(result.History, (await repeated.Content.ReadFromJsonAsync<OrderStatusUpdateResponse>())!.History);
        }
        using var customer = await LoginAsync(First);
        var detail = (await customer.GetFromJsonAsync<OrderDetailResponse>($"/api/orders/{order.Id}"))!;
        Assert.AreEqual("Entregue", detail.Order.Status); Assert.HasCount(4, detail.History);
        Assert.AreEqual(23.80m, detail.Order.Total);
        Assert.AreEqual("Morango historico", detail.Order.Items.Single().ProductName);
        Assert.AreEqual(11.90m, detail.Order.Items.Single().AppliedUnitPrice);
        Assert.AreEqual("01234567", detail.Order.Address.PostalCode);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        Assert.AreEqual(10, await db.Products.Select(value => value.Stock).SingleAsync());
        Assert.AreEqual(0, await db.CartItems.CountAsync());
    }

    [TestMethod]
    [DataRow("Recebido", "SaiuParaEntrega")]
    [DataRow("Recebido", "Entregue")]
    [DataRow("EmPreparo", "Recebido")]
    [DataRow("SaiuParaEntrega", "Recebido")]
    [DataRow("Entregue", "EmPreparo")]
    [DataRow("Entregue", "SaiuParaEntrega")]
    public async Task InvalidStatusTransitionDoesNotChangeOrderOrHistory(string from, string to)
    {
        var order = await SeedOrderAsync(First);
        using var client = await StatusClientAsync();
        var states = new[] { "Recebido", "EmPreparo", "SaiuParaEntrega", "Entregue" };
        foreach (var status in states.Take(Array.IndexOf(states, from) + 1))
            Assert.AreEqual(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/admin/orders/{order.Id}/status", new { status })).StatusCode);
        var response = await client.PutAsJsonAsync($"/api/admin/orders/{order.Id}/status", new { status = to });
        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        Assert.AreEqual(409, (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Status);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        Assert.AreEqual(from, (await db.Orders.Select(value => value.Status).SingleAsync()).ToString());
        Assert.AreEqual(Array.IndexOf(states, from) + 1, await db.OrderStatusHistory.CountAsync());
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("unknown")]
    [DataRow("numeric")]
    [DataRow("null")]
    [DataRow("extraDate")]
    [DataRow("cancelled")]
    [DataRow("lowercase")]
    public async Task InvalidStatusPayloadReturnsBadRequest(string scenario)
    {
        var order = await SeedOrderAsync(First);
        var payload = new Dictionary<string, object?> { ["status"] = "EmPreparo" };
        switch (scenario)
        {
            case "missing": payload.Clear(); break;
            case "unknown": payload["status"] = "Unknown"; break;
            case "numeric": payload["status"] = "1"; break;
            case "null": payload["status"] = null; break;
            case "extraDate": payload["changedAt"] = "2000-01-01T00:00:00Z"; break;
            case "cancelled": payload["status"] = "Cancelado"; break;
            case "lowercase": payload["status"] = "empreparo"; break;
        }
        using var client = await StatusClientAsync();
        var response = await client.PutAsJsonAsync($"/api/admin/orders/{order.Id}/status", payload);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.IsNotEmpty((await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors);
        using var scope = app.Services.CreateScope();
        Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<CupcakesDbContext>().OrderStatusHistory.CountAsync());
    }

    [TestMethod]
    [DataRow("visitor", 401)]
    [DataRow("customer", 403)]
    [DataRow("adminWithoutToken", 400)]
    public async Task UpdatingStatusRequiresAdministratorAndWriteToken(string actor, int status)
    {
        var order = await SeedOrderAsync(First);
        using var client = actor == "visitor" ? app.CreateBrowserClient() : await LoginAsync(actor == "customer" ? First : Admin);
        Assert.AreEqual((HttpStatusCode)status, (await client.PutAsJsonAsync($"/api/admin/orders/{order.Id}/status", new { status = "EmPreparo" })).StatusCode);
        using var scope = app.Services.CreateScope();
        Assert.AreEqual(OrderStatus.Recebido, await scope.ServiceProvider.GetRequiredService<CupcakesDbContext>().Orders.Select(value => value.Status).SingleAsync());
    }

    [TestMethod]
    public async Task UpdatingMissingOrderReturnsNotFound()
    {
        using var client = await StatusClientAsync();
        Assert.AreEqual(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/admin/orders/{Guid.NewGuid()}/status", new { status = "EmPreparo" })).StatusCode);
    }

    [TestMethod]
    public async Task ConcurrentSameStatusCreatesOneEvent()
    {
        var order = await SeedOrderAsync(First);
        using var first = await StatusClientAsync(); using var second = await StatusClientAsync();
        var responses = await Task.WhenAll(first.PutAsJsonAsync($"/api/admin/orders/{order.Id}/status", new { status = "EmPreparo" }),
            second.PutAsJsonAsync($"/api/admin/orders/{order.Id}/status", new { status = "EmPreparo" }));
        Assert.IsTrue(responses.All(response => response.StatusCode == HttpStatusCode.OK));
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        Assert.AreEqual(2, await db.OrderStatusHistory.CountAsync());
        Assert.AreEqual(OrderStatus.EmPreparo, await db.Orders.Select(value => value.Status).SingleAsync());
    }

    [TestMethod]
    public async Task ConcurrentDifferentStatusesNeverSkipAnEvent()
    {
        var order = await SeedOrderAsync(First);
        using var first = await StatusClientAsync(); using var second = await StatusClientAsync();
        var responses = await Task.WhenAll(first.PutAsJsonAsync($"/api/admin/orders/{order.Id}/status", new { status = "EmPreparo" }),
            second.PutAsJsonAsync($"/api/admin/orders/{order.Id}/status", new { status = "SaiuParaEntrega" }));
        Assert.AreEqual(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.IsTrue(responses[1].StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        var history = await db.OrderStatusHistory.OrderBy(value => value.ChangedAt).Select(value => value.Status).ToArrayAsync();
        var expected = responses[1].StatusCode == HttpStatusCode.OK
            ? new[] { OrderStatus.Recebido, OrderStatus.EmPreparo, OrderStatus.SaiuParaEntrega }
            : new[] { OrderStatus.Recebido, OrderStatus.EmPreparo };
        CollectionAssert.AreEqual(expected, history);
        Assert.AreEqual(expected.Last(), await db.Orders.Select(value => value.Status).SingleAsync());
    }

    [TestMethod]
    public async Task HistoryFailureRollsBackOrderStatus()
    {
        var order = await SeedOrderAsync(First);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.reject_status_event() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'forced status event failure'; END $$;
            CREATE TRIGGER reject_status_event BEFORE INSERT ON "OrderStatusHistory"
            FOR EACH ROW EXECUTE FUNCTION public.reject_status_event();
            """);
        using var client = await StatusClientAsync();
        try
        {
            Assert.AreEqual(HttpStatusCode.InternalServerError, (await client.PutAsJsonAsync($"/api/admin/orders/{order.Id}/status", new { status = "EmPreparo" })).StatusCode);
            Assert.AreEqual(OrderStatus.Recebido, await db.Orders.Select(value => value.Status).SingleAsync());
            Assert.AreEqual(1, await db.OrderStatusHistory.CountAsync());
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER reject_status_event ON "OrderStatusHistory";
                DROP FUNCTION public.reject_status_event();
                """);
        }
        Assert.AreEqual(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/admin/orders/{order.Id}/status", new { status = "EmPreparo" })).StatusCode);
    }

    [TestMethod]
    [DataRow("visitor", 401)]
    [DataRow("customer", 403)]
    public async Task OnlyAdministratorCanReadListOrDetail(string actor, int expected)
    {
        var order = await SeedOrderAsync(First);
        using var client = actor == "visitor" ? app.CreateBrowserClient() : await LoginAsync(First);
        Assert.AreEqual((HttpStatusCode)expected, (await client.GetAsync("/api/admin/orders")).StatusCode);
        Assert.AreEqual((HttpStatusCode)expected, (await client.GetAsync($"/api/admin/orders/{order.Id}")).StatusCode);
    }

    [TestMethod]
    public async Task EmptyAdministrativeListIsAnArrayAndNotCached()
    {
        using var client = await LoginAsync(Admin);
        var response = await client.GetAsync("/api/admin/orders");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(response.Headers.CacheControl!.NoStore);
        Assert.HasCount(0, (await response.Content.ReadFromJsonAsync<AdminOrderSummaryResponse[]>())!);
    }

    [TestMethod]
    public async Task ListIncludesDifferentCustomersAndSortsByDateThenId()
    {
        var first = await SeedOrderAsync(First); var second = await SeedOrderAsync(Second); var third = await SeedOrderAsync(First);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        var earlier = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        await db.Orders.ExecuteUpdateAsync(set => set.SetProperty(value => value.CreatedAt, earlier.AddDays(1)));
        await db.Orders.Where(value => value.Id == first.Id).ExecuteUpdateAsync(set => set.SetProperty(value => value.CreatedAt, earlier));
        using var client = await LoginAsync(Admin);
        var response = await client.GetAsync("/api/admin/orders");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var listed = (await response.Content.ReadFromJsonAsync<AdminOrderSummaryResponse[]>())!;
        var expected = new[] { second.Id, third.Id }.OrderByDescending(id => id).Append(first.Id).ToArray();
        CollectionAssert.AreEqual(expected, listed.Select(value => value.Order.Id).ToArray());
        Assert.AreEqual(Second, listed.Single(value => value.Order.Id == second.Id).CustomerEmail);
        Assert.AreEqual(First, listed.Single(value => value.Order.Id == first.Id).CustomerEmail);
        Assert.IsTrue(listed.All(value => value.Order.IsSimulation && value.Order.ItemCount == 2 && value.Order.Total == 23.80m
            && value.Order.Subtotal == 28m && value.Order.Discount == 4.20m && value.Order.Status == "Recebido"));
        var json = await response.Content.ReadAsStringAsync();
        Assert.IsFalse(json.Contains("password", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("postalCode", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task DetailReadsHistoricalValuesAndChronologicalHistoryForAnotherCustomer()
    {
        var order = await SeedOrderAsync(Second);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        await db.Products.ExecuteUpdateAsync(set => set.SetProperty(value => value.Name, "Produto alterado")
            .SetProperty(value => value.Price, 30m).SetProperty(value => value.IsActive, false));
        // Historico futuro preparado apenas no banco de teste; nao ha API de transicao nesta tarefa.
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"OrderStatusHistory\" (\"Id\", \"OrderId\", \"Status\", \"ChangedAt\") VALUES ({Guid.NewGuid()}, {order.Id}, 'SaiuParaEntrega', {order.CreatedAt.AddMinutes(2)})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"OrderStatusHistory\" (\"Id\", \"OrderId\", \"Status\", \"ChangedAt\") VALUES ({Guid.NewGuid()}, {order.Id}, 'EmPreparo', {order.CreatedAt.AddMinutes(1)})");
        await db.Orders.ExecuteUpdateAsync(set => set.SetProperty(value => value.Status, OrderStatus.SaiuParaEntrega));
        using var client = await LoginAsync(Admin);
        var response = await client.GetAsync($"/api/admin/orders/{order.Id}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(response.Headers.CacheControl!.NoStore);
        var result = (await response.Content.ReadFromJsonAsync<AdminOrderDetailResponse>())!;
        Assert.AreEqual(Second, result.CustomerEmail);
        Assert.IsTrue(result.Detail.IsSimulation);
        StringAssert.Contains(result.Detail.Notice, "simulado");
        var receipt = result.Detail.Order;
        Assert.AreEqual("SaiuParaEntrega", receipt.Status);
        Assert.AreEqual(order.Id, receipt.Id);
        Assert.AreEqual("Morango historico", receipt.Items.Single().ProductName);
        Assert.AreEqual(14m, receipt.Items.Single().OriginalUnitPrice);
        Assert.AreEqual(11.90m, receipt.Items.Single().AppliedUnitPrice);
        Assert.AreEqual(23.80m, receipt.Total);
        Assert.AreEqual("01234567", receipt.Address.PostalCode);
        Assert.AreEqual("Casa", receipt.Address.Complement);
        CollectionAssert.AreEqual(new[] { "Recebido", "EmPreparo", "SaiuParaEntrega" }, result.Detail.History.Select(value => value.Status).ToArray());
    }

    [TestMethod]
    public async Task MissingOrderReturnsProblemWithoutAngularFallback()
    {
        using var client = await LoginAsync(Admin);
        var response = await client.GetAsync($"/api/admin/orders/{Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.AreEqual(404, (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Status);
        var malformed = await client.GetAsync("/api/admin/orders/invalid-id");
        Assert.AreEqual(HttpStatusCode.NotFound, malformed.StatusCode);
        Assert.IsFalse((await malformed.Content.ReadAsStringAsync()).Contains("<app-root", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task CustomerRoutesContinueToIsolateOrders()
    {
        var own = await SeedOrderAsync(First); var other = await SeedOrderAsync(Second);
        using var client = await LoginAsync(First);
        var listed = (await client.GetFromJsonAsync<OrderSummaryResponse[]>("/api/orders"))!;
        Assert.HasCount(1, listed); Assert.AreEqual(own.Id, listed.Single().Id);
        Assert.AreEqual(HttpStatusCode.NotFound, (await client.GetAsync($"/api/orders/{other.Id}")).StatusCode);
    }

    [TestMethod]
    public async Task AdministrativeReadsNeedNoWriteTokenAndDoNotMutateBusinessData()
    {
        var order = await SeedOrderAsync(First);
        using var customer = await LoginAsync(First);
        var token = await customer.GetFromJsonAsync<AntiforgeryResponse>("/api/auth/antiforgery");
        customer.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token!.RequestToken);
        Assert.AreEqual(HttpStatusCode.OK, (await customer.PostAsJsonAsync("/api/cart/items", new { productId = order.Items.Single().ProductId, quantity = 1 })).StatusCode);
        using var admin = await LoginAsync(Admin);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            Assert.AreEqual(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/orders")).StatusCode);
            Assert.AreEqual(HttpStatusCode.OK, (await admin.GetAsync($"/api/admin/orders/{order.Id}")).StatusCode);
        }
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        Assert.AreEqual(10, await db.Products.Select(value => value.Stock).SingleAsync());
        Assert.AreEqual(1, await db.Orders.CountAsync());
        Assert.AreEqual(1, await db.CartItems.CountAsync());
        Assert.AreEqual(1, await db.OrderStatusHistory.CountAsync());
        Assert.AreEqual(OrderStatus.Recebido, await db.Orders.Select(value => value.Status).SingleAsync());
    }
}
