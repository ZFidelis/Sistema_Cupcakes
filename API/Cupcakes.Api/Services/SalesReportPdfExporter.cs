using System.Globalization;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Cupcakes.Api.Contracts;

namespace Cupcakes.Api.Services;

public static class SalesReportPdfExporter
{
    public const string ContentType = "application/pdf";
    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR");

    public static byte[] Export(SalesReportResponse report)
    {
        var document = new PdfDocumentBuilder();
        var regular = document.AddStandard14Font(Standard14Font.Helvetica);
        var bold = document.AddStandard14Font(Standard14Font.HelveticaBold);
        var page = document.AddPage(PageSize.A4, isPortrait: false);
        var height = page.PageSize.Height;
        void Text(string text, double x, double top, double size = 11, bool strong = false) =>
            page.AddText(text, size, new PdfPoint(x, height - top), strong ? bold : regular);
        // Fontes PDF padrao dispensam arquivos externos; rotulos usam caracteres ASCII.
        page.SetTextAndFillColor(69, 47, 43);
        Text("Cupcakes gourmet", 36, 38, 11, true);
        Text("Relatorio de vendas simuladas", 36, 70, 22, true);
        Text("SIMULACAO - sem transacoes financeiras reais", 36, 96, 11, true);
        var period = report.Period == "week" ? "Semanal" : "Diario";
        Text($"Periodo: {period} | {Date(report.StartDate)} a {Date(report.EndDate)}", 36, 128);
        Text($"Fuso: {report.TimeZone} | Moeda: {report.Currency}", 36, 149);
        Text(report.IsEmpty ? "Sem vendas simuladas aprovadas neste periodo." : "Somente pagamentos simulados aprovados; valores historicos da compra.", 36, 177);

        double[] edges = [36, 161, 276, 401, 536, 671, 806];
        var headers = new[] { "Data", "Pedidos", "Cupcakes", "Subtotal (BRL)", "Desconto (BRL)", "Total (BRL)" };
        Row(headers, 211, true);
        var y = 241d;
        foreach (var day in report.Days)
        {
            Row([Date(day.Date), Count(day.OrderCount), Count(day.ItemCount), Money(day.Subtotal), Money(day.Discount), Money(day.Total)], y);
            y += 30;
        }
        Row(["TOTAL", Count(report.OrderCount), Count(report.ItemCount), Money(report.Subtotal), Money(report.Discount), Money(report.Total)], y, true);
        Text("Dias sem vendas incluidos. Relatorio gerado com os dados atuais da loja.", 36, y + 38, 10);
        Text("Pagina 1 de 1", 36, 565, 9);
        return document.Build();

        void Row(string[] values, double top, bool strong = false)
        {
            page.SetStrokeColor(225, 214, 210);
            page.DrawLine(new PdfPoint(edges[0], height - top - 10), new PdfPoint(edges[^1], height - top - 10), .5);
            for (var column = 0; column < values.Length; column++)
            {
                var size = 11d;
                while (size > 6 && page.MeasureText(values[column], size, new PdfPoint(0, 0), strong ? bold : regular)
                    .Max(letter => letter.BoundingBox.Right) > edges[column + 1] - edges[column] - 16) size -= .5;
                Text(values[column], edges[column] + 6, top, size, strong);
            }
        }
    }

    private static string Date(DateOnly value) => value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    private static string Count(long value) => value.ToString("N0", Portuguese);
    private static string Money(decimal value) => value.ToString("N2", Portuguese);
}
