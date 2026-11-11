using Cupcakes.Api.Contracts;
using Cupcakes.Api.Data;
using Cupcakes.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cupcakes.Api.Services;

public sealed class OrderStatusService(CupcakesDbContext db)
{
    public async Task<OrderStatusUpdateResult> UpdateAsync(Guid orderId, OrderStatus next, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await db.Orders.FromSqlInterpolated(
            $"SELECT * FROM \"Orders\" WHERE \"Id\" = {orderId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (order is null) return new(404);
        await db.Entry(order).Collection(value => value.StatusHistory).LoadAsync(ct);
        var previous = order.Status;
        if (!order.TryAdvanceStatus(next)) return new(409);
        if (order.Status != previous)
            db.OrderStatusHistory.Add(order.StatusHistory.Single(entry => entry.Status == next));
        await db.SaveChangesAsync(ct);
        var history = order.StatusHistory.OrderBy(entry => entry.ChangedAt).ThenBy(entry => entry.Id)
            .Select(entry => new OrderStatusResponse(entry.Status.ToString(), entry.ChangedAt)).ToArray();
        await transaction.CommitAsync(ct);
        return new(200, new OrderStatusUpdateResponse(order.Id, order.Status.ToString(), history));
    }
}

public sealed record OrderStatusUpdateResult(int StatusCode, OrderStatusUpdateResponse? Response = null);
