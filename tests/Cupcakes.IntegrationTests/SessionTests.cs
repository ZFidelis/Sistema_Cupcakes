using System.Net;
using System.Net.Http.Json;
using Cupcakes.Api.Contracts;

namespace Cupcakes.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class SessionTests
{
    private static TestApplication app = null!;
    private const string Password = "CupcakeSessao123";

    [ClassInitialize]
    public static async Task Initialize(TestContext _) => app = await TestApplication.CreateAsync();

    [ClassCleanup]
    public static async Task Cleanup()
    {
        if (app is not null) await app.DisposeAsync();
    }

    private static async Task RefreshTokenAsync(HttpClient client)
    {
        var token = await client.GetFromJsonAsync<AntiforgeryResponse>("/api/auth/antiforgery");
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token!.RequestToken);
    }

    private static async Task<LoginResponse> RegisterAndLoginAsync(HttpClient client)
    {
        await RefreshTokenAsync(client);
        var email = $"sessao-{Guid.NewGuid():N}@example.invalid";
        var registration = await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password });
        Assert.AreEqual(HttpStatusCode.Created, registration.StatusCode);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);
        await RefreshTokenAsync(client);
        return (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    [TestMethod]
    public async Task AnonymousCannotReadSessionOrLogout()
    {
        using var client = app.CreateBrowserClient();
        var session = await client.GetAsync("/api/auth/me");
        Assert.AreEqual(HttpStatusCode.Unauthorized, session.StatusCode);
        Assert.IsNull(session.Headers.Location);
        var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.AreEqual(HttpStatusCode.Unauthorized, logout.StatusCode);
        Assert.IsNull(logout.Headers.Location);
    }

    [TestMethod]
    public async Task SessionReturnsCurrentUserAndLogoutEndsAccessWithoutDeletingAccount()
    {
        using var client = app.CreateBrowserClient();
        var login = await RegisterAndLoginAsync(client);
        var response = await client.GetAsync("/api/auth/me");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        StringAssert.Contains(response.Headers.CacheControl!.ToString(), "no-store");
        var session = await response.Content.ReadFromJsonAsync<SessionResponse>();
        Assert.AreEqual(login.Id, session!.Id);
        Assert.AreEqual(login.Email, session.Email);
        CollectionAssert.AreEquivalent(new[] { "Cliente" }, session.Roles);
        Assert.IsFalse((await response.Content.ReadAsStringAsync()).Contains("password", StringComparison.OrdinalIgnoreCase));

        var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.AreEqual(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.IsTrue(logout.Headers.GetValues("Set-Cookie").Any(value => value.StartsWith("Cupcakes.Auth=;", StringComparison.Ordinal)));
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);

        await RefreshTokenAsync(client);
        var newLogin = await client.PostAsJsonAsync("/api/auth/login", new { email = login.Email, password = Password });
        Assert.AreEqual(HttpStatusCode.OK, newLogin.StatusCode);
        Assert.AreEqual(login.Id, (await newLogin.Content.ReadFromJsonAsync<LoginResponse>())!.Id);
    }

    [TestMethod]
    public async Task LogoutWithoutRequiredTokenKeepsSession()
    {
        using var client = app.CreateBrowserClient();
        await RegisterAndLoginAsync(client);
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }
}
