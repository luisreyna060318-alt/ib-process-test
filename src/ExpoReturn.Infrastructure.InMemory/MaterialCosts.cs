using ClosedXML.Excel;
using ExpoReturn.Application;
using ExpoReturn.Domain.Rules;

namespace ExpoReturn.Infrastructure.InMemory;

/// <summary>MM60 cost master in memory (production: IB_PROCESS_MM60_COSTS). Materials are compared normalized.</summary>
public sealed class InMemoryMaterialCostCatalog : IMaterialCostCatalog
{
    private Dictionary<string, MaterialCost> _items = new();
    private readonly object _gate = new();

    public int Count { get { lock (_gate) return _items.Count; } }
    public string? SourceName { get; private set; }
    public DateTime? LoadedAtUtc { get; private set; }

    public MaterialCost? Find(string? material)
    {
        var key = Key(material);
        if (key is null) return null;
        lock (_gate) return _items.GetValueOrDefault(key);
    }

    /// <summary>Same rule as Oracle: materials in the file take the new price, the others keep the last one (nothing is deleted).</summary>
    public void Replace(IEnumerable<MaterialCost> costs, string sourceName, CostSource source = CostSource.Mm60File)
    {
        Dictionary<string, MaterialCost> map;
        lock (_gate) map = new Dictionary<string, MaterialCost>(_items);
        foreach (var c in costs)
            if (Key(c.Material) is { } k) map[k] = c with { Material = k };     // last row wins, as in a lookup
        lock (_gate) _items = map;
        SourceName = sourceName;
        LoadedAtUtc = DateTime.UtcNow;
    }

    private static string? Key(string? material) =>
        string.IsNullOrWhiteSpace(material) ? null : Identifiers.NormalizeMaterial(material.Trim().ToUpperInvariant());
}

public sealed record Mm60ReadResult(IReadOnlyList<MaterialCost> Costs, int RowsRead, int RowsSkipped, string SheetName);

/// <summary>
/// Reads an MM60 export (.xlsx) or the hidden "MM60 COST SAP" sheet of the Excel 2.0. Looks for a header row
/// (within the first 15 rows) with "Material" and "Price"; optional columns: "Material Description",
/// "Price unit", "Currency". Rows without material or numeric price are skipped.
/// </summary>
public static class Mm60Reader
{
    public static Mm60ReadResult Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var wb = new XLWorkbook(stream);
        return Read(wb) ?? throw new InvalidDataException("No sheet with the columns 'Material' and 'Price' was found (MM60 export).");
    }

    public static Mm60ReadResult? Read(XLWorkbook wb)
    {
        var sheets = wb.Worksheets.OrderBy(w => w.Name.Trim().Equals("MM60 COST SAP", StringComparison.OrdinalIgnoreCase) ? 0 : 1);
        foreach (var ws in sheets)
        {
            for (var r = 1; r <= 15; r++)
            {
                var cols = ws.Row(r).CellsUsed().ToDictionary(c => c.GetString().Trim().ToUpperInvariant(), c => c.Address.ColumnNumber);
                if (!cols.TryGetValue("MATERIAL", out var cMat) || !cols.TryGetValue("PRICE", out var cPrice)) continue;
                int? Col(string name) => cols.TryGetValue(name, out var c) ? c : null;
                var cDesc = Col("MATERIAL DESCRIPTION");
                var cUnit = Col("PRICE UNIT");
                var cCur = Col("CURRENCY");

                var list = new List<MaterialCost>();
                int read = 0, skipped = 0;
                var last = ws.LastRowUsed()?.RowNumber() ?? r;
                for (var i = r + 1; i <= last; i++)
                {
                    var row = ws.Row(i);
                    var material = row.Cell(cMat).GetString().Trim();
                    if (material.Length == 0) continue;
                    read++;
                    if (!row.Cell(cPrice).TryGetValue(out decimal price)) { skipped++; continue; }
                    decimal unit = 1;
                    if (cUnit is { } cu && row.Cell(cu).TryGetValue(out decimal u) && u > 0) unit = u;
                    var currency = cCur is { } cc ? row.Cell(cc).GetString().Trim() : "";
                    list.Add(new MaterialCost(material, cDesc is { } cd ? row.Cell(cd).GetString().Trim() : null, price, unit,
                                              currency.Length == 0 ? "USD" : currency));
                }
                return new Mm60ReadResult(list, read, skipped, ws.Name);
            }
        }
        return null;
    }
}

public sealed record Mm60ImportSummary(int Materials, int RowsSkipped, string SheetName, int RecordsUpdated);

/// <summary>
/// Owner action "Import MM60 file": replaces the cost master with the export and gives the cost to open records that
/// were saved without it ("PENDING"); they are recalculated and audited as SYSTEM.
/// </summary>
public static class Mm60Import
{
    public static Mm60ImportSummary Run(string path, IMaterialCostCatalog catalog, IPalletReturnRepository repo, IAuditTrail audit,
                                        IClock clock, Domain.CalcParameters parameters, string ownerNtUser)
    {
        var read = Mm60Reader.Read(path);
        catalog.Replace(read.Costs, Path.GetFileName(path));
        var updated = CompletePendingCosts(catalog, repo, audit, clock, parameters, $"MM60 import by {ownerNtUser}");
        audit.Record(new[] { new AuditEntry(0, "MM60", "MM60 cost master", null,
            $"{read.Costs.Count} materials loaded from {Path.GetFileName(path)}", ownerNtUser, clock.UtcNow, null) });
        return new Mm60ImportSummary(read.Costs.Count, read.RowsSkipped, read.SheetName, updated);
    }

    /// <summary>
    /// Gives the MM60 unit cost to the records still without it (new or imported from the Excel, "COSTEAR"), recalculates
    /// and audits them as SYSTEM. Returns how many were updated.
    /// </summary>
    public static int CompletePendingCosts(IMaterialCostCatalog catalog, IPalletReturnRepository repo, IAuditTrail audit, IClock clock,
                                           Domain.CalcParameters parameters, string reason)
    {
        var now = clock.UtcNow;
        var updated = 0;
        foreach (var p in repo.GetAll().Where(p => p.UnitCostSource == "PENDING"))
        {
            if (catalog.Find(p.ReturnedMaterial) is not { UnitCostUsd: > 0 } cost) continue;
            var edit = p.Clone();
            edit.UnitCostUsd = cost.UnitCostUsd;
            edit.UnitCostSource = "MM60";
            edit.ModifiedBy = "SYSTEM";
            edit.ModifiedAtUtc = now;
            Domain.PalletEvaluator.Apply(edit, parameters, clock.Today);
            try { repo.Save(edit); }
            catch (ConcurrencyException) { continue; }      // someone saved it meanwhile: it keeps PENDING until the next import
            audit.Record(new[] { new AuditEntry(edit.Id, edit.FolioCode, nameof(Domain.PalletReturn.UnitCostUsd), null,
                cost.UnitCostUsd.ToString(System.Globalization.CultureInfo.InvariantCulture), "SYSTEM", now, reason) });
            updated++;
        }
        return updated;
    }
}
