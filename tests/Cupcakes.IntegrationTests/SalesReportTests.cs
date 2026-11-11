using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Cupcakes.Api.Contracts;
using Cupcakes.Api.Data;
using Cupcakes.Api.Domain;
using Cupcakes.Api.Services;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cupcakes.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class SalesReportTests
{
    private static TestApplication app = null!;
    private const string Admin = "sales-admin@example.invalid";
    private const string Customer = "sales-customer@example.invalid";
    private const string Password = "RelatorioTeste123";

    [ClassInitialize]
    public static async Task Initialize(TestContext _)
    {
        app = await TestApplication.CreateAsync();
        using var scope = app.Services.CreateScope();
        Assert.IsTrue((await scope.ServiceProvider.GetRequiredService<AdminProvisioningService>().ProvisionAsync(Admin, Password)).Succeeded);
        Assert.IsTrue((await scope.ServiceProvider.GetRequiredService<RegistrationService>()
            .RegisterAsync(new RegisterRequest { Email = Customer, Password = Password }, default)).Result.Succeeded);
    }

    [ClassCleanup]
    public static async Task Cleanup() { if (app is not null) await app.DisposeAsync(); }

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

    private static async Task<Order> SeedAsync(string utc, int quantity = 2, decimal price = 14m, decimal? promotion = 11.90m)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        var owner = await db.Users.Where(user => user.Email == Customer).Select(user => user.Id).SingleAsync();
        var product = new Product("Produto historico", "Morango", "Ficticio", Product.DemoImagePath, price, promotion, Math.Max(quantity, 10));
        db.Products.Add(product);
        var order = new Order(owner, SimulatedPaymentMethod.Pix,
            new OrderAddress("Pessoa", "Rua", "1", "Bairro", "Cidade", "SP", "01234567", null),
            [new OrderItem(product.Id, product.Name, price, promotion ?? price, quantity)]);
        db.Orders.Add(order); await db.SaveChangesAsync();
        var createdAt = DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture).ToUniversalTime();
        await db.Orders.Where(value => value.Id == order.Id).ExecuteUpdateAsync(set => set.SetProperty(value => value.CreatedAt, createdAt));
        return order;
    }

    private static async Task<SalesReportResponse> ReportAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync("/api/admin/reports/sales?" + query);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(response.Headers.CacheControl!.NoStore);
        return (await response.Content.ReadFromJsonAsync<SalesReportResponse>())!;
    }

    [TestMethod]
    [DataRow("day", 1)]
    [DataRow("week", 7)]
    public async Task EmptyReportReturnsZeroForEveryDay(string period, int days)
    {
        using var client = await LoginAsync(Admin);
        var result = await ReportAsync(client, "date=2026-11-11&period=" + period);
        Assert.IsTrue(result.IsEmpty && result.IsSimulation);
        Assert.AreEqual("BRL", result.Currency); Assert.AreEqual("America/Sao_Paulo", result.TimeZone);
        Assert.AreEqual(0L, result.OrderCount); Assert.AreEqual(0L, result.ItemCount);
        Assert.AreEqual(0m, result.Subtotal); Assert.AreEqual(0m, result.Discount); Assert.AreEqual(0m, result.Total);
        Assert.HasCount(days, result.Days);
        Assert.IsTrue(result.Days.All(day => day.OrderCount == 0 && day.Total == 0));
    }

    [TestMethod]
    public async Task DailyReportUsesInclusiveLocalMidnightAndExclusiveNextMidnight()
    {
        await SeedAsync("2026-11-11T02:59:59.999999Z");
        await SeedAsync("2026-11-11T03:00:00Z");
        await SeedAsync("2026-11-12T02:59:59.999999Z", 1, promotion: null);
        await SeedAsync("2026-11-12T03:00:00Z");
        using var client = await LoginAsync(Admin);
        var result = await ReportAsync(client, "date=2026-11-11");
        Assert.AreEqual("day", result.Period);
        Assert.AreEqual(new DateOnly(2026, 11, 11), result.StartDate); Assert.AreEqual(result.StartDate, result.EndDate);
        Assert.IsFalse(result.IsEmpty);
        Assert.AreEqual(2L, result.OrderCount); Assert.AreEqual(3L, result.ItemCount);
        Assert.AreEqual(42m, result.Subtotal); Assert.AreEqual(4.20m, result.Discount); Assert.AreEqual(37.80m, result.Total);
        Assert.AreEqual(result.Total, result.Days.Single().Total);
    }

    [TestMethod]
    public async Task WeekStartsOnMondayAndIncludesSundayAcrossYearBoundary()
    {
        await SeedAsync("2025-12-29T02:59:59.999999Z");
        await SeedAsync("2025-12-29T03:00:00Z");
        await SeedAsync("2026-01-05T02:59:59.999999Z");
        await SeedAsync("2026-01-05T03:00:00Z");
        using var client = await LoginAsync(Admin);
        var result = await ReportAsync(client, "date=2026-01-04&period=week");
        Assert.AreEqual(new DateOnly(2025, 12, 29), result.StartDate);
        Assert.AreEqual(new DateOnly(2026, 1, 4), result.EndDate);
        Assert.AreEqual(2L, result.OrderCount); Assert.AreEqual(47.60m, result.Total);
        Assert.AreEqual(1L, result.Days[0].OrderCount); Assert.AreEqual(1L, result.Days[6].OrderCount);
        Assert.IsTrue(result.Days.Skip(1).Take(5).All(day => day.OrderCount == 0));
        var sameWeek = await ReportAsync(client, "date=2025-12-29&period=week");
        CollectionAssert.AreEqual(result.Days, sameWeek.Days);
    }

    [TestMethod]
    public async Task HistoricalDaylightSavingMidnightIsHandled()
    {
        await SeedAsync("2018-11-04T02:59:59.999999Z");
        await SeedAsync("2018-11-04T03:00:00Z");
        await SeedAsync("2018-11-05T01:59:59.999999Z");
        await SeedAsync("2018-11-05T02:00:00Z");
        using var client = await LoginAsync(Admin);
        Assert.AreEqual(2L, (await ReportAsync(client, "date=2018-11-04")).OrderCount);
    }

    [TestMethod]
    public async Task HistoricalPricesAndAllDeliveryStatesRemainInSalesWithoutMutation()
    {
        var order = await SeedAsync("2026-11-11T12:00:00Z");
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        await db.Products.ExecuteUpdateAsync(set => set.SetProperty(value => value.Price, 30m).SetProperty(value => value.IsActive, false));
        using var client = await LoginAsync(Admin);
        foreach (var state in Enum.GetValues<OrderStatus>())
        {
            // Preparacao de estado apenas para conferir que etapa de entrega nao muda a venda aprovada.
            await db.Orders.ExecuteUpdateAsync(set => set.SetProperty(value => value.Status, state));
            var result = await ReportAsync(client, "date=2026-11-11");
            Assert.AreEqual(23.80m, result.Total); Assert.AreEqual(4.20m, result.Discount); Assert.AreEqual(1L, result.OrderCount);
            Assert.AreEqual(state, await db.Orders.Select(value => value.Status).SingleAsync());
        }
        Assert.AreEqual(10, await db.Products.Select(value => value.Stock).SingleAsync());
        Assert.AreEqual(1, await db.OrderStatusHistory.CountAsync()); Assert.AreEqual(0, await db.CartItems.CountAsync());
        Assert.AreEqual(order.Id, await db.Orders.Select(value => value.Id).SingleAsync());
    }

    [TestMethod]
    public async Task ItemCountsUseLongAndMoneyKeepsCentPrecision()
    {
        await SeedAsync("2026-11-11T12:00:00Z", int.MaxValue, .02m, .01m);
        await SeedAsync("2026-11-11T13:00:00Z", int.MaxValue, .02m, .01m);
        using var client = await LoginAsync(Admin);
        var result = await ReportAsync(client, "date=2026-11-11");
        Assert.AreEqual(4294967294L, result.ItemCount);
        Assert.AreEqual(42949672.94m, result.Total);
        Assert.AreEqual(result.Subtotal - result.Discount, result.Total);
    }

    [TestMethod]
    [DataRow("visitor", 401)]
    [DataRow("customer", 403)]
    public async Task ReportRequiresAdministrator(string actor, int status)
    {
        using var client = actor == "visitor" ? app.CreateBrowserClient() : await LoginAsync(Customer);
        Assert.AreEqual((HttpStatusCode)status, (await client.GetAsync("/api/admin/reports/sales?date=2026-11-11")).StatusCode);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("date=invalid")]
    [DataRow("date=2026-02-29")]
    [DataRow("date=11/11/2026")]
    [DataRow("date=0001-01-01")]
    [DataRow("date=9999-12-31")]
    [DataRow("date=2026-11-11&period=month")]
    [DataRow("date=2026-11-11&period=")]
    public async Task InvalidQueryReturnsValidationProblem(string query)
    {
        using var client = await LoginAsync(Admin);
        var response = await client.GetAsync("/api/admin/reports/sales?" + query);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.IsNotEmpty((await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors);
    }

    private static async Task VerifyExportAsync(HttpClient client, string query)
    {
        var report = await ReportAsync(client, query);
        using var response = await client.GetAsync("/api/admin/reports/sales/export.xlsx?" + query);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(response.Headers.CacheControl!.NoStore);
        Assert.AreEqual("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType!.MediaType);
        var disposition = response.Content.Headers.ContentDisposition!;
        Assert.AreEqual("attachment", disposition.DispositionType);
        var period = report.Period == "week" ? "semanal" : "diario";
        Assert.AreEqual(FormattableString.Invariant($"vendas-simuladas-{period}-{report.StartDate:yyyy-MM-dd}-{report.EndDate:yyyy-MM-dd}.xlsx"), disposition.FileNameStar);
        using var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var document = SpreadsheetDocument.Open(stream, false);
        var errors = new OpenXmlValidator().Validate(document).ToArray();
        Assert.IsEmpty(errors, string.Join("; ", errors.Select(error => error.Description)));
        var workbook = document.WorkbookPart!;
        var sheet = workbook.Workbook!.GetFirstChild<Sheets>()!.Elements<Sheet>().Single();
        Assert.AreEqual("Vendas simuladas", sheet.Name!.Value);
        var worksheet = ((WorksheetPart)workbook.GetPartById(sheet.Id!)).Worksheet!;
        var rows = worksheet.GetFirstChild<SheetData>()!.Elements<Row>().ToArray();
        Assert.HasCount(11 + report.Days.Length, rows);
        string Value(string reference) => worksheet.Descendants<Cell>().Single(cell => cell.CellReference == reference).InnerText;
        Assert.Contains("SIMULAÇÃO", Value("A2"));
        Assert.AreEqual(report.Period == "week" ? "Semanal" : "Diário", Value("B3"));
        Assert.AreEqual(report.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Value("B4"));
        Assert.AreEqual(report.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Value("B5"));
        Assert.AreEqual(report.TimeZone, Value("B6")); Assert.AreEqual(report.Currency, Value("B7"));
        Assert.AreEqual(report.IsEmpty ? "Sem vendas simuladas aprovadas" : "Vendas simuladas aprovadas", Value("B8"));
        CollectionAssert.AreEqual(new[] { "Data (ISO)", "Pedidos", "Cupcakes", "Subtotal (BRL)", "Desconto (BRL)", "Total (BRL)" },
            rows[9].Elements<Cell>().Select(cell => cell.InnerText).ToArray());
        void AssertNumbers(Row row, long orders, long items, decimal subtotal, decimal discount, decimal total)
        {
            var cells = row.Elements<Cell>().Skip(1).ToArray();
            Assert.HasCount(5, cells);
            Assert.IsTrue(cells.All(cell => cell.DataType!.Value == CellValues.Number));
            CollectionAssert.AreEqual(new decimal[] { orders, items, subtotal, discount, total },
                cells.Select(cell => decimal.Parse(cell.CellValue!.Text, CultureInfo.InvariantCulture)).ToArray());
            Assert.IsTrue(cells.Skip(2).All(cell => cell.StyleIndex!.Value is 2 or 3));
        }
        for (var i = 0; i < report.Days.Length; i++)
        {
            var day = report.Days[i]; var row = rows[i + 10];
            Assert.AreEqual(day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), row.Elements<Cell>().First().InnerText);
            AssertNumbers(row, day.OrderCount, day.ItemCount, day.Subtotal, day.Discount, day.Total);
        }
        Assert.AreEqual("TOTAL", rows[^1].Elements<Cell>().First().InnerText);
        AssertNumbers(rows[^1], report.OrderCount, report.ItemCount, report.Subtotal, report.Discount, report.Total);
        Assert.IsFalse(worksheet.Descendants<CellFormula>().Any());
        Assert.IsFalse(workbook.ExternalRelationships.Any());
        Assert.IsNull(workbook.VbaProjectPart);
    }

    [TestMethod]
    [DataRow("day")]
    [DataRow("week")]
    public async Task XlsxEmptyPeriodIsValidWithZeroDaysAndTotals(string period)
    {
        using var client = await LoginAsync(Admin);
        await VerifyExportAsync(client, "date=2026-11-11&period=" + period);
    }

    [TestMethod]
    [DataRow("day")]
    [DataRow("week")]
    public async Task XlsxMatchesJsonUsingLocalDatesAndHistoricalPrices(string period)
    {
        await SeedAsync("2026-11-09T02:59:59Z"); // Domingo local: fora da semana.
        await SeedAsync("2026-11-09T03:00:00Z");
        await SeedAsync("2026-11-11T02:59:59Z"); // Dia anterior em São Paulo.
        await SeedAsync("2026-11-11T03:00:00Z");
        await SeedAsync("2026-11-12T02:59:59Z", 1, promotion: null);
        await SeedAsync("2026-11-16T03:00:00Z"); // Segunda seguinte: fora da semana.
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        await db.Products.ExecuteUpdateAsync(set => set.SetProperty(product => product.Price, 30m).SetProperty(product => product.IsActive, false));
        using var client = await LoginAsync(Admin);
        await VerifyExportAsync(client, "date=2026-11-11&period=" + period);
        Assert.AreEqual(6, await db.Orders.CountAsync());
        Assert.AreEqual(6, await db.OrderStatusHistory.CountAsync());
        Assert.IsTrue(await db.Products.AllAsync(product => product.Price == 30m && !product.IsActive));
    }

    [TestMethod]
    public async Task XlsxPreservesLargeQuantitiesAndCentValues()
    {
        await SeedAsync("2026-11-11T12:00:00Z", int.MaxValue, .02m, .01m);
        await SeedAsync("2026-11-11T13:00:00Z", int.MaxValue, .02m, .01m);
        using var client = await LoginAsync(Admin);
        await VerifyExportAsync(client, "date=2026-11-11");
    }

    [TestMethod]
    [DataRow("visitor", 401)]
    [DataRow("customer", 403)]
    public async Task XlsxRequiresAdministrator(string actor, int status)
    {
        using var client = actor == "visitor" ? app.CreateBrowserClient() : await LoginAsync(Customer);
        var response = await client.GetAsync("/api/admin/reports/sales/export.xlsx?date=2026-11-11");
        Assert.AreEqual((HttpStatusCode)status, response.StatusCode);
        Assert.IsNull(response.Content.Headers.ContentDisposition);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("date=2026-02-29")]
    [DataRow("date=0001-01-01")]
    [DataRow("date=2026-11-11&period=month")]
    public async Task XlsxInvalidQueryReturnsProblemInsteadOfAttachment(string query)
    {
        using var client = await LoginAsync(Admin);
        var response = await client.GetAsync("/api/admin/reports/sales/export.xlsx?" + query);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.IsNotEmpty((await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors);
        Assert.IsNull(response.Content.Headers.ContentDisposition);
    }

    private static async Task VerifyPdfAsync(HttpClient client, string query)
    {
        var report = await ReportAsync(client, query);
        using var response = await client.GetAsync("/api/admin/reports/sales/export.pdf?" + query);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(response.Headers.CacheControl!.NoStore);
        Assert.AreEqual("application/pdf", response.Content.Headers.ContentType!.MediaType);
        var period = report.Period == "week" ? "semanal" : "diario";
        Assert.AreEqual("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.AreEqual(FormattableString.Invariant($"vendas-simuladas-{period}-{report.StartDate:yyyy-MM-dd}-{report.EndDate:yyyy-MM-dd}.pdf"),
            response.Content.Headers.ContentDisposition.FileNameStar);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.AreEqual("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
        using var document = UglyToad.PdfPig.PdfDocument.Open(bytes);
        Assert.AreEqual(1, document.NumberOfPages);
        var page = document.GetPage(1);
        Assert.IsGreaterThan(page.Height, page.Width);
        Assert.Contains("Relatorio de vendas simuladas", page.Text);
        Assert.Contains("SIMULACAO - sem transacoes financeiras reais", page.Text);
        Assert.Contains(report.TimeZone, page.Text); Assert.Contains("Moeda: BRL", page.Text);
        var culture = CultureInfo.GetCultureInfo("pt-BR");
        string Date(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        Assert.Contains($"{Date(report.StartDate)} a {Date(report.EndDate)}", page.Text);
        Assert.Contains(report.Period == "week" ? "Periodo: Semanal" : "Periodo: Diario", page.Text);
        Assert.Contains(report.IsEmpty ? "Sem vendas simuladas aprovadas neste periodo." : "Somente pagamentos simulados aprovados", page.Text);
        double[] edges = [36, 161, 276, 401, 536, 671, 806];
        void Row(double top, string[] expected)
        {
            for (var column = 0; column < expected.Length; column++)
            {
                var letters = page.Letters.Where(letter => Math.Abs(letter.StartBaseLine.Y - (page.Height - top)) < .1
                    && letter.StartBaseLine.X >= edges[column] && letter.StartBaseLine.X < edges[column + 1]).ToArray();
                Assert.AreEqual(expected[column].Replace(" ", ""), string.Concat(letters.Select(letter => letter.Value)).Replace(" ", ""));
                Assert.IsTrue(letters.All(letter => letter.BoundingBox.Right < edges[column + 1]), "Celula nao pode invadir a coluna seguinte.");
            }
        }
        Row(211, ["Data", "Pedidos", "Cupcakes", "Subtotal (BRL)", "Desconto (BRL)", "Total (BRL)"]);
        for (var i = 0; i < report.Days.Length; i++)
        {
            var day = report.Days[i];
            Row(241 + i * 30, [Date(day.Date), day.OrderCount.ToString("N0", culture), day.ItemCount.ToString("N0", culture),
                day.Subtotal.ToString("N2", culture), day.Discount.ToString("N2", culture), day.Total.ToString("N2", culture)]);
        }
        Row(241 + report.Days.Length * 30, ["TOTAL", report.OrderCount.ToString("N0", culture), report.ItemCount.ToString("N0", culture),
            report.Subtotal.ToString("N2", culture), report.Discount.ToString("N2", culture), report.Total.ToString("N2", culture)]);
        Assert.IsTrue(page.Letters.All(letter => letter.BoundingBox.Left >= 30 && letter.BoundingBox.Right < page.Width - 30
            && letter.BoundingBox.Bottom > 20 && letter.BoundingBox.Top < page.Height - 20), "Texto deve permanecer dentro das margens.");
    }

    [TestMethod]
    [DataRow("day")]
    [DataRow("week")]
    public async Task PdfEmptyPeriodHasValidPageAndZeroTotals(string period)
    {
        using var client = await LoginAsync(Admin);
        await VerifyPdfAsync(client, "date=2026-11-11&period=" + period);
    }

    [TestMethod]
    [DataRow("day")]
    [DataRow("week")]
    public async Task PdfMatchesJsonWithHistoricalPricesAndLocalDates(string period)
    {
        await SeedAsync("2026-11-11T02:59:59Z");
        await SeedAsync("2026-11-11T03:00:00Z");
        await SeedAsync("2026-11-12T02:59:59Z", 1, promotion: null);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        await db.Products.ExecuteUpdateAsync(set => set.SetProperty(product => product.Price, 30m).SetProperty(product => product.IsActive, false));
        using var client = await LoginAsync(Admin);
        await VerifyPdfAsync(client, "date=2026-11-11&period=" + period);
        Assert.AreEqual(3, await db.Orders.CountAsync()); Assert.AreEqual(3, await db.OrderStatusHistory.CountAsync());
        Assert.IsTrue(await db.Products.AllAsync(product => product.Stock == 10 && !product.IsActive));
    }

    [TestMethod]
    public async Task PdfLargeCountsAndCentValuesFitTheirColumns()
    {
        await SeedAsync("2026-11-11T12:00:00Z", int.MaxValue, .02m, .01m);
        await SeedAsync("2026-11-11T13:00:00Z", int.MaxValue, .02m, .01m);
        using var client = await LoginAsync(Admin);
        await VerifyPdfAsync(client, "date=2026-11-11");
    }

    [TestMethod]
    [DataRow("visitor", 401)]
    [DataRow("customer", 403)]
    public async Task PdfRequiresAdministrator(string actor, int status)
    {
        using var client = actor == "visitor" ? app.CreateBrowserClient() : await LoginAsync(Customer);
        Assert.AreEqual((HttpStatusCode)status, (await client.GetAsync("/api/admin/reports/sales/export.pdf?date=2026-11-11")).StatusCode);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("date=2026-02-29")]
    [DataRow("date=9999-12-31")]
    [DataRow("date=2026-11-11&period=month")]
    public async Task PdfInvalidQueryReturnsProblemInsteadOfAttachment(string query)
    {
        using var client = await LoginAsync(Admin);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/pdf");
        var response = await client.GetAsync("/api/admin/reports/sales/export.pdf?" + query);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.IsNotEmpty((await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors);
        Assert.IsNull(response.Content.Headers.ContentDisposition);
    }

    [TestMethod]
    public async Task DeclinedCheckoutDoesNotCountAsSale()
    {
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        var product = new Product("Teste", "Chocolate", "Ficticio", Product.DemoImagePath, 12.50m, null, 4);
        db.Products.Add(product); await db.SaveChangesAsync();
        using var customer = await LoginAsync(Customer);
        var token = await customer.GetFromJsonAsync<AntiforgeryResponse>("/api/auth/antiforgery");
        customer.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token!.RequestToken);
        Assert.AreEqual(HttpStatusCode.OK, (await customer.PostAsJsonAsync("/api/cart/items", new { productId = product.Id, quantity = 1 })).StatusCode);
        var response = await customer.PostAsJsonAsync("/api/checkout/confirm", new {
            confirmationId = Guid.NewGuid(), address = new { recipient = "Pessoa", street = "Rua", number = "1", neighborhood = "Bairro", city = "Cidade", state = "SP", postalCode = "01234567" },
            paymentMethod = "Pix", simulation = "Recusar", expectedItems = new[] { new { productId = product.Id, quantity = 1, price = 12.50m, effectivePrice = 12.50m } }
        });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("declined", (await response.Content.ReadFromJsonAsync<CheckoutConfirmationResponse>())!.Code);
        using var admin = await LoginAsync(Admin);
        Assert.IsTrue((await ReportAsync(admin, "date=2026-11-11")).IsEmpty);
        Assert.AreEqual(0, await db.Orders.CountAsync()); Assert.AreEqual(4, await db.Products.Select(value => value.Stock).SingleAsync());
    }
}
