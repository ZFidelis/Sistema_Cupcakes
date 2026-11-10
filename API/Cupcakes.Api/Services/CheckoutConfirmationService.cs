using Cupcakes.Api.Contracts;
using Cupcakes.Api.Data;
using Cupcakes.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cupcakes.Api.Services;

public sealed class CheckoutConfirmationService(CupcakesDbContext db, CheckoutPreviewService previews,
    SimulatedPaymentService payments)
{
    public async Task<CheckoutConfirmationResult> ConfirmAsync(string userId, CheckoutConfirmationRequest request, CancellationToken ct)
    {
        var address = request.Address.ToDomain();
        var method = Enum.Parse<SimulatedPaymentMethod>(request.PaymentMethod);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Mesmo bloqueio usado pelo carrinho; inclui criacao e limpeza dos seus itens.
        var user = await db.Users.FromSqlInterpolated(
            $"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {userId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        if (user is null) return Result(401, "session_expired", "Entre novamente para confirmar a compra.");

        var existing = await db.Orders.AsNoTracking().Include(order => order.Items).Include(order => order.Address)
            .SingleOrDefaultAsync(order => order.UserId == userId && order.ConfirmationId == request.ConfirmationId, ct);
        if (existing is not null)
        {
            if (!MatchesOrder(existing, request, address, method))
                return Result(409, "confirmation_reused", "Este identificador ja pertence a outro conteudo de compra. Consulte o pedido original e use um novo identificador para outra compra.");
            return new(200, new("approved", "Compra simulada ja confirmada. O pedido original foi recuperado, sem nova baixa de estoque.", true, Map(existing)));
        }

        var productIds = await db.CartItems.Where(item => db.Carts.Any(cart => cart.Id == item.CartId && cart.UserId == userId))
            .Select(item => item.ProductId).ToArrayAsync(ct);
        if (productIds.Length > 0)
        {
            // Ordem fixa evita bloqueios circulares entre compras com produtos em comum.
            _ = await db.Products.FromSqlInterpolated(
                $"SELECT * FROM \"Products\" WHERE \"Id\" = ANY ({productIds}) ORDER BY \"Id\" FOR UPDATE")
                .AsNoTracking().ToArrayAsync(ct);
        }

        var preview = await previews.PreviewAsync(userId, address, ct);
        if (!preview.CanProceed)
            return new(409, new("checkout_unavailable", "Revise o carrinho antes de confirmar.", true, Preview: preview));
        if (!MatchesCart(preview.Cart, request.ExpectedItems))
            return new(409, new("checkout_changed", "Produtos, quantidades ou precos mudaram. Confira o resumo atualizado e confirme novamente.", true, Preview: preview));

        if (!payments.Approve(method, request.Simulation))
            return Result(200, "declined", "Pagamento recusado na simulacao. Nenhuma cobranca ou pedido foi realizado; seu carrinho foi preservado.");

        var order = new Order(userId, method, address, preview.Cart.Items.Select(item =>
            new OrderItem(item.ProductId, item.Name, item.Price, item.EffectivePrice, item.Quantity)), request.ConfirmationId);

        foreach (var item in preview.Cart.Items)
        {
            var updated = await db.Products.Where(product => product.Id == item.ProductId
                    && product.IsActive && product.Stock >= item.Quantity)
                .ExecuteUpdateAsync(update => update.SetProperty(product => product.Stock, product => product.Stock - item.Quantity), ct);
            if (updated != 1) throw new InvalidOperationException("O estoque nao pode ser atualizado de forma consistente.");
        }

        db.Orders.Add(order);
        await db.CartItems.Where(item => db.Carts.Any(cart => cart.Id == item.CartId && cart.UserId == userId)).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(201, new("approved", "Pedido confirmado com pagamento inteiramente simulado. Nenhuma transacao financeira real foi processada.", true, Map(order)));
    }

    private static bool MatchesCart(CartResponse cart, IReadOnlyCollection<CheckoutExpectedItem> expected) =>
        cart.Items.Length == expected.Count && cart.Items.All(item => expected.Any(value =>
            value.ProductId == item.ProductId && value.Quantity == item.Quantity
            && value.Price == item.Price && value.EffectivePrice == item.EffectivePrice));

    private static bool MatchesOrder(Order order, CheckoutConfirmationRequest request, OrderAddress address, SimulatedPaymentMethod method) =>
        request.Simulation == "Aprovar" && order.PaymentMethod == method
        && order.Items.Count == request.ExpectedItems.Count && order.Items.All(item => request.ExpectedItems.Any(value =>
            value.ProductId == item.ProductId && value.Quantity == item.Quantity
            && value.Price == item.OriginalUnitPrice && value.EffectivePrice == item.AppliedUnitPrice))
        && order.Address.Recipient == address.Recipient && order.Address.Street == address.Street
        && order.Address.Number == address.Number && order.Address.Neighborhood == address.Neighborhood
        && order.Address.City == address.City && order.Address.State == address.State
        && order.Address.PostalCode == address.PostalCode && order.Address.Complement == address.Complement;

    private static OrderConfirmationResponse Map(Order order) => new(order.Id, order.ConfirmationId,
        order.CreatedAt, order.Status.ToString(), order.PaymentMethod.ToString(), order.PaymentResult,
        new(order.Address.Recipient, order.Address.Street, order.Address.Number, order.Address.Neighborhood,
            order.Address.City, order.Address.State, order.Address.PostalCode, order.Address.Complement),
        order.Items.OrderBy(item => item.ProductName).ThenBy(item => item.ProductId).Select(item =>
            new OrderConfirmationItem(item.ProductId, item.ProductName, item.Quantity, item.OriginalUnitPrice,
                item.AppliedUnitPrice, item.Subtotal, item.Discount, item.Total)).ToArray(),
        order.Subtotal, order.Discount, order.Total);

    private static CheckoutConfirmationResult Result(int status, string code, string message) =>
        new(status, new(code, message, true));
}

public sealed record CheckoutConfirmationResult(int StatusCode, CheckoutConfirmationResponse Response);
