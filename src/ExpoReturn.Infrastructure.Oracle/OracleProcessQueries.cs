using ExpoReturn.Application;
using ExpoReturn.Domain;
using static ExpoReturn.Infrastructure.Oracle.OracleDb;

namespace ExpoReturn.Infrastructure.Oracle;

/// <summary>
/// Inbox, Balance / Duties and Alerts straight from the database: SQL on IB_PROCESS_INBOX_V / IB_PROCESS_PALLETS_V
/// (05_views.sql), one page at a time (OFFSET … FETCH NEXT, Oracle 12c+). Status, duty risk and due dates come from the
/// views, calculated with today's date, so a page always shows the data as it is now. Filters and order are built only
/// from fixed SQL texts; user text always goes as a bind variable.
/// </summary>
public sealed class OracleProcessQueries : IProcessQueries
{
    private readonly OracleDb _db;
    private readonly OracleProcessRepository _repo;

    public OracleProcessQueries(OracleDb db, OracleProcessRepository repo) { _db = db; _repo = repo; }

    private string Inbox => _db.T("IB_PROCESS_INBOX_V");
    private string PalletsView => _db.T("IB_PROCESS_PALLETS_V");

    // ------------------------------------------------------------------ filter and order

    /// <summary>WHERE of the inbox view (alias i) and its bind variables.</summary>
    public (string Sql, (string Name, object? Value)[] Args) Where(ProcessFilter f) => Where(f, _db.T);

    /// <summary>WHERE of the inbox view (alias i); <paramref name="table"/> qualifies the table names (schema).</summary>
    public static (string Sql, (string Name, object? Value)[] Args) Where(ProcessFilter f, Func<string, string> table)
    {
        var sql = new List<string> { "1 = 1" };
        var args = new List<(string, object?)>();
        if (f.ValueStreamId is { } vs) { sql.Add("i.VALUE_STREAM_ID = :vs"); Bind(args, ("vs", vs)); }
        if (f.Status is { } st) { sql.Add("i.PROCESS_STATUS_ID = :status"); Bind(args, ("status", (int)st)); }
        if (f.OnlyOpen) sql.Add("i.IS_CLOSED = 'N'");
        if (f.OnlyDutyRisk) sql.Add("i.IS_DUTY_RISK = 'Y'");
        if (f.OnlyFindings) sql.Add("i.IS_LEGACY = 'Y' AND i.OPEN_FINDINGS > 0");
        if (!string.IsNullOrWhiteSpace(f.MineOf))
        {
            // Same rule as ProcessFilter.IsMine, on the table (the view does not need new columns).
            sql.Add($@"EXISTS (SELECT 1 FROM {table("IB_PROCESS_PROCESSES")} m WHERE m.PROCESS_ID = i.PROCESS_ID
                              AND (UPPER(m.CREATED_BY) = :mine OR UPPER(m.QMM_RESPONSIBLE_NT) = :mine OR UPPER(m.REQUESTOR_NT) = :mine))");
            Bind(args, ("mine", f.MineOf.Trim().ToUpperInvariant()));
        }
        if (f.PendingFor is { } scopes)
        {
            // "Pending for my area": same conditions as PendingWork.IsPendingFor, on an OPEN pallet of the process.
            var parts = new List<string>();
            for (var n = 0; n < scopes.Count; n++)
            {
                var s = scopes[n];
                var cond = new List<string> { PendingWork.SqlCondition(s.Area) };
                if (s.ValueStreamId != 0) { cond.Add($"m.VALUE_STREAM_ID = :pvs{n}"); Bind(args, ($"pvs{n}", s.ValueStreamId)); }
                if (s.OnlyOwnOf is { } own)
                {
                    cond.Add($"(UPPER(m.CREATED_BY) = :pown{n} OR UPPER(m.QMM_RESPONSIBLE_NT) = :pown{n})");
                    Bind(args, ($"pown{n}", own.Trim().ToUpperInvariant()));
                }
                parts.Add("(" + string.Join(" AND ", cond) + ")");
            }
            sql.Add(parts.Count == 0 ? "1 = 0" :
                $@"EXISTS (SELECT 1 FROM {table("IB_PROCESS_PALLETS_V")} v
                             JOIN {table("IB_PROCESS_PALLETS")} x ON x.PALLET_ID = v.PALLET_ID
                             JOIN {table("IB_PROCESS_PROCESSES")} m ON m.PROCESS_ID = v.PROCESS_ID
                            WHERE v.PROCESS_ID = i.PROCESS_ID AND v.IS_OPEN = 'Y' AND ({string.Join(" OR ", parts)}))");
        }
        if (f.SearchText is { } text)
        {
            sql.Add($@"(UPPER(i.FOLIO_CODE) LIKE :q ESCAPE '\' OR UPPER(i.IMPORT_INVOICE) LIKE :q ESCAPE '\'
                   OR UPPER(i.QMM_RESPONSIBLE) LIKE :q ESCAPE '\' OR UPPER(i.REQUESTOR_NAME) LIKE :q ESCAPE '\'
                   OR UPPER(i.DELIVERY_NUMBER) LIKE :q ESCAPE '\'
                   OR EXISTS (SELECT 1 FROM {table("IB_PROCESS_PALLETS")} x WHERE x.PROCESS_ID = i.PROCESS_ID
                              AND (UPPER(x.RETURNED_PART_NUMBER) LIKE :q ESCAPE '\' OR UPPER(x.ORIGINAL_HU) LIKE :q ESCAPE '\'
                                   OR UPPER(x.ORIGINAL_BATCH) LIKE :q ESCAPE '\' OR UPPER(x.PO_NUMBER) LIKE :q ESCAPE '\'
                                   OR UPPER(x.CUSTOMS_EXPORT) LIKE :q ESCAPE '\'
                                   OR UPPER(i.FOLIO_CODE || '-' || LPAD(TO_CHAR(x.LINE_NO), 2, '0')) LIKE :q ESCAPE '\')))");
            Bind(args, ("q", Like(text)));
        }
        return (string.Join(" AND ", sql), args.ToArray());
    }

    private static void Bind(List<(string, object?)> args, (string Name, object? Value) arg) => args.Add(arg);

    private static (string Name, object? Value)[] Args(IEnumerable<(string Name, object? Value)> args, params (string Name, object? Value)[] more) =>
        args.Concat(more).ToArray();

    /// <summary>%text% in capitals, with % _ \ taken literally (ESCAPE '\').</summary>
    public static string Like(string text) =>
        "%" + text.Trim().ToUpperInvariant().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    private static readonly Dictionary<string, string> SortColumns = new()
    {
        ["ValueStream"] = "i.VALUE_STREAM_ID", ["Status"] = "i.PROCESS_STATUS_ID", ["PalletCount"] = "i.PALLET_COUNT",
        ["Qty"] = "i.QTY_IMPORTED", ["Material"] = "i.FIRST_PART_NUMBER", ["PoNumbers"] = "i.FIRST_PO_NUMBER",
        ["SapUsdTotal"] = "i.SAP_TOTAL_USD", ["Balance"] = "i.GENERAL_BALANCE_USD", ["DaysInJuP1"] = "i.DAYS_IN_JUP1",
        ["DueDate"] = "i.NEAREST_DUE_DATE", ["DaysToLegalDue"] = "i.DAYS_TO_DUE", ["Qmm"] = "i.QMM_RESPONSIBLE",
        ["Requestor"] = "i.REQUESTOR_NAME", ["Department"] = "i.REQUESTING_DEPARTMENT", ["Invoice"] = "i.IMPORT_INVOICE",
        ["DqText"] = "i.DATA_QUALITY_FLAGS",
    };

    /// <summary>ORDER BY from a fixed list of columns (empty values last), always ending in VS, folio, id: stable pages.</summary>
    public static string OrderBy(ProcessSort sort)
    {
        var dir = sort.Descending ? "DESC" : "ASC";
        var first = sort.IsDefault ? "CASE WHEN i.IS_CLOSED = 'Y' THEN 1 ELSE 0 END, i.DAYS_TO_DUE NULLS LAST"
            : sort.Member == "FolioCode" ? $"i.VALUE_STREAM_ID {dir}, i.FOLIO_NUMBER {dir}"
            : $"{SortColumns[sort.Member!]} {dir} NULLS LAST";
        return $"{first}, i.VALUE_STREAM_ID, i.FOLIO_NUMBER, i.PROCESS_ID";
    }

    // ------------------------------------------------------------------ inbox

    public ProcessTotals Totals(ProcessFilter filter)
    {
        var (where, args) = Where(filter);
        return _db.Query($@"SELECT COUNT(*) AS PROCESS_COUNT,
                                   NVL(SUM(CASE WHEN i.IS_CLOSED = 'N' THEN 1 ELSE 0 END), 0) AS OPEN_COUNT,
                                   NVL(SUM(CASE WHEN i.IS_DUTY_RISK = 'Y' THEN 1 ELSE 0 END), 0) AS RISK_COUNT,
                                   NVL(SUM(i.GENERAL_BALANCE_USD), 0) AS BALANCE_USD
                              FROM {Inbox} i WHERE {where}",
            r => new ProcessTotals(Int(r, "PROCESS_COUNT") ?? 0, Int(r, "OPEN_COUNT") ?? 0, Int(r, "RISK_COUNT") ?? 0, Dec(r, "BALANCE_USD") ?? 0m),
            args).Single();
    }

    public IReadOnlyList<ProcessSummary> Page(ProcessFilter filter, ProcessSort sort, int skip, int take)
    {
        var (where, args) = Where(filter);
        return _db.Query($@"SELECT i.* FROM {Inbox} i WHERE {where} ORDER BY {OrderBy(sort)}
                            OFFSET :skip ROWS FETCH NEXT :take ROWS ONLY", ReadSummary,
            Args(args, ("skip", Math.Max(0, skip)), ("take", Math.Max(0, take))));
    }

    private static ProcessSummary ReadSummary(global::Oracle.ManagedDataAccess.Client.OracleDataReader r) => new(
        Int(r, "PROCESS_ID")!.Value, Int(r, "VALUE_STREAM_ID")!.Value, Int(r, "FOLIO_NUMBER")!.Value, Str(r, "FOLIO_CODE")!,
        (WorkflowStatus)(Int(r, "PROCESS_STATUS_ID") ?? 1), Int(r, "PALLET_COUNT") ?? 0, Int(r, "QTY_IMPORTED") ?? 0,
        Str(r, "FIRST_PART_NUMBER"), Int(r, "PART_NUMBER_COUNT") ?? 0, Str(r, "FIRST_PO_NUMBER"), Int(r, "PO_NUMBER_COUNT") ?? 0,
        Dec(r, "SAP_TOTAL_USD") ?? 0m, Dec(r, "GENERAL_BALANCE_USD") ?? 0m, Dec(r, "DUTIES_TO_PAY_USD") ?? 0m, Flag(r, "IS_DUTY_RISK"),
        Date(r, "CROSSING_DATE"), Int(r, "DAYS_IN_JUP1"), Date(r, "NEAREST_DUE_DATE"), Int(r, "DAYS_TO_DUE"),
        Str(r, "QMM_RESPONSIBLE"), Str(r, "REQUESTOR_NAME"), Str(r, "REQUESTING_DEPARTMENT"), Str(r, "IMPORT_INVOICE"),
        Str(r, "DELIVERY_NUMBER"), Flag(r, "IS_LEGACY"), Int(r, "OPEN_FINDINGS") ?? 0, Str(r, "DATA_QUALITY_FLAGS"));

    public IReadOnlyList<PalletSummary> PalletsOf(IReadOnlyCollection<int> processIds)
    {
        var list = new List<PalletSummary>();
        foreach (var chunk in processIds.Distinct().Chunk(500))
            list.AddRange(_db.Query($@"SELECT v.* FROM {PalletsView} v WHERE v.PROCESS_ID IN ({string.Join(", ", chunk)})
                                       ORDER BY v.PROCESS_ID, v.LINE_NO", r => new PalletSummary(
                Int(r, "PALLET_ID")!.Value, Int(r, "PROCESS_ID")!.Value, Int(r, "LINE_NO") ?? 1, Str(r, "PALLET_CODE")!,
                (WorkflowStatus)(Int(r, "WORKFLOW_STATUS_ID") ?? 1), Str(r, "RETURNED_PART_NUMBER"), Int(r, "QTY_IMPORTED") ?? 0,
                Str(r, "ORIGINAL_HU"), Str(r, "ORIGINAL_BATCH"), Str(r, "PO_NUMBER"), Dec(r, "GENERAL_BALANCE_USD") ?? 0m,
                Date(r, "LEGAL_DUE_DATE"), Int(r, "DAYS_TO_DUE"), Str(r, "STATUS_CATEGORY_CODE"), Str(r, "CUSTOMS_EXPORT"))));
        return list;
    }

    public IReadOnlyList<IbProcess> Load(ProcessFilter filter, ProcessSort sort)
    {
        var (where, args) = Where(filter);
        var order = _db.Query($"SELECT i.PROCESS_ID FROM {Inbox} i WHERE {where} ORDER BY {OrderBy(sort)}", r => Int(r, "PROCESS_ID")!.Value, args);
        var rank = order.Select((id, n) => (id, n)).ToDictionary(x => x.id, x => x.n);
        return _repo.LoadWhere($"prc.PROCESS_ID IN (SELECT i.PROCESS_ID FROM {Inbox} i WHERE {where})", args)
            .OrderBy(p => rank.GetValueOrDefault(p.Id, int.MaxValue)).ToList();
    }

    public bool AnyLegacy() =>
        _db.Query($"SELECT COUNT(*) AS N FROM {_db.T("IB_PROCESS_PROCESSES")} WHERE IS_LEGACY = 'Y' AND ROWNUM = 1", r => Int(r, "N") ?? 0).Single() > 0;

    // ------------------------------------------------------------------ Balance / Duties and Alerts

    public IReadOnlyList<BalanceRow> Balance(int? fiscalYear)
    {
        var year = fiscalYear is null ? "" : " AND EXTRACT(YEAR FROM v.CROSSING_DATE) = :year";
        var args = fiscalYear is { } y ? Args(Array.Empty<(string, object?)>(), ("year", y)) : Array.Empty<(string, object?)>();
        var rows = _db.Query($@"SELECT v.VALUE_STREAM_ID, COUNT(*) AS PALLET_COUNT,
                                       SUM(CASE WHEN v.IS_OPEN = 'Y' THEN 1 ELSE 0 END) AS OPEN_COUNT,
                                       SUM(CASE WHEN v.IS_DUTY_RISK = 'Y' THEN 1 ELSE 0 END) AS RISK_COUNT,
                                       SUM(v.SAP_TOTAL_USD) AS TOTAL_USD, SUM(v.MFO_INVENTORY_USD) AS INVENTORY_USD,
                                       SUM(v.HSE_SCRAP_USD) AS SCRAP_USD, SUM(v.MFO_LOW_FAILURE_USD) AS FAILURE_USD,
                                       SUM(v.DUTIES_TO_PAY_USD) AS DUTIES_USD
                                  FROM {PalletsView} v
                                 WHERE v.CROSSING_DATE IS NOT NULL{year}
                                 GROUP BY v.VALUE_STREAM_ID ORDER BY v.VALUE_STREAM_ID", r =>
        {
            var total = Dec(r, "TOTAL_USD") ?? 0m;
            var inv = Dec(r, "INVENTORY_USD") ?? 0m;
            var hse = Dec(r, "SCRAP_USD") ?? 0m;
            var fail = Dec(r, "FAILURE_USD") ?? 0m;
            return new BalanceRow($"VS{Int(r, "VALUE_STREAM_ID")}", Int(r, "PALLET_COUNT") ?? 0, Int(r, "OPEN_COUNT") ?? 0,
                Int(r, "RISK_COUNT") ?? 0, total, inv, hse, fail, total - inv - hse - fail, inv + hse + fail, Dec(r, "DUTIES_USD") ?? 0m);
        }, args);
        if (rows.Count > 0)
            rows.Add(new BalanceRow("Total", rows.Sum(x => x.Pallets), rows.Sum(x => x.OpenPallets), rows.Sum(x => x.DutyRiskPallets),
                rows.Sum(x => x.TotalImportedUsd), rows.Sum(x => x.MfoInventoryUsd), rows.Sum(x => x.HseScrapUsd), rows.Sum(x => x.MfoLowFailureUsd),
                rows.Sum(x => x.ExportedUsd), rows.Sum(x => x.BalanceGeneralUsd), rows.Sum(x => x.DutiesToPayUsd)));
        return rows;
    }

    public IReadOnlyList<StatusCount> StatusDistribution() =>
        _db.Query($@"SELECT i.PROCESS_STATUS_ID, COUNT(*) AS PROCESS_COUNT FROM {Inbox} i
                     GROUP BY i.PROCESS_STATUS_ID ORDER BY i.PROCESS_STATUS_ID",
            r => new StatusCount((WorkflowStatus)(Int(r, "PROCESS_STATUS_ID") ?? 1), Int(r, "PROCESS_COUNT") ?? 0));

    public IReadOnlyList<int> FiscalYears() =>
        _db.Query($@"SELECT DISTINCT EXTRACT(YEAR FROM ELP_JUP1_CROSSING_DATE) AS FISCAL_YEAR FROM {_db.T("IB_PROCESS_PROCESSES")}
                     WHERE ELP_JUP1_CROSSING_DATE IS NOT NULL ORDER BY FISCAL_YEAR", r => Int(r, "FISCAL_YEAR")!.Value);

    /// <summary>Only the processes with an open pallet are read (closed history is never loaded for the alerts).</summary>
    public IReadOnlyList<PalletReturn> OpenPallets() =>
        _repo.LoadWhere($"EXISTS (SELECT 1 FROM {PalletsView} v WHERE v.PROCESS_ID = prc.PROCESS_ID AND v.IS_OPEN = 'Y')")
            .SelectMany(p => p.Pallets).Where(p => !p.Status.IsClosed()).ToList();
}
