using System.Net;
using System.Net.Http.Json;
using System.Text;
using Cupcakes.Api.Contracts;
using Cupcakes.Api.Data;
using Cupcakes.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cupcakes.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class CheckoutPreviewTests
{
    private static TestApplication app = null!;
    private const string Password = "CheckoutTeste123";
    private const string PreviewPath = "/api/checkout/preview";

    [ClassInitialize]
    public static async Task Initialize(TestContext _) => app = await TestApplication.CreateAsync();

    [ClassCleanup]
    public static async Task Cleanup()
    {
        if (app is not null) await app.DisposeAsync();
    }

    private static Dictionary<string, object?> Address() => new()
    {
        ["recipient"] = " Pessoa Ficticia ", ["street"] = " Rua de Teste ", ["number"] = " s/n ",
        ["neighborhood"] = " Bairro ", ["city"] = " Cidade ", ["state"] = "sp",
        ["postalCode"] = "01234567", ["complement"] = " "
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
        var email = $"preview-{Guid.NewGuid():N}@example.invalid";
        Assert.AreEqual(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password })).StatusCode);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);
        await TokenAsync(client);
        return (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Id;
    }

    private static async Task<Product> AddProductAsync(HttpClient client, decimal price = 12.50m,
        decimal? promotion = null, int quantity = 2, int stock = 10)
    {
        var product = new Product("Teste " + Guid.NewGuid().ToString("N"), "Chocolate", "Produto ficticio",
            Product.DemoImagePath, price, promotion, stock);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        db.Products.Add(product);
        await db.SaveChangesAsync();
        Assert.AreEqual(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/cart/items", new { productId = product.Id, quantity })).StatusCode);
        return product;
    }

    private static async Task<CheckoutPreviewResponse> PreviewAsync(HttpClient client, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await client.PostAsJsonAsync(PreviewPath, new { address = Address() });
        Assert.AreEqual(expected, response.StatusCode,
            (await response.Content.ReadAsStringAsync()) + "\n" + app.LoggedExceptions.LastOrDefault());
        StringAssert.Contains(response.Headers.CacheControl!.ToString(), "no-store");
        return (await response.Content.ReadFromJsonAsync<CheckoutPreviewResponse>())!;
    }

    [TestMethod]
    public async Task AnonymousPreviewIsRejected()
    {
        using var client = app.CreateBrowserClient();
        Assert.AreEqual(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync(PreviewPath, new { address = Address() })).StatusCode);
    }

    [TestMethod]
    public async Task ExistingWriteTokenIsRequired()
    {
        using var client = app.CreateBrowserClient();
        await LoginAsync(client);
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Assert.AreEqual(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync(PreviewPath, new { address = Address() })).StatusCode);
    }

    [TestMethod]
    public async Task ValidPreviewUsesDatabaseTotalsAndNormalizedAddressWithoutSideEffectsEvenWhenRepeated()
    {
        using var client = app.CreateBrowserClient();
        var userId = await LoginAsync(client);
        var regular = await AddProductAsync(client);
        var promotion = await AddProductAsync(client, 14m, 11.90m, 3);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var preview = await PreviewAsync(client);
            Assert.IsTrue(preview.CanProceed);
            Assert.HasCount(0, preview.Issues);
            Assert.AreEqual(67m, preview.Cart.Subtotal);
            Assert.AreEqual(6.30m, preview.Cart.Discount);
            Assert.AreEqual(60.70m, preview.Cart.Total);
            Assert.AreEqual(5L, preview.Cart.ItemCount);
            Assert.AreEqual("Pessoa Ficticia", preview.Address.Recipient);
            Assert.AreEqual("Rua de Teste", preview.Address.Street);
            Assert.AreEqual("s/n", preview.Address.Number);
            Assert.AreEqual("SP", preview.Address.State);
            Assert.AreEqual("01234567", preview.Address.PostalCode);
            Assert.IsNull(preview.Address.Complement);
            StringAssert.Contains(preview.Notice, "sem criar pedido");
        }

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        Assert.AreEqual(0, await db.Orders.CountAsync());
        Assert.AreEqual(0, await db.OrderItems.CountAsync());
        Assert.AreEqual(0, await db.OrderAddresses.CountAsync());
        Assert.AreEqual(0, await db.OrderStatusHistory.CountAsync());
        Assert.AreEqual(10, await db.Products.Where(x => x.Id == regular.Id).Select(x => x.Stock).SingleAsync());
        Assert.AreEqual(10, await db.Products.Where(x => x.Id == promotion.Id).Select(x => x.Stock).SingleAsync());
        var items = await db.Carts.Where(x => x.UserId == userId).SelectMany(x => x.Items).ToArrayAsync();
        Assert.AreEqual(5, items.Sum(x => x.Quantity));
    }

    [TestMethod]
    public async Task EmptyCartReturnsConflictWithEmptySummaryWithoutCreatingCart()
    {
        using var client = app.CreateBrowserClient();
        var userId = await LoginAsync(client);
        var preview = await PreviewAsync(client, HttpStatusCode.Conflict);
        Assert.IsFalse(preview.CanProceed);
        Assert.AreEqual("empty_cart", preview.Issues.Single().Code);
        Assert.AreEqual(0m, preview.Cart.Total);
        using var scope = app.Services.CreateScope();
        Assert.IsFalse(await scope.ServiceProvider.GetRequiredService<CupcakesDbContext>().Carts.AnyAsync(x => x.UserId == userId));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ProductDeactivationOrInsufficientStockReturnsCurrentSummaryAndProductIssue(bool deactivate)
    {
        using var client = app.CreateBrowserClient();
        await LoginAsync(client);
        var product = await AddProductAsync(client);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        if (deactivate)
            await db.Products.Where(x => x.Id == product.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.IsActive, false));
        else
            await db.Products.Where(x => x.Id == product.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.Stock, 1));
        var preview = await PreviewAsync(client, HttpStatusCode.Conflict);
        Assert.IsFalse(preview.CanProceed);
        Assert.IsTrue(preview.Cart.HasUnavailableItems);
        Assert.AreEqual(product.Id, preview.Issues.Single().ProductId);
        Assert.AreEqual("unavailable_product", preview.Issues.Single().Code);
        Assert.AreEqual(2, preview.Cart.Items.Single().Quantity);
        Assert.AreEqual(0, await db.Orders.CountAsync());
    }

    [TestMethod]
    public async Task NewPreviewReflectsPriceAndPromotionChangesInsteadOfHoldingOldPrices()
    {
        using var client = app.CreateBrowserClient();
        await LoginAsync(client);
        var product = await AddProductAsync(client);
        Assert.AreEqual(25m, (await PreviewAsync(client)).Cart.Total);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        await db.Products.Where(x => x.Id == product.Id).ExecuteUpdateAsync(set => set
            .SetProperty(x => x.Price, 15m).SetProperty(x => x.PromotionalPrice, (decimal?)10.01m));
        var updated = await PreviewAsync(client);
        Assert.AreEqual(30m, updated.Cart.Subtotal);
        Assert.AreEqual(9.98m, updated.Cart.Discount);
        Assert.AreEqual(20.02m, updated.Cart.Total);
    }

    [TestMethod]
    public async Task AnotherUserCannotChooseOwnersCartBySendingUserIdOrPrices()
    {
        using var owner = app.CreateBrowserClient();
        using var other = app.CreateBrowserClient();
        var ownerId = await LoginAsync(owner);
        await LoginAsync(other);
        var ownersProduct = await AddProductAsync(owner);
        var empty = await other.PostAsJsonAsync(PreviewPath, new { address = Address(), userId = ownerId, total = .01m });
        Assert.AreEqual(HttpStatusCode.Conflict, empty.StatusCode);
        Assert.HasCount(0, (await empty.Content.ReadFromJsonAsync<CheckoutPreviewResponse>())!.Cart.Items);
        var ownProduct = await AddProductAsync(other, 7.25m, quantity: 1);
        var preview = await PreviewAsync(other);
        Assert.AreEqual(ownProduct.Id, preview.Cart.Items.Single().ProductId);
        Assert.AreNotEqual(ownersProduct.Id, preview.Cart.Items.Single().ProductId);
        Assert.AreEqual(7.25m, preview.Cart.Total);
    }

    [TestMethod]
    public async Task PreviewEnforcesOrderMoneyLimitBeforeConfirmation()
    {
        using var client = app.CreateBrowserClient();
        await LoginAsync(client);
        var product = await AddProductAsync(client, Product.MaximumPrice, quantity: 1, stock: 2);
        Assert.AreEqual(Product.MaximumPrice, (await PreviewAsync(client)).Cart.Total);
        Assert.AreEqual(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/cart/items/{product.Id}", new { quantity = 2 })).StatusCode);
        var preview = await PreviewAsync(client, HttpStatusCode.Conflict);
        Assert.IsFalse(preview.CanProceed);
        Assert.AreEqual("amount_limit", preview.Issues.Single().Code);
        Assert.AreEqual(Product.MaximumPrice * 2, preview.Cart.Total);
    }

    [TestMethod]
    [DataRow("recipient")]
    [DataRow("street")]
    [DataRow("number")]
    [DataRow("neighborhood")]
    [DataRow("city")]
    [DataRow("state")]
    [DataRow("postalCode")]
    [DataRow("complement")]
    public async Task InvalidAddressReturnsValidationError(string field)
    {
        using var client = app.CreateBrowserClient();
        await LoginAsync(client);
        var address = Address();
        address[field] = field switch {
            "state" => "S1", "postalCode" => "12345-678", "complement" => new string('a', 101), _ => " "
        };
        var response = await client.PostAsJsonAsync(PreviewPath, new { address });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors;
        Assert.IsNotEmpty(errors);
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("{\"address\":null}")]
    [DataRow("{\"address\":{}}")]
    public async Task MissingAddressIsRejected(string json)
    {
        using var client = app.CreateBrowserClient();
        await LoginAsync(client);
        var response = await client.PostAsync(PreviewPath, new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task ExtraPaymentAndPriceFieldsDoNotCreateOrderOrChangePreviewTotals()
    {
        using var client = app.CreateBrowserClient();
        await LoginAsync(client);
        await AddProductAsync(client);
        var response = await client.PostAsJsonAsync(PreviewPath, new {
            address = Address(), total = .01m, price = .01m, paymentMethod = "Cartao", paymentResult = "Aprovado"
        });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(25m, (await response.Content.ReadFromJsonAsync<CheckoutPreviewResponse>())!.Cart.Total);
        using var scope = app.Services.CreateScope();
        Assert.AreEqual(0, await scope.ServiceProvider.GetRequiredService<CupcakesDbContext>().Orders.CountAsync());
    }
}
