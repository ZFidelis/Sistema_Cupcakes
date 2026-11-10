using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cupcakes.Api.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Cupcakes.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class LoginTests
{
    private static TestApplication app = null!;
    private const string Password = "CupcakeLogin123";
    private const string WrongPassword = "SenhaIncorreta123";

    [ClassInitialize]
    public static async Task Initialize(TestContext _) => app = await TestApplication.CreateAsync();

    [ClassCleanup]
    public static async Task Cleanup()
    {
        if (app is not null) await app.DisposeAsync();
    }

    private static string Email() => $"login-{Guid.NewGuid():N}@example.invalid";

    private static async Task RefreshTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/antiforgery");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var token = await response.Content.ReadFromJsonAsync<AntiforgeryResponse>();
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token!.RequestToken);
    }

    private static async Task<HttpClient> ClientAsync()
    {
        var client = app.CreateBrowserClient();
        await RefreshTokenAsync(client);
        return client;
    }

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var email = Email();
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        return email;
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password = Password)
        => client.PostAsJsonAsync("/api/auth/login", new { email, password });

    private static void AssertNoSessionCookie(HttpResponseMessage response)
    {
        Assert.IsFalse(response.Headers.TryGetValues("Set-Cookie", out var values)
            && values.Any(value => value.StartsWith("Cupcakes.Auth=", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task SuccessfulLoginIssuesSecureSessionCookieAndAuthenticatesNextRequest()
    {
        using var client = await ClientAsync();
        var email = await RegisterAsync(client);
        var response = await LoginAsync(client, $"  {email.ToUpperInvariant()}  ");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.AreEqual(email, profile!.Email);
        CollectionAssert.AreEquivalent(new[] { "Cliente" }, profile.Roles);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("Cupcakes.Auth=", StringComparison.Ordinal));
        var lowerCookie = cookie.ToLowerInvariant();
        StringAssert.Contains(lowerCookie, "httponly");
        StringAssert.Contains(lowerCookie, "secure");
        StringAssert.Contains(lowerCookie, "samesite=lax");
        Assert.IsFalse(lowerCookie.Contains("expires=", StringComparison.Ordinal));
        Assert.IsFalse(lowerCookie.Contains("max-age=", StringComparison.Ordinal));
        StringAssert.Contains(response.Headers.CacheControl!.ToString(), "no-store");
        var json = await response.Content.ReadAsStringAsync();
        Assert.IsFalse(json.Contains(Password, StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("passwordHash", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("token", StringComparison.OrdinalIgnoreCase));

        var probe = await client.GetAsync("/__tests/authentication");
        Assert.AreEqual(HttpStatusCode.OK, probe.StatusCode);
        var claims = await probe.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(profile.Id, claims.GetProperty("id").GetString());
        Assert.IsTrue(claims.GetProperty("customer").GetBoolean());
        await RefreshTokenAsync(client);
        var invalidInput = await client.PostAsJsonAsync("/api/auth/login", new { });
        Assert.AreEqual(HttpStatusCode.BadRequest, invalidInput.StatusCode);
        Assert.IsTrue((await invalidInput.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("errors", out _));
        AssertNoSessionCookie(invalidInput);
    }

    [TestMethod]
    public async Task UnknownAccountAndWrongPasswordHaveSameGenericFailure()
    {
        using var client = await ClientAsync();
        var email = await RegisterAsync(client);
        var wrong = await LoginAsync(client, email, WrongPassword);
        var unknown = await LoginAsync(client, Email(), WrongPassword);
        Assert.AreEqual(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, unknown.StatusCode);
        var wrongBody = await wrong.Content.ReadFromJsonAsync<JsonElement>();
        var unknownBody = await unknown.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(wrongBody.GetProperty("title").GetString(), unknownBody.GetProperty("title").GetString());
        Assert.AreEqual(wrongBody.GetProperty("detail").GetString(), unknownBody.GetProperty("detail").GetString());
        AssertNoSessionCookie(wrong);
        AssertNoSessionCookie(unknown);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/__tests/authentication")).StatusCode);
    }

    [TestMethod]
    public async Task FifthFailureLocksAccountForFifteenMinutesAndRejectsCorrectPassword()
    {
        using var client = await ClientAsync();
        var email = await RegisterAsync(client);
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await LoginAsync(client, email, WrongPassword)).StatusCode);
            using var scope = app.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = await users.FindByEmailAsync(email);
            Assert.AreEqual(attempt, user!.AccessFailedCount);
            Assert.IsFalse(await users.IsLockedOutAsync(user));
        }

        var before = DateTimeOffset.UtcNow;
        var fifth = await LoginAsync(client, email, WrongPassword);
        Assert.AreEqual(HttpStatusCode.Unauthorized, fifth.StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = await users.FindByEmailAsync(email);
            Assert.IsTrue(await users.IsLockedOutAsync(user!));
            Assert.IsTrue(user!.LockoutEnd >= before.AddMinutes(15));
            Assert.IsTrue(user.LockoutEnd <= DateTimeOffset.UtcNow.AddMinutes(15));
        }
        var blocked = await LoginAsync(client, email);
        Assert.AreEqual(HttpStatusCode.Unauthorized, blocked.StatusCode);
        var fifthBody = await fifth.Content.ReadFromJsonAsync<JsonElement>();
        var blockedBody = await blocked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(fifthBody.GetProperty("detail").GetString(), blockedBody.GetProperty("detail").GetString());
        AssertNoSessionCookie(blocked);
    }

    [TestMethod]
    public async Task ExpiredLockoutAllowsCorrectLogin()
    {
        using var client = await ClientAsync();
        var email = await RegisterAsync(client);
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = await users.FindByEmailAsync(email);
            Assert.IsTrue((await users.SetLockoutEndDateAsync(user!, DateTimeOffset.UtcNow.AddMinutes(-1))).Succeeded);
        }
        Assert.AreEqual(HttpStatusCode.OK, (await LoginAsync(client, email)).StatusCode);
    }

    [TestMethod]
    public async Task SuccessfulLoginResetsPreviousFailedAttempts()
    {
        using var client = await ClientAsync();
        var email = await RegisterAsync(client);
        for (var i = 0; i < 2; i++)
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await LoginAsync(client, email, WrongPassword)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await LoginAsync(client, email)).StatusCode);
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        Assert.AreEqual(0, (await users.FindByEmailAsync(email))!.AccessFailedCount);
    }

    [TestMethod]
    public async Task MissingAndInvalidAntiforgeryTokenDoNotAuthenticateOrCountPasswordFailures()
    {
        using var client = await ClientAsync();
        var email = await RegisterAsync(client);
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Assert.AreEqual(HttpStatusCode.BadRequest, (await LoginAsync(client, email)).StatusCode);
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", "invalido");
        Assert.AreEqual(HttpStatusCode.BadRequest, (await LoginAsync(client, email)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/__tests/authentication")).StatusCode);
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        Assert.AreEqual(0, (await users.FindByEmailAsync(email))!.AccessFailedCount);
    }

    [TestMethod]
    [DataRow("invalido", "senha")]
    [DataRow("", "senha")]
    [DataRow("cliente@example.invalid", "")]
    public async Task InvalidInputReturnsBadRequest(string email, string password)
    {
        using var client = await ClientAsync();
        var response = await LoginAsync(client, email, password);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoSessionCookie(response);
    }

    [TestMethod]
    public async Task PasswordLongerThan128CharactersIsRejected()
    {
        using var client = await ClientAsync();
        var response = await LoginAsync(client, Email(), new string('a', 129));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoSessionCookie(response);
    }

    [TestMethod]
    public async Task ClientCannotChoosePersistentCookieOrPrivileges()
    {
        using var client = await ClientAsync();
        var email = await RegisterAsync(client);
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password, rememberMe = true, role = "Administrador" });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoSessionCookie(response);
    }

    [TestMethod]
    public async Task CookieAuthorizesCustomerButNotAdministratorWithoutRedirects()
    {
        using var client = await ClientAsync();
        var anonymous = await client.GetAsync("/__tests/authentication");
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.IsNull(anonymous.Headers.Location);
        var email = await RegisterAsync(client);
        Assert.AreEqual(HttpStatusCode.OK, (await LoginAsync(client, email)).StatusCode);
        var forbidden = await client.GetAsync("/__tests/authentication/admin");
        Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.IsNull(forbidden.Headers.Location);
    }
}
