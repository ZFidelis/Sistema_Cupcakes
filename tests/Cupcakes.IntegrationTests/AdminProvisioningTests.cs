using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Cupcakes.Api.Contracts;
using Cupcakes.Api.Data;
using Cupcakes.Api.Identity;
using Cupcakes.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cupcakes.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class AdminProvisioningTests
{
    private static TestApplication app = null!;
    private const string Email = "admin@example.invalid";
    private const string Password = "CupcakeAdmin123";

    [ClassInitialize]
    public static async Task Initialize(TestContext _) => app = await TestApplication.CreateAsync();

    [ClassCleanup]
    public static async Task Cleanup()
    {
        if (app is not null) await app.DisposeAsync();
    }

    [TestInitialize]
    public async Task ClearAccounts()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        await db.Users.ExecuteDeleteAsync();
        await db.Roles.Where(role => role.Name == ApplicationRoles.Admin).ExecuteDeleteAsync();
    }

    [TestMethod]
    public async Task CreatesAdministratorWhoCanLoginAndAccessRoleRestrictedRoute()
    {
        using var scope = app.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<AdminProvisioningService>()
            .ProvisionAsync("  " + Email + "  ", Password);
        Assert.IsTrue(result.Succeeded);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = (await users.FindByEmailAsync(Email))!;
        CollectionAssert.AreEquivalent(new[] { ApplicationRoles.Admin }, (await users.GetRolesAsync(user)).ToArray());

        using var client = app.CreateBrowserClient();
        var token = await client.GetFromJsonAsync<AntiforgeryResponse>("/api/auth/antiforgery");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token!.RequestToken);
        Assert.AreEqual(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email = Email, password = Password })).StatusCode);
        var session = await client.GetFromJsonAsync<SessionResponse>("/api/auth/me");
        CollectionAssert.AreEquivalent(new[] { ApplicationRoles.Admin }, session!.Roles);
        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync("/__tests/authentication/admin")).StatusCode);
    }

    [TestMethod]
    public async Task RepetitionPreservesAccountAndOriginalPassword()
    {
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AdminProvisioningService>();
        Assert.IsTrue((await service.ProvisionAsync(Email, Password)).Succeeded);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var original = (await users.FindByEmailAsync(Email))!;
        var originalHash = original.PasswordHash;
        Assert.IsTrue((await service.ProvisionAsync(Email.ToUpperInvariant(), "OutraSenha123")).Succeeded);
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        Assert.AreEqual(1, await db.Users.CountAsync());
        Assert.AreEqual(1, await db.UserRoles.CountAsync());
        var persisted = await db.Users.AsNoTracking().SingleAsync();
        Assert.AreEqual(original.Id, persisted.Id);
        Assert.AreEqual(originalHash, persisted.PasswordHash);
        Assert.IsTrue(await users.CheckPasswordAsync(persisted, Password));
        Assert.IsFalse(await users.CheckPasswordAsync(persisted, "OutraSenha123"));
    }

    [TestMethod]
    public async Task ExistingCustomerIsNotPromotedOrModified()
    {
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var customer = new IdentityUser { UserName = Email, Email = Email };
        Assert.IsTrue((await users.CreateAsync(customer, Password)).Succeeded);
        Assert.IsTrue((await users.AddToRoleAsync(customer, ApplicationRoles.Customer)).Succeeded);
        var hash = customer.PasswordHash;
        var result = await scope.ServiceProvider.GetRequiredService<AdminProvisioningService>().ProvisionAsync(Email, "OutraSenha123");
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(hash, customer.PasswordHash);
        CollectionAssert.AreEquivalent(new[] { ApplicationRoles.Customer }, (await users.GetRolesAsync(customer)).ToArray());
        Assert.IsFalse(await scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>().RoleExistsAsync(ApplicationRoles.Admin));
    }

    [TestMethod]
    [DataRow(null, "CupcakeAdmin123")]
    [DataRow("invalid", "CupcakeAdmin123")]
    [DataRow("admin@example.invalid", null)]
    [DataRow("admin@example.invalid", "weak")]
    [DataRow("admin@example.invalid", "semnumeromaiusculo")]
    public async Task InvalidConfigurationDoesNotLeaveAccountOrRole(string? email, string? password)
    {
        using var scope = app.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<AdminProvisioningService>().ProvisionAsync(email, password);
        Assert.IsFalse(result.Succeeded);
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        Assert.AreEqual(0, await db.Users.CountAsync());
        Assert.IsFalse(await db.Roles.AnyAsync(role => role.Name == ApplicationRoles.Admin));
    }

    [TestMethod]
    public async Task FailedRoleAssignmentRollsBackUserAndNewRole()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.reject_admin_role() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'forced admin role failure'; END $$;
            CREATE TRIGGER reject_admin_role BEFORE INSERT ON "AspNetUserRoles"
            FOR EACH ROW EXECUTE FUNCTION public.reject_admin_role();
            """);
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => scope.ServiceProvider.GetRequiredService<AdminProvisioningService>()
                .ProvisionAsync(Email, Password));
            Assert.AreEqual(0, await db.Users.CountAsync());
            Assert.IsFalse(await db.Roles.AnyAsync(role => role.Name == ApplicationRoles.Admin));
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER reject_admin_role ON "AspNetUserRoles";
                DROP FUNCTION public.reject_admin_role();
                """);
        }
    }

    [TestMethod]
    public async Task CommandExitsWithResultAndDoesNotStartWebServer()
    {
        var missing = await RunCommandAsync(null, null);
        Assert.AreEqual(1, missing.Code);
        StringAssert.Contains(missing.Output, "Configure Administrator:Email");
        var created = await RunCommandAsync(Email, Password);
        Assert.AreEqual(0, created.Code);
        StringAssert.Contains(created.Output, "Administrador criado");
        var repeated = await RunCommandAsync(Email, "OutraSenha123");
        Assert.AreEqual(0, repeated.Code);
        StringAssert.Contains(repeated.Output, "ja existente");
        Assert.IsFalse(created.Output.Contains(Password, StringComparison.Ordinal));
        Assert.IsFalse(created.Output.Contains("Now listening", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task CommandWithMissingConnectionExitsWithFailureWithoutCreatingAccount()
    {
        var result = await RunCommandAsync(Email, Password, missingConnection: true);
        Assert.AreEqual(1, result.Code);
        StringAssert.Contains(result.Output, "Confira a conexao");
        using var scope = app.Services.CreateScope();
        Assert.AreEqual(0, await scope.ServiceProvider.GetRequiredService<CupcakesDbContext>().Users.CountAsync());
    }

    private static async Task<(int Code, string Output)> RunCommandAsync(string? email, string? password, bool missingConnection = false)
    {
        using var scope = app.Services.CreateScope();
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>().ContentRootPath,
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--provision-admin");
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        start.Environment["DOTNET_ENVIRONMENT"] = "Testing";
        start.Environment["ConnectionStrings__DefaultConnection"] = missingConnection ? "" : scope.ServiceProvider.GetRequiredService<CupcakesDbContext>().Database.GetConnectionString();
        start.Environment["Administrator__Email"] = email ?? "";
        start.Environment["Administrator__Password"] = password ?? "";
        using var process = Process.Start(start)!;
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, await stdout + await stderr);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }
}
