using ClosedXML.Excel;
using ExpoReturn.Domain;

namespace ExpoReturn.Infrastructure.InMemory;

/// <summary>
/// Exporta la vista filtrada de la bandeja a un Excel de SÓLO LECTURA (UX-B06): hoja protegida,
/// para consulta o envío; la captura sigue siendo en la aplicación.
/// </summary>
public static class ExcelExporter
{
    public static void ExportPallets(IEnumerable<PalletReturn> pallets, string path, DateTime today, string filterDescription)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Retornos");
        string[] headers =
        {
            "#", "VS", "Estatus", "Returned Pallet 13 Digits Part number", "Quantity imported", "SAP USD TOTAL", "Balance GENERAL DLLS",
            "Días en JuP1", "Días para vencer", "QMM Responsible", "Invoice", "Customs Export", "COMMENTS", "Calidad de datos",
        };
        ws.Cell(1, 1).Value = $"Expo & Return FG Follow Up · Bandeja exportada el {DateTime.Now:dd/MM/yyyy HH:mm} · Filtro: {filterDescription}";
        ws.Cell(1, 1).Style.Font.Italic = true;
        for (var c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(3, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F3864");
        }
        var r = 4;
        foreach (var p in pallets)
        {
            ws.Cell(r, 1).Value = p.FolioCode;
            ws.Cell(r, 2).Value = $"VS{p.ValueStreamId}";
            ws.Cell(r, 3).Value = p.StatusName;
            ws.Cell(r, 4).Value = p.ReturnedMaterial;
            ws.Cell(r, 5).Value = p.QtyImported;
            ws.Cell(r, 6).Value = p.SapUsdTotal;
            ws.Cell(r, 7).Value = p.Financials.BalanceGeneralUsd;
            if (p.DaysInJuP1(today) is { } d) ws.Cell(r, 8).Value = d;
            if (p.LegalDueDate is { } due && !p.Status.IsClosed()) ws.Cell(r, 9).Value = (int)(due - today).TotalDays;
            ws.Cell(r, 10).Value = p.QmmResponsible;
            ws.Cell(r, 11).Value = p.ImportInvoice;
            ws.Cell(r, 12).Value = p.CustomsExportReference;
            ws.Cell(r, 13).Value = p.StatusCategoryCode;
            ws.Cell(r, 14).Value = p.DataQualityText;
            r++;
        }
        ws.Range(4, 6, Math.Max(4, r - 1), 7).Style.NumberFormat.Format = "#,##0.00";
        ws.Columns().AdjustToContents(3, Math.Max(3, r - 1));
        ws.SheetView.FreezeRows(3);
        ws.Protect();                       // sólo lectura (sin contraseña: evita capturar en el Excel)
        wb.SaveAs(path);
    }
}
