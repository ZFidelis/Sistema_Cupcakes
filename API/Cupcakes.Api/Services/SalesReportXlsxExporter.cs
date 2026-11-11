using System.Globalization;
using Cupcakes.Api.Contracts;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Cupcakes.Api.Services;

public static class SalesReportXlsxExporter
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static byte[] Export(SalesReportResponse report)
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new Workbook();
            var styles = workbook.AddNewPart<WorkbookStylesPart>();
            styles.Stylesheet = new Stylesheet(
                new Fonts(new Font(), new Font(new Bold())) { Count = 2 },
                new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }),
                    new Fill(new PatternFill { PatternType = PatternValues.Gray125 })) { Count = 2 },
                new Borders(new Border()) { Count = 1 },
                new CellStyleFormats(new CellFormat()) { Count = 1 },
                new CellFormats(new CellFormat(), new CellFormat { FontId = 1, ApplyFont = true },
                    new CellFormat { NumberFormatId = 4, ApplyNumberFormat = true },
                    new CellFormat { FontId = 1, ApplyFont = true, NumberFormatId = 4, ApplyNumberFormat = true }) { Count = 4 });

            var worksheet = workbook.AddNewPart<WorksheetPart>();
            var data = new SheetData();
            AddRow(data, Text("Relatório de vendas simuladas", 1));
            AddRow(data, Text("SIMULAÇÃO — sem transações financeiras reais"));
            AddRow(data, Text("Período"), Text(report.Period == "week" ? "Semanal" : "Diário"));
            // ISO como texto preserva datas civis e anos anteriores ao calendário numérico do Excel.
            AddRow(data, Text("Início"), Text(report.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            AddRow(data, Text("Fim"), Text(report.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            AddRow(data, Text("Fuso horário"), Text(report.TimeZone));
            AddRow(data, Text("Moeda"), Text(report.Currency));
            AddRow(data, Text("Situação"), Text(report.IsEmpty ? "Sem vendas simuladas aprovadas" : "Vendas simuladas aprovadas"));
            AddRow(data);
            AddRow(data, Text("Data (ISO)", 1), Text("Pedidos", 1), Text("Cupcakes", 1),
                Text("Subtotal (BRL)", 1), Text("Desconto (BRL)", 1), Text("Total (BRL)", 1));
            foreach (var day in report.Days)
                AddRow(data, Text(day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    Number(day.OrderCount), Number(day.ItemCount), Number(day.Subtotal, 2),
                    Number(day.Discount, 2), Number(day.Total, 2));
            AddRow(data, Text("TOTAL", 1), Number(report.OrderCount, 1), Number(report.ItemCount, 1),
                Number(report.Subtotal, 3), Number(report.Discount, 3), Number(report.Total, 3));

            worksheet.Worksheet = new Worksheet(
                new Columns(new Column { Min = 1, Max = 1, Width = 25, CustomWidth = true },
                    new Column { Min = 2, Max = 3, Width = 19, CustomWidth = true },
                    new Column { Min = 4, Max = 6, Width = 22, CustomWidth = true }), data);
            workbook.Workbook.Append(new Sheets(new Sheet {
                Id = workbook.GetIdOfPart(worksheet), SheetId = 1, Name = "Vendas simuladas" }));
        }
        return stream.ToArray();
    }

    private static Cell Text(string value, uint style = 0) => new() {
        DataType = CellValues.InlineString, StyleIndex = style,
        InlineString = new InlineString(new Text(value)) };

    private static Cell Number(decimal value, uint style = 0) => new() {
        DataType = CellValues.Number, StyleIndex = style,
        CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture)) };

    private static void AddRow(SheetData data, params Cell[] cells)
    {
        var index = (uint)data.ChildElements.Count + 1;
        var row = new Row { RowIndex = index };
        for (var column = 0; column < cells.Length; column++)
        {
            cells[column].CellReference = $"{(char)('A' + column)}{index}";
            row.Append(cells[column]);
        }
        data.Append(row);
    }
}
