using Cupcakes.Api.Contracts;
using Cupcakes.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Cupcakes.Api.Services;

public sealed class AdminOrderQueryService(CupcakesDbContext db)
{
    public async Task<AdminOrderSummaryResponse[]> ListAsync(CancellationToken ct) =>
        await (from order in db.Orders.AsNoTracking()
               join user in db.Users on order.UserId equals user.Id
               orderby order.CreatedAt descending, order.Id descending
               select new AdminOrderSummaryResponse(user.Email!,
                   new OrderSummaryResponse(order.Id, order.CreatedAt, order.Status.ToString(),
                       order.PaymentMethod.ToString(), order.PaymentResult,
                       order.Items.Sum(item => (long)item.Quantity), order.Subtotal, order.Discount, order.Total, true)))
            .ToArrayAsync(ct);

    public async Task<AdminOrderDetailResponse?> GetAsync(Guid orderId, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(order => order.Address)
            .Include(order => order.Items).Include(order => order.StatusHistory).AsSingleQuery()
            .SingleOrDefaultAsync(order => order.Id == orderId, ct);
        if (order is null) return null;
        var email = await db.Users.Where(user => user.Id == order.UserId).Select(user => user.Email!).SingleAsync(ct);
        return new AdminOrderDetailResponse(email, OrderResponseMapper.MapDetail(order));
    }
}
