using Cupcakes.Api.Contracts;
using Cupcakes.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Cupcakes.Api.Services;

public sealed class OrderQueryService(CupcakesDbContext db)
{
    public async Task<OrderSummaryResponse[]> ListAsync(string userId, CancellationToken ct) =>
        await db.Orders.AsNoTracking().Where(order => order.UserId == userId)
            .OrderByDescending(order => order.CreatedAt).ThenByDescending(order => order.Id)
            .Select(order => new OrderSummaryResponse(order.Id, order.CreatedAt, order.Status.ToString(),
                order.PaymentMethod.ToString(), order.PaymentResult,
                order.Items.Sum(item => (long)item.Quantity), order.Subtotal, order.Discount, order.Total, true))
            .ToArrayAsync(ct);

    public async Task<OrderDetailResponse?> GetAsync(string userId, Guid orderId, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(order => order.Address)
            .Include(order => order.Items).Include(order => order.StatusHistory).AsSingleQuery()
            .SingleOrDefaultAsync(order => order.Id == orderId && order.UserId == userId, ct);
        if (order is null) return null;

        return new OrderDetailResponse(OrderResponseMapper.Map(order),
            order.StatusHistory.OrderBy(entry => entry.ChangedAt).ThenBy(entry => entry.Id)
                .Select(entry => new OrderStatusResponse(entry.Status.ToString(), entry.ChangedAt)).ToArray(),
            true, "Pedido com pagamento inteiramente simulado. Nenhuma transacao financeira real foi processada.");
    }
}
