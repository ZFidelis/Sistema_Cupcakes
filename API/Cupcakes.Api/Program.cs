using Cupcakes.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Configuration
        .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
        .AddEnvironmentVariables()
        .AddCommandLine(args);
}

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Configure ConnectionStrings:DefaultConnection por variavel de ambiente " +
        "ou appsettings.Local.json em Development.");
}

builder.Services.AddDbContext<CupcakesDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
