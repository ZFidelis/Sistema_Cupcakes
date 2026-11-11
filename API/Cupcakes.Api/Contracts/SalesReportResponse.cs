namespace Cupcakes.Api.Contracts;

public sealed record SalesReportDay(DateOnly Date, long OrderCount, long ItemCount,
    decimal Subtotal, decimal Discount, decimal Total);

public sealed record SalesReportResponse(string Period, DateOnly StartDate, DateOnly EndDate,
    string TimeZone, string Currency, long OrderCount, long ItemCount, decimal Subtotal,
    decimal Discount, decimal Total, bool IsSimulation, SalesReportDay[] Days)
{
    public bool IsEmpty => OrderCount == 0;
}
