using System.Net;
using System.Net.Http.Json;
using Cupcakes.Api.Contracts;
using Cupcakes.Api.Data;
using Cupcakes.Api.Domain;
using Cupcakes.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cupcakes.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class OrderFilterTests
{
    private static TestApplication app = null!;
    private const string Password = "FilterOrders123";
    private const string Admin = "filter-admin@example.invalid";
    private const string Owner = "filter-owner@example.invalid";
    private const string Other = "filter-other@example.invalid";
    private Guid before, first, last, after, foreign;

    [ClassInitialize]
    public static async Task Initialize(TestContext _)
    {
        app = await TestApplication.CreateAsync();
        using var scope = app.Services.CreateScope();
        Assert.IsTrue((await scope.ServiceProvider.GetRequiredService<AdminProvisioningService>().ProvisionAsync(Admin, Password)).Succeeded);
        foreach (var email in new[] { Owner, Other })
            Assert.IsTrue((await scope.ServiceProvider.GetRequiredService<RegistrationService>().RegisterAsync(new RegisterRequest { Email = email, Password = Password }, default)).Result.Succeeded);
    }
    [ClassCleanup]
    public static async Task Cleanup() { if (app is not null) await app.DisposeAsync(); }

    [TestInitialize]
    public async Task Seed()
    {
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        await db.Orders.ExecuteDeleteAsync();
        var product = await db.Products.FirstAsync();
        var owner = await db.Users.Where(u => u.Email == Owner).Select(u => u.Id).SingleAsync();
        var other = await db.Users.Where(u => u.Email == Other).Select(u => u.Id).SingleAsync();
        Order Add(string userId, string utc, OrderStatus status)
        {
            var order = new Order(userId, SimulatedPaymentMethod.Pix,
                new OrderAddress("Teste", "Rua", "1", "Centro", "Cidade", "SP", "01000000", null),
                [new OrderItem(product.Id, product.Name, product.Price, product.EffectivePrice, 1)]);
            for (var step = 1; step <= (int)status; step++) Assert.IsTrue(order.TryAdvanceStatus((OrderStatus)step));
            db.Orders.Add(order);
            db.Entry(order).Property(o => o.CreatedAt).CurrentValue = DateTimeOffset.Parse(utc).ToUniversalTime();
            return order;
        }
        before = Add(owner, "2026-11-14T02:59:59Z", OrderStatus.Recebido).Id;
        first = Add(owner, "2026-11-14T03:00:00Z", OrderStatus.Entregue).Id;
        last = Add(owner, "2026-11-15T02:59:59Z", OrderStatus.Recebido).Id;
        after = Add(owner, "2026-11-15T03:00:00Z", OrderStatus.Recebido).Id;
        foreign = Add(other, "2026-11-14T18:00:00Z", OrderStatus.Entregue).Id;
        await db.SaveChangesAsync();
    }

    private static async Task<HttpClient> LoginAsync(string email)
    {
        var client = app.CreateBrowserClient();
        var token = await client.GetFromJsonAsync<AntiforgeryResponse>("/api/auth/antiforgery");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token!.RequestToken);
        Assert.AreEqual(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password })).StatusCode);
        return client;
    }

    [TestMethod]
    public async Task CustomerPeriodUsesInclusiveSaoPauloDatesAndNeverIncludesOtherUsers()
    {
        using var client = await LoginAsync(Owner);
        var result = (await client.GetFromJsonAsync<OrderSummaryResponse[]>("/api/orders?startDate=2026-11-14&endDate=2026-11-14"))!;
        CollectionAssert.AreEqual(new[] { last, first }, result.Select(o => o.Id).ToArray());
        var filtered = (await client.GetFromJsonAsync<OrderSummaryResponse[]>("/api/orders?startDate=2026-11-14&endDate=2026-11-14&status=Entregue"))!;
        CollectionAssert.AreEqual(new[] { first }, filtered.Select(o => o.Id).ToArray());
        Assert.IsFalse(result.Any(o => o.Id == foreign));
    }

    [TestMethod]
    public async Task AdministratorCombinesPeriodStatusAndExactOrderId()
    {
        using var client = await LoginAsync(Admin);
        var url = $"/api/admin/orders?startDate=2026-11-14&endDate=2026-11-14&status=Entregue&orderId={foreign}";
        var result = (await client.GetFromJsonAsync<AdminOrderSummaryResponse[]>(url))!;
        Assert.HasCount(1, result); Assert.AreEqual(foreign, result[0].Order.Id); Assert.AreEqual(Other, result[0].CustomerEmail);
        Assert.HasCount(0, (await client.GetFromJsonAsync<AdminOrderSummaryResponse[]>(url.Replace("Entregue", "Recebido")))!);
        Assert.HasCount(0, (await client.GetFromJsonAsync<AdminOrderSummaryResponse[]>($"/api/admin/orders?orderId={Guid.NewGuid()}"))!);
    }

    [TestMethod]
    public async Task OneSidedDatesAndClearedFiltersPreserveOrdering()
    {
        using var client = await LoginAsync(Owner);
        var startOnly = (await client.GetFromJsonAsync<OrderSummaryResponse[]>("/api/orders?startDate=2026-11-15"))!;
        CollectionAssert.AreEqual(new[] { after }, startOnly.Select(o => o.Id).ToArray());
        var endOnly = (await client.GetFromJsonAsync<OrderSummaryResponse[]>("/api/orders?endDate=2026-11-13"))!;
        CollectionAssert.AreEqual(new[] { before }, endOnly.Select(o => o.Id).ToArray());
        var all = (await client.GetFromJsonAsync<OrderSummaryResponse[]>("/api/orders"))!;
        CollectionAssert.AreEqual(new[] { after, last, first, before }, all.Select(o => o.Id).ToArray());
    }

    [TestMethod]
    [DataRow("status=Inexistente")]
    [DataRow("status=1")]
    [DataRow("startDate=2026-11-15&endDate=2026-11-14")]
    [DataRow("startDate=nao-e-data")]
    [DataRow("endDate=9999-12-31")]
    public async Task InvalidFiltersAreRejectedInBothLists(string query)
    {
        using var owner = await LoginAsync(Owner); using var admin = await LoginAsync(Admin);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/orders?" + query)).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/admin/orders?" + query)).StatusCode);
    }

    [TestMethod]
    public async Task InvalidIdIsRejectedAndCustomerCannotReadAdministrativeFilters()
    {
        using var admin = await LoginAsync(Admin); using var owner = await LoginAsync(Owner); using var visitor = app.CreateBrowserClient();
        Assert.AreEqual(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/admin/orders?orderId=invalid")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await owner.GetAsync($"/api/admin/orders?orderId={foreign}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await visitor.GetAsync("/api/orders?status=Recebido")).StatusCode);
    }
}
