using Cupcakes.Api.Contracts;
using Cupcakes.Api.Domain;

namespace Cupcakes.Api.Services;

internal static class OrderFilters
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static IQueryable<Order> Apply(IQueryable<Order> query, OrderFilterRequest filters)
    {
        if (filters.StartDate is { } start)
        {
            var from = StartOfDayUtc(start);
            query = query.Where(order => order.CreatedAt >= from);
        }
        if (filters.EndDate is { } end)
        {
            var until = StartOfDayUtc(end.AddDays(1));
            query = query.Where(order => order.CreatedAt < until);
        }
        if (!string.IsNullOrEmpty(filters.Status))
        {
            var status = Enum.Parse<OrderStatus>(filters.Status);
            query = query.Where(order => order.Status == status);
        }
        if (filters is AdminOrderFilterRequest { OrderId: { } id })
            query = query.Where(order => order.Id == id);
        return query;
    }

    private static DateTimeOffset StartOfDayUtc(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (Zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        if (Zone.IsAmbiguousTime(local)) return new DateTimeOffset(local, Zone.GetAmbiguousTimeOffsets(local).Max()).ToUniversalTime();
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, Zone));
    }
}
