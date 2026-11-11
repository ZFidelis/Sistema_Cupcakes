using Cupcakes.Api.Data;
using Cupcakes.Api.Filters;
using Cupcakes.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var provisionAdmin = args.Contains("--provision-admin", StringComparer.Ordinal);
var applicationArgs = args.Where(arg => arg != "--provision-admin").ToArray();
var builder = WebApplication.CreateBuilder(applicationArgs);

if (builder.Environment.IsDevelopment())
{
    builder.Configuration
        .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
        .AddEnvironmentVariables()
        .AddCommandLine(applicationArgs);
}

builder.Services.AddDbContext<CupcakesDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")));
builder.Services
    .AddIdentity<IdentityUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;
        options.SignIn.RequireConfirmedEmail = false;
    })
    .AddEntityFrameworkStores<CupcakesDbContext>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "Cupcakes.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});
builder.Services.AddScoped<RegistrationService>();
builder.Services.AddScoped<AdminProvisioningService>();
builder.Services.AddScoped<CartService>();
builder.Services.AddScoped<CheckoutPreviewService>();
builder.Services.AddScoped<CheckoutConfirmationService>();
builder.Services.AddScoped<SimulatedPaymentService>();
builder.Services.AddScoped<OrderQueryService>();
builder.Services.AddScoped<AdminOrderQueryService>();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = "Cupcakes.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});
builder.Services.AddProblemDetails();
builder.Services.AddControllers(options => options.Filters.Add<ValidateAntiforgeryFilter>());
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();

if (provisionAdmin)
{
    await using var scope = app.Services.CreateAsyncScope();
    try
    {
        var result = await scope.ServiceProvider.GetRequiredService<AdminProvisioningService>()
            .ProvisionAsync(app.Configuration["Administrator:Email"], app.Configuration["Administrator:Password"]);
        if (result.Succeeded) Console.WriteLine(result.Message);
        else Console.Error.WriteLine(result.Message);
        Environment.ExitCode = result.Succeeded ? 0 : 1;
    }
    catch (Exception)
    {
        Console.Error.WriteLine("Falha ao provisionar administrador. Confira a conexao e aplique as migrations antes de repetir.");
        Environment.ExitCode = 1;
    }
    await app.DisposeAsync();
    return;
}

if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString("DefaultConnection")))
{
    throw new InvalidOperationException(
        "Configure ConnectionStrings:DefaultConnection por variavel de ambiente " +
        "ou appsettings.Local.json em Development.");
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (Directory.Exists(app.Environment.WebRootPath))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();

    app.MapFallbackToFile("{*path:nonfile:regex(^(?!api(/|$)).*)}", "index.html");
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program { }
