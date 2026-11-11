using System.Net;
using System.Net.Http.Json;
using Cupcakes.Api.Contracts;
using Cupcakes.Api.Data;
using Cupcakes.Api.Domain;
using Cupcakes.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Cupcakes.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class ProductReviewTests
{
    private static TestApplication app = null!;
    private static Guid productId;
    private const string First = "review-first@example.invalid";
    private const string Second = "review-second@example.invalid";
    private const string Password = "AvaliacaoTeste123";
    private static string Url => $"/api/products/{productId}/reviews";

    [ClassInitialize]
    public static async Task Initialize(TestContext _)
    {
        app = await TestApplication.CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        Assert.IsFalse(db.Database.HasPendingModelChanges());
        Assert.AreEqual(0, await db.ProductReviews.CountAsync());
        foreach (var email in new[] { First, Second })
            Assert.IsTrue((await scope.ServiceProvider.GetRequiredService<RegistrationService>()
                .RegisterAsync(new RegisterRequest { Email = email, Password = Password }, default)).Result.Succeeded);
    }
    [ClassCleanup]
    public static async Task Cleanup() { if (app is not null) await app.DisposeAsync(); }
    [TestInitialize]
    public async Task Reset()
    {
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        await db.ProductReviews.ExecuteDeleteAsync(); await db.Products.ExecuteDeleteAsync();
        var product = new Product("Teste", "Chocolate", "Ficticio", Product.DemoImagePath, 12.50m, null, 0);
        db.Products.Add(product); await db.SaveChangesAsync(); productId = product.Id;
    }
    private static async Task<HttpClient> LoginAsync(string email = First)
    {
        var client = app.CreateBrowserClient();
        await TokenAsync(client);
        Assert.AreEqual(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password })).StatusCode);
        await TokenAsync(client); return client;
    }
    private static async Task TokenAsync(HttpClient client)
    {
        var token = await client.GetFromJsonAsync<AntiforgeryResponse>("/api/auth/antiforgery");
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token!.RequestToken);
    }
    private static async Task<ProductReviewResponse> SaveAsync(HttpClient client, int rating = 5, string comment = "  Muito bom  ")
    {
        var response = await client.PutAsJsonAsync(Url + "/mine", new { rating, comment });
        Assert.IsTrue(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ProductReviewResponse>())!;
    }

    [TestMethod]
    public async Task PublicEmptyListAndOwnMissingReviewHaveExplicitResults()
    {
        using var visitor = app.CreateBrowserClient();
        var response = await visitor.GetAsync(Url);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(response.Headers.CacheControl!.NoStore);
        Assert.HasCount(0, (await response.Content.ReadFromJsonAsync<ProductReviewResponse[]>())!);
        using var owner = await LoginAsync();
        Assert.AreEqual(HttpStatusCode.NotFound, (await owner.GetAsync(Url + "/mine")).StatusCode);
    }

    [TestMethod]
    public async Task CustomerWithoutPurchaseCanCreateUpdateAndRepeatOwnReview()
    {
        using var client = await LoginAsync();
        var create = await client.PutAsJsonAsync(Url + "/mine", new { rating = 5, comment = "  Muito bom  " });
        Assert.AreEqual(HttpStatusCode.Created, create.StatusCode);
        var original = (await create.Content.ReadFromJsonAsync<ProductReviewResponse>())!;
        Assert.AreEqual("Muito bom", original.Comment);
        Assert.AreEqual(original.CreatedAt, original.UpdatedAt);
        var edited = await SaveAsync(client, 4, "Comentario novo");
        Assert.AreEqual(original.Id, edited.Id); Assert.AreEqual(original.CreatedAt, edited.CreatedAt);
        Assert.IsTrue(edited.UpdatedAt > original.UpdatedAt);
        Assert.AreEqual(edited, await SaveAsync(client, 4, "  Comentario novo  "));
        Assert.AreEqual(edited, await client.GetFromJsonAsync<ProductReviewResponse>(Url + "/mine"));
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        Assert.AreEqual(1, await db.ProductReviews.CountAsync()); Assert.AreEqual(0, await db.Orders.CountAsync());
        Assert.AreEqual(0, await db.Products.Select(value => value.Stock).SingleAsync());
    }

    [TestMethod]
    public async Task PublicCommentsAreOrderedWithoutExposingAuthorIdentity()
    {
        using var first = await LoginAsync(); using var second = await LoginAsync(Second);
        var old = await SaveAsync(first); var recent = await SaveAsync(second, 3, "Outro comentario");
        using var visitor = app.CreateBrowserClient();
        var response = await visitor.GetAsync(Url);
        var values = (await response.Content.ReadFromJsonAsync<ProductReviewResponse[]>())!;
        CollectionAssert.AreEqual(new[] { recent.Id, old.Id }, values.Select(value => value.Id).ToArray());
        var json = await response.Content.ReadAsStringAsync();
        Assert.IsFalse(json.Contains("userId", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("@example.invalid", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("password", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task UserCannotSelectAnotherAuthorAndOwnEndpointStaysIsolated()
    {
        using var first = await LoginAsync(); using var second = await LoginAsync(Second);
        var original = await SaveAsync(first);
        Assert.AreEqual(HttpStatusCode.NotFound, (await second.GetAsync(Url + "/mine")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await second.PutAsJsonAsync(Url + "/mine", new { rating = 1, comment = "Teste", userId = "other" })).StatusCode);
        var own = await SaveAsync(second, 2, "Minha opiniao");
        Assert.AreNotEqual(original.Id, own.Id);
        Assert.AreEqual(original, await first.GetFromJsonAsync<ProductReviewResponse>(Url + "/mine"));
    }

    [TestMethod]
    [DataRow("missingRating")]
    [DataRow("zero")]
    [DataRow("six")]
    [DataRow("fraction")]
    [DataRow("empty")]
    [DataRow("spaces")]
    [DataRow("null")]
    [DataRow("long")]
    public async Task InvalidReviewIsRejectedWithoutChangingExistingOne(string invalid)
    {
        using var client = await LoginAsync(); var original = await SaveAsync(client);
        var payload = new Dictionary<string, object?> { ["rating"] = 5, ["comment"] = "Novo" };
        switch (invalid)
        {
            case "missingRating": payload.Remove("rating"); break;
            case "zero": payload["rating"] = 0; break;
            case "six": payload["rating"] = 6; break;
            case "fraction": payload["rating"] = 1.5m; break;
            case "empty": payload["comment"] = ""; break;
            case "spaces": payload["comment"] = " \t "; break;
            case "null": payload["comment"] = null; break;
            case "long": payload["comment"] = new string('a', 1001); break;
        }
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(Url + "/mine", payload)).StatusCode);
        Assert.AreEqual(original, await client.GetFromJsonAsync<ProductReviewResponse>(Url + "/mine"));
    }

    [TestMethod]
    public async Task AuthenticationAndWriteTokenAreRequiredOnlyForOwnOperations()
    {
        using var visitor = app.CreateBrowserClient();
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await visitor.GetAsync(Url + "/mine")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await visitor.PutAsJsonAsync(Url + "/mine", new { rating = 5, comment = "Teste" })).StatusCode);
        using var owner = await LoginAsync(); owner.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Assert.AreEqual(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync(Url + "/mine", new { rating = 5, comment = "Teste" })).StatusCode);
    }

    [TestMethod]
    public async Task MissingProductAndInactiveProductRejectWritesButKeepExistingReview()
    {
        using var client = await LoginAsync(); var original = await SaveAsync(client);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        await db.Products.ExecuteUpdateAsync(set => set.SetProperty(value => value.IsActive, false));
        using var visitor = app.CreateBrowserClient();
        Assert.AreEqual(HttpStatusCode.NotFound, (await visitor.GetAsync(Url)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(Url + "/mine", new { rating = 3, comment = "Novo" })).StatusCode);
        Assert.AreEqual(original, await client.GetFromJsonAsync<ProductReviewResponse>(Url + "/mine"));
        var missing = $"/api/products/{Guid.NewGuid()}/reviews";
        Assert.AreEqual(HttpStatusCode.NotFound, (await visitor.GetAsync(missing)).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(missing + "/mine", new { rating = 3, comment = "Novo" })).StatusCode);
    }

    [TestMethod]
    public async Task ConcurrentFirstWritesKeepOneReviewPerUserAndProduct()
    {
        using var first = await LoginAsync(); using var second = await LoginAsync();
        var responses = await Task.WhenAll(first.PutAsJsonAsync(Url + "/mine", new { rating = 2, comment = "Primeira" }),
            second.PutAsJsonAsync(Url + "/mine", new { rating = 4, comment = "Segunda" }));
        CollectionAssert.AreEquivalent(new[] { HttpStatusCode.Created, HttpStatusCode.OK }, responses.Select(value => value.StatusCode).ToArray());
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        Assert.AreEqual(1, await db.ProductReviews.CountAsync());
        Assert.IsTrue((await db.ProductReviews.SingleAsync()).Rating is 2 or 4);
    }

    [TestMethod]
    [DataRow("rating")]
    [DataRow("comment")]
    [DataRow("duplicate")]
    public async Task DatabaseProtectsRatingCommentAndUniqueness(string invalid)
    {
        using var client = await LoginAsync(); await SaveAsync(client);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        if (invalid == "duplicate")
        {
            var existing = await db.ProductReviews.AsNoTracking().SingleAsync();
            db.ProductReviews.Add(new ProductReview(existing.UserId, existing.ProductId, 4, "Duplicada"));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.AreEqual(PostgresErrorCodes.UniqueViolation, ((PostgresException)error.InnerException!).SqlState);
        }
        else
        {
            var sql = invalid == "rating" ? "UPDATE \"ProductReviews\" SET \"Rating\"=0" : "UPDATE \"ProductReviews\" SET \"Comment\"='   '";
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
            Assert.AreEqual(PostgresErrorCodes.CheckViolation, error.SqlState);
        }
    }

    [TestMethod]
    public async Task MarkupRemainsCommentTextInJsonAndMaximumLengthIsAccepted()
    {
        using var client = await LoginAsync(); var text = "<script>alert('teste')</script>";
        var result = await SaveAsync(client, 5, text);
        Assert.AreEqual(text, result.Comment);
        using var visitor = app.CreateBrowserClient(); var response = await visitor.GetAsync(Url);
        Assert.AreEqual("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.AreEqual(text, (await response.Content.ReadFromJsonAsync<ProductReviewResponse[]>())!.Single().Comment);
        Assert.AreEqual(1000, (await SaveAsync(client, 1, new string('a', 1000))).Comment.Length);
    }
}
