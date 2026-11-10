using System.Globalization;
using Cupcakes.Api.Data;
using Cupcakes.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Cupcakes.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class OrderModelTests
{
    private static TestApplication app = null!;

    [ClassInitialize]
    public static async Task Initialize(TestContext _)
    {
        app = await TestApplication.CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        Assert.IsFalse(db.Database.HasPendingModelChanges());
        Assert.AreEqual(0, await db.Orders.CountAsync(), "Nao criar pedidos demonstrativos automaticamente.");
    }

    [ClassCleanup]
    public static async Task Cleanup()
    {
        if (app is not null) await app.DisposeAsync();
    }

    private static OrderAddress Address() => new(" Pessoa Ficticia ", " Rua de Teste ", "s/n",
        " Bairro ", " Cidade ", "sp", "01234567", " Casa ");

    private static OrderItem Item(Guid? productId = null, int quantity = 3) =>
        new(productId ?? Guid.NewGuid(), " Morango ", 14m, 11.90m, quantity);

    private static async Task<Order> PersistAsync()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        var email = $"order-model-{Guid.NewGuid():N}@example.invalid";
        var user = new IdentityUser { Email = email, UserName = email };
        Assert.IsTrue((await scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>().CreateAsync(user)).Succeeded);
        var product = new Product("Morango", "Morango", "Produto ficticio", Product.DemoImagePath, 14m, 11.90m, 10);
        db.Products.Add(product);
        var order = new Order(user.Id, SimulatedPaymentMethod.Pix, Address(), [Item(product.Id)]);
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    [TestMethod]
    public void OrderComputesExactTotalsAndCreatesInitialStatusWithUtcTimestamp()
    {
        var before = DateTimeOffset.UtcNow;
        var order = new Order("owner", SimulatedPaymentMethod.Cartao, Address(),
            [Item(), new OrderItem(Guid.NewGuid(), "Chocolate", 12.50m, 12.50m, 2)]);
        Assert.AreEqual(67m, order.Subtotal);
        Assert.AreEqual(6.30m, order.Discount);
        Assert.AreEqual(60.70m, order.Total);
        Assert.AreEqual("Aprovado", order.PaymentResult);
        Assert.AreEqual(OrderStatus.Recebido, order.Status);
        Assert.AreEqual(SimulatedPaymentMethod.Cartao, order.PaymentMethod);
        Assert.AreEqual(TimeSpan.Zero, order.CreatedAt.Offset);
        Assert.IsTrue(order.CreatedAt >= before && order.CreatedAt <= DateTimeOffset.UtcNow);
        Assert.HasCount(1, order.StatusHistory);
        var initial = order.StatusHistory.Single();
        Assert.AreEqual(order.Id, initial.OrderId);
        Assert.AreEqual(order.CreatedAt, initial.ChangedAt);
        Assert.AreEqual(OrderStatus.Recebido, initial.Status);
        Assert.IsTrue(order.Items.All(item => item.OrderId == order.Id));
    }

    [TestMethod]
    public void AddressTrimsTextNormalizesStateAndPreservesPostalCodeAndUnnumberedStreet()
    {
        var address = Address();
        Assert.AreEqual("Pessoa Ficticia", address.Recipient);
        Assert.AreEqual("Rua de Teste", address.Street);
        Assert.AreEqual("s/n", address.Number);
        Assert.AreEqual("SP", address.State);
        Assert.AreEqual("01234567", address.PostalCode);
        Assert.AreEqual("Casa", address.Complement);
        Assert.IsNull(new OrderAddress("Pessoa", "Rua", "1", "Bairro", "Cidade", "SP", "12345678", " ").Complement);
    }

    [TestMethod]
    public void ReusingInputsDoesNotShareOrReassignHistoricalObjects()
    {
        var address = Address();
        var item = Item();
        var first = new Order("owner", SimulatedPaymentMethod.Pix, address, [item]);
        var second = new Order("owner", SimulatedPaymentMethod.Cartao, address, [item]);
        Assert.AreNotSame(first.Address, second.Address);
        Assert.AreNotSame(first.Items.Single(), second.Items.Single());
        Assert.AreEqual(first.Id, first.Address.OrderId);
        Assert.AreEqual(first.Id, first.Items.Single().OrderId);
        Assert.AreEqual(second.Id, second.Items.Single().OrderId);
        Assert.AreEqual(Guid.Empty, address.OrderId);
        Assert.AreEqual(Guid.Empty, item.OrderId);
        Assert.Throws<NotSupportedException>(() => ((ICollection<OrderItem>)first.Items).Clear());
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
    public void AddressRejectsInvalidFields(string field)
    {
        Assert.Throws<ArgumentException>(() => new OrderAddress(
            field == "recipient" ? " " : "Pessoa",
            field == "street" ? new string('a', 151) : "Rua",
            field == "number" ? " " : "1",
            field == "neighborhood" ? new string('a', 81) : "Bairro",
            field == "city" ? " " : "Cidade",
            field == "state" ? "S1" : "SP",
            field == "postalCode" ? "12345-678" : "12345678",
            field == "complement" ? new string('a', 101) : null));
    }

    [TestMethod]
    [DataRow("0", "0")]
    [DataRow("-1", "1")]
    [DataRow("10", "11")]
    [DataRow("10", "0")]
    [DataRow("10.001", "9")]
    [DataRow("10", "9.001")]
    [DataRow("10000000000", "1")]
    public void ItemRejectsInvalidMonetaryValues(string original, string applied)
    {
        Assert.Throws<ArgumentException>(() => new OrderItem(Guid.NewGuid(), "Cupcake",
            decimal.Parse(original, CultureInfo.InvariantCulture), decimal.Parse(applied, CultureInfo.InvariantCulture), 1));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void ItemRejectsNonpositiveQuantity(int quantity) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Item(quantity: quantity));

    [TestMethod]
    public void OrderRejectsEmptyDuplicatedOrExcessivelyLargePurchaseAndInvalidIdentityOrMethod()
    {
        var item = Item();
        Assert.Throws<ArgumentException>(() => new Order(" ", SimulatedPaymentMethod.Pix, Address(), [item]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Order("owner", (SimulatedPaymentMethod)99, Address(), [item]));
        Assert.Throws<ArgumentException>(() => new Order("owner", SimulatedPaymentMethod.Pix, Address(), []));
        Assert.Throws<ArgumentException>(() => new Order("owner", SimulatedPaymentMethod.Pix, Address(), [item, item]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Order("owner", SimulatedPaymentMethod.Pix, Address(),
            [new OrderItem(Guid.NewGuid(), "Cupcake", Product.MaximumPrice, Product.MaximumPrice, 2)]));
        Assert.Throws<ArgumentException>(() => new OrderItem(Guid.Empty, "Cupcake", 1m, 1m, 1));
        Assert.Throws<ArgumentException>(() => new OrderItem(Guid.NewGuid(), " ", 1m, 1m, 1));
    }

    [TestMethod]
    public async Task RoundTripPreservesHistoricalPricesNameAddressAndInitialHistory()
    {
        var original = await PersistAsync();
        var productId = original.Items.Single().ProductId;
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        await db.Products.Where(x => x.Id == productId).ExecuteUpdateAsync(set => set
            .SetProperty(x => x.Name, "Novo nome").SetProperty(x => x.Price, 20m)
            .SetProperty(x => x.PromotionalPrice, (decimal?)null).SetProperty(x => x.IsActive, false));
        var stored = await db.Orders.AsNoTracking().Include(x => x.Items).Include(x => x.Address)
            .Include(x => x.StatusHistory).SingleAsync(x => x.Id == original.Id);
        Assert.AreEqual(42m, stored.Subtotal);
        Assert.AreEqual(6.30m, stored.Discount);
        Assert.AreEqual(35.70m, stored.Total);
        Assert.AreEqual("Morango", stored.Items.Single().ProductName);
        Assert.AreEqual(14m, stored.Items.Single().OriginalUnitPrice);
        Assert.AreEqual(11.90m, stored.Items.Single().AppliedUnitPrice);
        Assert.AreEqual("Pessoa Ficticia", stored.Address.Recipient);
        Assert.AreEqual("01234567", stored.Address.PostalCode);
        Assert.AreEqual(stored.Id, stored.Address.OrderId);
        Assert.AreEqual(original.CreatedAt.UtcDateTime.Ticks / 10, stored.CreatedAt.UtcDateTime.Ticks / 10);
        Assert.AreEqual(OrderStatus.Recebido, stored.StatusHistory.Single().Status);
        Assert.AreEqual(stored.CreatedAt, stored.StatusHistory.Single().ChangedAt);
        Assert.AreEqual(TimeSpan.Zero, stored.CreatedAt.Offset);
        Assert.AreEqual(10, await db.Products.Where(x => x.Id == productId).Select(x => x.Stock).SingleAsync(),
            "Persistir o modelo isoladamente nao deve simular a futura transacao de checkout.");
    }

    [TestMethod]
    [DataRow("total")]
    [DataRow("status")]
    [DataRow("method")]
    [DataRow("result")]
    [DataRow("quantity")]
    [DataRow("price")]
    [DataRow("state")]
    [DataRow("postal-code")]
    public async Task DatabaseRejectsInvalidStoredValues(string violation)
    {
        var order = await PersistAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        // SQL fixo por cenario; parametros preservam o isolamento de cada pedido de teste.
        FormattableString sql = violation switch
        {
            "total" => $"UPDATE \"Orders\" SET \"Total\" = 1 WHERE \"Id\" = {order.Id}",
            "status" => $"UPDATE \"Orders\" SET \"Status\" = 'Invalido' WHERE \"Id\" = {order.Id}",
            "method" => $"UPDATE \"Orders\" SET \"PaymentMethod\" = 'Real' WHERE \"Id\" = {order.Id}",
            "result" => $"UPDATE \"Orders\" SET \"PaymentResult\" = 'Recusado' WHERE \"Id\" = {order.Id}",
            "quantity" => $"UPDATE \"OrderItems\" SET \"Quantity\" = 0 WHERE \"OrderId\" = {order.Id}",
            "price" => $"UPDATE \"OrderItems\" SET \"AppliedUnitPrice\" = 20 WHERE \"OrderId\" = {order.Id}",
            "state" => $"UPDATE \"OrderAddresses\" SET \"State\" = 'S1' WHERE \"OrderId\" = {order.Id}",
            _ => $"UPDATE \"OrderAddresses\" SET \"PostalCode\" = '1234567x' WHERE \"OrderId\" = {order.Id}"
        };
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(sql));
        Assert.AreEqual(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [TestMethod]
    public async Task DatabasePreventsDeletingReferencedUserAndProductOrDuplicatingItemAndStatus()
    {
        var order = await PersistAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CupcakesDbContext>();
        var productId = order.Items.Single().ProductId;
        var userError = await Assert.ThrowsAsync<PostgresException>(() => db.Users.Where(x => x.Id == order.UserId).ExecuteDeleteAsync());
        Assert.AreEqual(PostgresErrorCodes.ForeignKeyViolation, userError.SqlState);
        var productError = await Assert.ThrowsAsync<PostgresException>(() => db.Products.Where(x => x.Id == productId).ExecuteDeleteAsync());
        Assert.AreEqual(PostgresErrorCodes.ForeignKeyViolation, productError.SqlState);
        var itemError = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"OrderItems\" (\"OrderId\", \"ProductId\", \"ProductName\", \"OriginalUnitPrice\", \"AppliedUnitPrice\", \"Quantity\") SELECT \"OrderId\", \"ProductId\", \"ProductName\", \"OriginalUnitPrice\", \"AppliedUnitPrice\", \"Quantity\" FROM \"OrderItems\" WHERE \"OrderId\" = {order.Id}"));
        Assert.AreEqual(PostgresErrorCodes.UniqueViolation, itemError.SqlState);
        var historyError = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"OrderStatusHistory\" (\"Id\", \"OrderId\", \"Status\", \"ChangedAt\") VALUES ({Guid.NewGuid()}, {order.Id}, 'Recebido', {DateTimeOffset.UtcNow})"));
        Assert.AreEqual(PostgresErrorCodes.UniqueViolation, historyError.SqlState);
    }
}
