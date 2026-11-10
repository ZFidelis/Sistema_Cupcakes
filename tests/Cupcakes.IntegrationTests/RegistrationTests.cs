using System.Net;
using System.Net.Http.Json;
using Cupcakes.Api.Contracts;
using Cupcakes.Api.Data;
using Cupcakes.Api.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cupcakes.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class RegistrationTests
{
    private static TestApplication app = null!;
    private const string ValidPassword = "CupcakeTeste123";

    [ClassInitialize]
    public static async Task Initialize(TestContext _) => app = await TestApplication.CreateAsync();

    [ClassCleanup]
    public static async Task Cleanup()
    {
        if (app is not null) await app.DisposeAsync();
    }

    private static string Email() => $"cliente-{Guid.NewGuid():N}@example.invalid";

    private static async Task<HttpClient> ClientAsync()
    {
        var client = app.CreateBrowserClient();
        var response = await client.GetAsync("/api/auth/antiforgery");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            string.Join(Environment.NewLine, app.LoggedExceptions.Select(exception => exception.ToString())));
        StringAssert.Contains(response.Headers.CacheControl!.ToString(), "no-store");
        var token = await response.Content.ReadFromJsonAsync<AntiforgeryResponse>();
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token!.RequestToken);
        return client;
    }

    [TestMethod]
    public async Task RegistersOnlyCustomerWithHashedPasswordAndNoSession()
    {
        using var client = await ClientAsync();
        var email = Email();
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email = $"  {email}  ", password = ValidPassword });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.AreEqual(email, body!.Email);
        Assert.IsFalse(response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(value => value.StartsWith("Cupcakes.Auth=", StringComparison.Ordinal)));

        using var scope = app.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = await manager.FindByIdAsync(body.Id);
        Assert.IsNotNull(user);
        Assert.IsFalse(user.EmailConfirmed);
        CollectionAssert.AreEquivalent(new[] { ApplicationRoles.Customer }, (await manager.GetRolesAsync(user)).ToArray());
        Assert.AreNotEqual(ValidPassword, user.PasswordHash);
        Assert.IsTrue(await manager.CheckPasswordAsync(user, ValidPassword));
        var json = await response.Content.ReadAsStringAsync();
        Assert.IsFalse(json.Contains(ValidPassword, StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("passwordHash", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    [DataRow("invalido", "CupcakeTeste123")]
    [DataRow("", "CupcakeTeste123")]
    [DataRow("valid@example.invalid", "Curta1")]
    [DataRow("valid@example.invalid", "semdigitomaiusculo")]
    [DataRow("valid@example.invalid", "minuscula123")]
    [DataRow("valid@example.invalid", "MAIUSCULA123")]
    [DataRow("valid@example.invalid", "SemDigitoMaiusculo")]
    public async Task RejectsInvalidInput(string email, string password)
    {
        using var client = await ClientAsync();
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        Assert.IsFalse(await db.Users.AnyAsync(user => user.Email == email));
    }

    [TestMethod]
    public async Task EnforcesPasswordMaximumWithoutTruncation()
    {
        using var client = await ClientAsync();
        var valid = await client.PostAsJsonAsync("/api/auth/register", new { email = Email(), password = "A1" + new string('a', 126) });
        Assert.AreEqual(HttpStatusCode.Created, valid.StatusCode);
        var email = Email();
        var invalid = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "A1" + new string('a', 127) });
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [TestMethod]
    public async Task RejectsDuplicateEmailIgnoringCase()
    {
        using var client = await ClientAsync();
        var email = Email();
        Assert.AreEqual(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/auth/register", new { email, password = ValidPassword })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/auth/register", new { email = email.ToUpperInvariant(), password = ValidPassword })).StatusCode);
    }

    [TestMethod]
    public async Task ConcurrentDuplicateRequestsCreateOneCustomer()
    {
        using var first = await ClientAsync();
        using var second = await ClientAsync();
        var email = Email();
        var responses = await Task.WhenAll(first.PostAsJsonAsync("/api/auth/register", new { email, password = ValidPassword }),
            second.PostAsJsonAsync("/api/auth/register", new { email, password = ValidPassword }));
        CollectionAssert.AreEquivalent(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }, responses.Select(response => response.StatusCode).ToArray());
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        Assert.AreEqual(1, await db.Users.CountAsync(user => user.NormalizedEmail == email.ToUpperInvariant()));
    }

    [TestMethod]
    public async Task RejectsPrivilegeFields()
    {
        using var client = await ClientAsync();
        var email = Email();
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password = ValidPassword, role = "Administrador" });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        Assert.IsFalse(await db.Users.AnyAsync(user => user.Email == email));
        Assert.IsFalse(await db.Roles.AnyAsync(role => role.Name == "Administrador"));
    }

    [TestMethod]
    public async Task RejectsMissingOrInvalidAntiforgeryToken()
    {
        using var client = app.CreateBrowserClient();
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", new { email = Email(), password = ValidPassword })).StatusCode);
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", "invalido");
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", new { email = Email(), password = ValidPassword })).StatusCode);
    }

    [TestMethod]
    public async Task RollsBackAccountWhenRoleAssignmentFails()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.reject_registration_role() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'forced test role failure'; END $$;
            CREATE TRIGGER reject_registration_role BEFORE INSERT ON "AspNetUserRoles"
            FOR EACH ROW EXECUTE FUNCTION public.reject_registration_role();
            """);
        var email = Email();
        try
        {
            using var client = await ClientAsync();
            var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password = ValidPassword });
            Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.IsFalse((await response.Content.ReadAsStringAsync()).Contains("forced test role failure", StringComparison.Ordinal));
            Assert.IsFalse(await db.Users.AnyAsync(user => user.Email == email));
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER reject_registration_role ON "AspNetUserRoles";
                DROP FUNCTION public.reject_registration_role();
                """);
        }
    }
}
