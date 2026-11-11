using Cupcakes.Api.Contracts;
using Cupcakes.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Cupcakes.Api.Services;

public sealed class SalesReportService(CupcakesDbContext db)
{
    private const string ZoneId = "America/Sao_Paulo";
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(ZoneId);

    public async Task<SalesReportResponse> GetAsync(DateOnly date, bool weekly, CancellationToken ct)
    {
        var start = weekly ? date.AddDays(-(((int)date.DayOfWeek + 6) % 7)) : date;
        var dayCount = weekly ? 7 : 1;
        var endExclusive = start.AddDays(dayCount);
        var fromUtc = StartOfDayUtc(start);
        var toUtc = StartOfDayUtc(endExclusive);
        var orders = await db.Orders.AsNoTracking()
            .Where(order => order.PaymentResult == "Aprovado" && order.CreatedAt >= fromUtc && order.CreatedAt < toUtc)
            .Select(order => new { order.CreatedAt, order.Subtotal, order.Discount, order.Total,
                ItemCount = order.Items.Sum(item => (long)item.Quantity) })
            .ToArrayAsync(ct);
        var groups = orders.GroupBy(order => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(order.CreatedAt, Zone).DateTime))
            .ToDictionary(group => group.Key, group => new SalesReportDay(group.Key, group.LongCount(),
                group.Sum(order => order.ItemCount), group.Sum(order => order.Subtotal),
                group.Sum(order => order.Discount), group.Sum(order => order.Total)));
        var days = Enumerable.Range(0, dayCount).Select(offset => start.AddDays(offset))
            .Select(day => groups.GetValueOrDefault(day) ?? new SalesReportDay(day, 0, 0, 0, 0, 0)).ToArray();
        return new(weekly ? "week" : "day", start, endExclusive.AddDays(-1), ZoneId, "BRL",
            days.Sum(day => day.OrderCount), days.Sum(day => day.ItemCount), days.Sum(day => day.Subtotal),
            days.Sum(day => day.Discount), days.Sum(day => day.Total), true, days);
    }

    private static DateTimeOffset StartOfDayUtc(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // Em datas historicas com inicio do horario de verao, meia-noite pode nao existir.
        while (Zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        if (Zone.IsAmbiguousTime(local))
            return new DateTimeOffset(local, Zone.GetAmbiguousTimeOffsets(local).Max()).ToUniversalTime();
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, Zone));
    }
}
