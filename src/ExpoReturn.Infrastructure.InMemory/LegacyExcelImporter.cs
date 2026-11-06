using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using ExpoReturn.Application;
using ExpoReturn.Domain;
using ExpoReturn.Domain.Rules;

namespace ExpoReturn.Infrastructure.InMemory;

public sealed record LegacyImportResult(
    IReadOnlyList<PalletReturn> Pallets,
    IReadOnlyList<Person> Champions,
    IReadOnlyDictionary<string, int> IssueCounts,
    IReadOnlyDictionary<string, int> RowsPerSheet)
{
    public string Summary =>
        $"{Pallets.Count} registros importados ({string.Join(", ", RowsPerSheet.Select(kv => $"{kv.Key}: {kv.Value}"))}); " +
        $"{IssueCounts.Values.Sum()} observaciones de calidad de datos.";
}

/// <summary>
/// Lee la MATRIZ DE RASTREO 2.0 (tablas VS_1, VS_4, VS_5, VS_7, VS_9 y hoja "Champions VS") y la convierte
/// al modelo nuevo, SÓLO EN MEMORIA. Es el prototipo del ETL de migración (doc 04 §2).
/// Usa el valor cacheado de las fórmulas (XLOOKUP no se recalcula fuera de Excel).
/// </summary>
public static partial class LegacyExcelImporter
{
    private static readonly Dictionary<string, string> Alias = new(StringComparer.OrdinalIgnoreCase)
    {
        ["QMM Responsible"] = "QMM",
        ["Requesting department"] = "Dept",
        ["Requestor"] = "Requestor",
        ["Reason"] = "Reason",
        ["\"Return Reason\" 22.09.26"] = "ReasonDetail",
        ["Returned Pallet 13 Digits Part number"] = "PN",
        ["MM60 SAP UNIT COST"] = "Cost",
        ["Quantity"] = "Qty", ["Quantity imported"] = "Qty",
        ["Original #HU"] = "HU",
        ["Original Batch"] = "Batch",
        ["Date PO"] = "DatePO", ["Date Returned to WHS"] = "DatePO",
        ["Import Date"] = "ImportDate",
        ["Invoice"] = "Invoice",
        ["Import date ELP-JuP1"] = "Elp",
        ["PO Number"] = "PO", ["Return PO"] = "PO",
        ["Delivery Number 22.09.26"] = "Delivery",
        ["Returned Pallet 13 Dgt New PN"] = "NewPN",
        ["Physical material delivered to LOW"] = "Physical",
        ["New HU"] = "NewHU",
        ["Original Batch#2"] = "ReworkBatch",
        ["New Reinsertion Material Qty"] = "Reinsertion",
        ["Date send Back"] = "SentBack",
        ["Old Material Delivered to LOW"] = "Old",
        ["Scrap to HSE (Qty)"] = "Scrap",
        ["Scrap Date"] = "ScrapDate",
        ["LOW export"] = "LowExport",
        ["Customs Export"] = "Customs",
        ["GR Number"] = "GR",
        ["Qty (Kg). from HSE"] = "Kg", ["Kg Qty from HSE"] = "Kg",
        ["HSE confirmation Date"] = "HseDate",
        ["COMMENTS"] = "Comments", ["Date - Name:"] = "Comments", ["Column2"] = "Comments",
    };

    private static readonly Dictionary<string, string> CategoryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EXPORT"] = "EXPORT", ["SCRAP"] = "SCRAP", ["INVENTORY"] = "INVENTORY", ["ENVIO NACIONAL"] = "ENVIO_NACIONAL",
        ["SCRAP SIN SALIDA FISICA"] = "SCRAP_SIN_SALIDA_FISICA", ["NO REPORTADO CORRECTAMENTE"] = "NO_REPORTADO_CORRECTAMENTE",
        ["NO REPORTADO CON CONO VERDE"] = "NO_REPORTADO_CONO_VERDE", ["NO LLEGO CON CONO"] = "NO_LLEGO_CON_CONO",
        ["MATERIAL CON CONO NO REPORTADO POR LOW"] = "CONO_NO_REPORTADO_POR_LOW", ["MATERIAL NO LLEGO DE ELP"] = "MATERIAL_NO_LLEGO_DE_ELP",
        ["LOW-NO REPORTADO (PLATAFORMA,CONO,RMA)"] = "LOW_NO_REPORTADO_PCR", ["MFO-NO REPORTADO (PLATAFORMA,CONO,RMA)"] = "MFO_NO_REPORTADO_PCR",
    };

    public static LegacyImportResult Import(string path, DateTime today)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var wb = new XLWorkbook(stream);
        var pallets = new List<PalletReturn>();
        var issues = new Dictionary<string, int>();
        var perSheet = new Dictionary<string, int>();
        void Flag(PalletReturn p, string code) { p.DataQualityFlags.Add(code); issues[code] = issues.GetValueOrDefault(code) + 1; }

        foreach (var vs in new[] { 1, 4, 5, 7, 9 })
        {
            var table = wb.Worksheets.SelectMany(w => w.Tables).FirstOrDefault(t => t.Name.Equals($"VS_{vs}", StringComparison.OrdinalIgnoreCase));
            if (table is null) continue;
            var headerRow = table.HeadersRow();
            var cols = new Dictionary<string, int>();
            var seen = new Dictionary<string, int>();
            foreach (var cell in headerRow.Cells())
            {
                var h = Regex.Replace(cell.GetString(), @"\s+", " ").Trim().TrimEnd('.');
                seen[h] = seen.GetValueOrDefault(h) + 1;
                var key = seen[h] > 1 ? h + "#2" : h;
                if (Alias.TryGetValue(key, out var a) && !cols.ContainsKey(a)) cols[a] = cell.Address.ColumnNumber;
            }

            var count = 0;
            foreach (var row in table.DataRange.Rows())
            {
                var ws = row.Worksheet;
                var r = row.RangeAddress.FirstAddress.RowNumber;
                IXLCell? C(string k) => cols.TryGetValue(k, out var c) ? ws.Cell(r, c) : null;
                var pnRaw = Text(C("PN"));
                var qty = Int(C("Qty"));
                if (pnRaw is null && qty is null) continue;

                var p = new PalletReturn { ValueStreamId = vs, IsLegacy = true, LegacySource = $"{ws.Name}!fila {r}", CreatedBy = "MIGRACION" };
                var folioCell = ws.Cell(r, table.RangeAddress.FirstAddress.ColumnNumber - 1);
                p.FolioNumber = Int(folioCell) ?? 900000 + r;

                p.QmmResponsible = Text(C("QMM"));
                if (p.QmmResponsible is null) Flag(p, "DQ-QMM-MISSING");
                var requestor = Text(C("Requestor"));
                var dept = Text(C("Dept"));
                if (requestor is not null)
                {
                    var m = RequestorRx().Match(requestor);
                    if (m.Success) { dept ??= m.Groups["dept"].Value.ToUpperInvariant(); p.RequestorName = m.Groups["name"].Value.Trim(); }
                    else p.RequestorName = requestor;
                }
                p.RequestingDepartment = dept;
                if (dept is null) Flag(p, "DQ-DEPT-MISSING");
                var reasonText = Text(C("Reason"));
                p.ReasonId = Catalogs.Reasons.FirstOrDefault(x => x.Description.Equals(reasonText, StringComparison.OrdinalIgnoreCase))?.Id;
                if (p.ReasonId is null) Flag(p, "DQ-REASON-MISSING");
                p.ReturnReasonDetail = Text(C("ReasonDetail"));

                p.ReturnedMaterial = Identifiers.NormalizeMaterial(pnRaw);
                if (!Identifiers.IsValidMaterial(p.ReturnedMaterial)) Flag(p, "DQ-PN-FORMAT");
                var cost = Dec(C("Cost"));
                p.UnitCostUsd = cost is > 0 ? cost : null;
                p.UnitCostSource = p.UnitCostUsd is null ? "PENDING" : "LEGACY";
                if (p.UnitCostUsd is null) Flag(p, "DQ-COST-PENDING");
                p.QtyImported = qty ?? 0;
                if (p.QtyImported <= 0) Flag(p, "DQ-QTY-INVALID");

                var hu = FirstToken(Text(C("HU")));
                var batch = Text(C("Batch"));
                if (Identifiers.IsHandlingUnit(hu)) p.OriginalHU = hu;
                else if (Identifiers.IsBatch(hu)) { batch ??= hu!.ToUpperInvariant(); Flag(p, "DQ-HU-IS-BATCH"); }
                else if (hu is not null) { p.OriginalHU = hu; Flag(p, "DQ-HU-FORMAT"); }
                p.OriginalBatch = batch;

                p.DateReturnedToWhs = Date(C("DatePO"));
                p.ImportDate = Date(C("ImportDate"));
                if (C("ImportDate") is { } idc && idc.DataType == XLDataType.Text && p.ImportDate is not null) Flag(p, "DQ-DATE-TEXT");
                p.ImportInvoice = Text(C("Invoice"));
                var elp = Text(C("Elp"));
                if (elp is not null)
                {
                    p.ImportDateElpJup1 = LegacyDate.Parse(elp) ?? p.ImportDate;
                    var parts = elp.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 1) { p.ImportInvoice ??= parts[^1]; Flag(p, "DQ-ELP-CONCAT"); }
                    if (p.ImportDateElpJup1 is null) Flag(p, "DQ-DATE-UNPARSEABLE");
                }
                if (p.ImportInvoice is not null && !Identifiers.IsInvoice(p.ImportInvoice)) Flag(p, "DQ-INVOICE-FORMAT");
                if (p.ImportDate < p.DateReturnedToWhs) Flag(p, "DQ-DATE-ORDER");

                var po = Text(C("PO"));
                if (po is not null)
                {
                    var pm = PoRx().Match(po);
                    if (pm.Success) { p.ReturnPONumber = pm.Groups["po"].Value; if (pm.Groups["item"].Success) p.ReturnPOItem = pm.Groups["item"].Value; }
                    else { p.ReturnPONumber = po.Length > 10 ? po[..10] : po; Flag(p, "DQ-PO-FORMAT"); }
                }
                p.DeliveryNumber = Text(C("Delivery"));

                p.NewMaterial = Identifiers.NormalizeMaterial(Text(C("NewPN")));
                p.PhysicalDeliveredQty = Int(C("Physical"));
                p.NewHU = FirstToken(Text(C("NewHU")));
                if (p.NewHU is not null && !Identifiers.IsHandlingUnit(p.NewHU)) Flag(p, "DQ-NEWHU-FORMAT");
                p.ReworkBatch = Text(C("ReworkBatch"));
                p.ReinsertionQty = Int(C("Reinsertion"));
                var sentBack = C("SentBack");
                p.DateSentBack = Date(sentBack);
                if (sentBack is not null && p.DateSentBack is null && Text(sentBack) is { Length: > 3 } note)
                {
                    p.Comments = Append(p.Comments, note);          // comentarios capturados en la columna de fecha
                    Flag(p, "DQ-COMMENT-IN-DATE");
                }
                p.OldMaterialDeliveredQty = Int(C("Old"));
                p.ScrapQty = Int(C("Scrap"));
                p.ScrapDate = Date(C("ScrapDate"));

                var low = Text(C("LowExport"));
                if (low is not null)
                {
                    p.LowExportNote = low.Length > 200 ? low[..200] : low;
                    var lm = LowExportRx().Match(low);
                    if (lm.Success) p.LowExportNumber = int.Parse(lm.Groups[1].Value);
                    var dm = DateInTextRx().Match(low);
                    if (dm.Success) p.LowExportDate = LegacyDate.Parse(dm.Value);
                }
                var customs = C("Customs");
                if (customs is not null && customs.HasFormula) Flag(p, "DQ-CUSTOMS-FORMULA");
                else
                {
                    p.CustomsExportReference = Text(customs);
                    if (p.ClosureType == ClosureDocumentType.Unrecognized) Flag(p, "DQ-CUSTOMS-TEXT");
                }
                var gr = Text(C("GR"));
                p.ScrapGrNumber = Identifiers.IsGrNumber(gr) ? gr : null;
                p.HseScrapKg = Dec(C("Kg"));
                p.HseConfirmationDate = Date(C("HseDate"));

                var comment = Text(C("Comments"));
                if (comment is not null)
                {
                    if (CategoryMap.TryGetValue(comment.Trim(), out var code)) p.StatusCategoryCode = code;
                    else { p.Comments = Append(p.Comments, comment); Flag(p, "DQ-STATUS-TEXT"); }
                }

                var outQty = (p.OldMaterialDeliveredQty ?? 0) + (p.ScrapQty ?? 0);
                if (outQty > p.QtyImported && p.QtyImported > 0) Flag(p, "DQ-QTY-OVERFLOW");
                if (p.PhysicalDeliveredQty is not null && outQty < p.QtyImported) Flag(p, "DQ-QTY-GAP");
                if ((p.HseScrapKg > 0 || p.HseConfirmationDate is not null) && (p.ScrapQty ?? 0) == 0) Flag(p, "DQ-HSE-WITHOUT-QTY");

                PalletEvaluator.Apply(p, CalcParameters.Default, today);
                pallets.Add(p);
                count++;
            }
            perSheet[$"VS{vs}"] = count;
        }

        return new LegacyImportResult(pallets, ReadChampions(wb), issues, perSheet);
    }

    /// <summary>Hoja "Champions VS": fila 2 = área, fila 3 = Champion/Proxy, filas 4+ = VS.</summary>
    private static List<Person> ReadChampions(XLWorkbook wb)
    {
        var people = new List<Person>();
        var ws = wb.Worksheets.FirstOrDefault(w => w.Name.Trim().Equals("Champions VS", StringComparison.OrdinalIgnoreCase));
        if (ws is null) return people;
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = 4; r <= lastRow; r++)
        {
            var vsText = ws.Cell(r, 1).GetString().Trim();
            if (!vsText.StartsWith("VS", StringComparison.OrdinalIgnoreCase) || !int.TryParse(vsText[2..], out var vs)) continue;
            for (var c = 2; c <= lastCol; c++)
            {
                var name = Clean(ws.Cell(r, c).GetString());
                if (name is null) continue;
                var area = Clean(ws.Cell(2, c).GetString())?.ToUpperInvariant();
                var role = area switch
                {
                    "MFO" => RoleCode.Mfo, "QMM" => RoleCode.Qmm, "LOW" => RoleCode.Low,
                    "OSD43" or "GS/OSD43" => RoleCode.Gs, "MFQ" => RoleCode.Requestor,
                    "LOP" => RoleCode.Lop, "HSE" => RoleCode.Hse, _ => (RoleCode?)null,
                };
                if (role is null) continue;
                var assignment = Clean(ws.Cell(3, c).GetString()) ?? "Champion";
                people.Add(new Person(name, role.Value, vs, assignment));
            }
        }
        return people;
    }

    // ---------------- Lectura de celdas ----------------

    private static string? Clean(string? s)
    {
        if (s is null) return null;
        var t = Regex.Replace(s.Replace(' ', ' '), @"\s+", " ").Trim();
        return t.Length == 0 ? null : t;
    }

    private static XLCellValue Val(IXLCell c) => c.HasFormula ? c.CachedValue : c.Value;

    private static string? Text(IXLCell? c)
    {
        if (c is null) return null;
        var v = Val(c);
        if (v.IsBlank || v.IsError) return null;
        if (v.IsNumber) return Clean(v.GetNumber().ToString("0.##########", CultureInfo.InvariantCulture));
        if (v.IsDateTime) return v.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return Clean(v.ToString(CultureInfo.InvariantCulture));
    }

    private static int? Int(IXLCell? c)
    {
        if (c is null) return null;
        var v = Val(c);
        if (v.IsNumber) { var d = v.GetNumber(); return d >= 0 && d == Math.Floor(d) && d < int.MaxValue ? (int)d : null; }
        if (v.IsText && int.TryParse(v.GetText().Trim().Replace(",", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) && i >= 0) return i;
        return null;
    }

    private static decimal? Dec(IXLCell? c)
    {
        if (c is null) return null;
        var v = Val(c);
        if (v.IsNumber) return (decimal)v.GetNumber();
        if (v.IsText && decimal.TryParse(v.GetText().Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) return d;
        return null;
    }

    private static DateTime? Date(IXLCell? c)
    {
        if (c is null) return null;
        var v = Val(c);
        if (v.IsDateTime) return v.GetDateTime().Date;
        if (v.IsNumber) { var d = v.GetNumber(); return d is > 30000 and < 80000 ? DateTime.FromOADate(d).Date : null; }
        if (v.IsText) return LegacyDate.Parse(v.GetText());
        return null;
    }

    private static string? FirstToken(string? s) => s?.Split(new[] { ' ', '/' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
    private static string Append(string? a, string b) => string.IsNullOrWhiteSpace(a) ? b : $"{a} | {b}";

    [GeneratedRegex(@"^\s*(?<dept>(QMM|MFQ|MFO|LOP|LOW|PUQ)[\w\-]*?\d?)\s*(,\s*\w+)?\s*[:\-.]\s*(?<name>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex RequestorRx();
    [GeneratedRegex(@"^\s*(?<po>\d{8})(\.\d+)?\s*(-\s*(?<item>\d{1,6}))?\s*$")] private static partial Regex PoRx();
    [GeneratedRegex(@"expo\s*#?\s*(\d{1,2})", RegexOptions.IgnoreCase)] private static partial Regex LowExportRx();
    [GeneratedRegex(@"\d{1,2}[/.-](\d{1,2}|[A-Za-z]{3})[/.-]\d{2,4}")] private static partial Regex DateInTextRx();
}

/// <summary>Fechas heredadas: 'JAN.20.2025', 'DIC.11.24', 'Juio/16/2026', '17/07/2026', '20-Mar.26', '29/11/24'.</summary>
public static class LegacyDate
{
    private static readonly Dictionary<string, int> Months = new(StringComparer.OrdinalIgnoreCase)
    {
        ["JAN"] = 1, ["ENE"] = 1, ["FEB"] = 2, ["MAR"] = 3, ["APR"] = 4, ["ABR"] = 4, ["MAY"] = 5, ["JUN"] = 6,
        ["JUL"] = 7, ["JUI"] = 7, ["AUG"] = 8, ["AGO"] = 8, ["SEP"] = 9, ["SET"] = 9, ["OCT"] = 10, ["NOV"] = 11,
        ["DEC"] = 12, ["DIC"] = 12,
    };

    public static DateTime? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().ToUpperInvariant();
        if (s is "N/A" or "NA" or "0" or "-") return null;
        if (DateTime.TryParseExact(s.Length >= 10 ? s[..10] : s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso)) return iso;
        s = s.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        var parts = s.Split(new[] { '.', '/', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) return null;
        if (!int.TryParse(parts[2], out var y)) return null;
        if (y < 100) y += 2000;
        if (y is < 2020 or > 2099) return null;
        int m, d;
        if (char.IsLetter(parts[0][0]))
        {
            if (!Months.TryGetValue(parts[0][..Math.Min(3, parts[0].Length)], out m) || !int.TryParse(parts[1], out d)) return null;
        }
        else if (char.IsLetter(parts[1][0]))
        {
            if (!Months.TryGetValue(parts[1][..Math.Min(3, parts[1].Length)], out m) || !int.TryParse(parts[0], out d)) return null;
        }
        else
        {
            if (!int.TryParse(parts[0], out var a) || !int.TryParse(parts[1], out var b)) return null;
            (d, m) = b > 12 ? (b, a) : (a, b);                    // dd/mm salvo que el 2º > 12
        }
        if (m is < 1 or > 12 || d is < 1 or > 31 || d > DateTime.DaysInMonth(y, m)) return null;
        return new DateTime(y, m, d);
    }
}
