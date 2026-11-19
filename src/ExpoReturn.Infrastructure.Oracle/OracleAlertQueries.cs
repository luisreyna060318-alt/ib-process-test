using ExpoReturn.Application;
using ExpoReturn.Domain;
using ExpoReturn.Domain.Rules;
using static ExpoReturn.Infrastructure.Oracle.OracleDb;

namespace ExpoReturn.Infrastructure.Oracle;

/// <summary>
/// Alerts calculated by the database on IB_PROCESS_PALLETS_V (open pallets only), with the same rules as
/// <see cref="ReportingService.Alerts"/>: the screen reads one page of 50 and the totals, instead of every open process
/// with its pallets. Handled alerts (IB_PROCESS_ALERTS) and the responsible area are filtered in SQL too. "Today" is the
/// date of the PC (like the C# rules); the parameters are the ones in force (IB_PROCESS_PARAMETERS).
/// </summary>
public sealed class OracleAlertQueries : IAlertQueries
{
    private readonly OracleDb _db;
    public OracleAlertQueries(OracleDb db) => _db = db;

    private static readonly AlertType[] Types = Enum.GetValues<AlertType>();

    /// <summary>One SELECT per alert type over the open pallets (b): numbers of the message and the order of the screen.</summary>
    private static string Branch(AlertType t, string condition, string n1 = "0", string n2 = "0", string usd = "0") =>
        $@"SELECT b.PALLET_ID, b.PALLET_CODE, b.VALUE_STREAM_ID, b.RETURNED_PART_NUMBER, '{OracleAlertStore.Code(t)}' AS ALERT_CODE,
                  {AlertTexts.OwnerRank(AlertTexts.Owner(t))} AS OWNER_ORD, {(int)AlertTexts.SeverityOf(t)} AS SEV,
                  {n1} AS N1, {n2} AS N2, {usd} AS USD
             FROM b WHERE {condition}";

    private string Alerts()
    {
        const string crossed = "b.CROSSING_DATE IS NOT NULL";
        const string term = crossed + " AND b.LEGAL_DUE_DATE IS NOT NULL AND b.QTY_OPEN_TOTAL > 0";
        var branches = new[]
        {
            Branch(AlertType.CostPending, "NVL(b.MM60_UNIT_COST_USD, 0) = 0"),
            Branch(AlertType.LegalExpired, term + " AND b.LEFT_DAYS < 0", "-b.LEFT_DAYS", usd: "b.GENERAL_BALANCE_USD"),
            Branch(AlertType.AgingCritical, term + " AND b.LEFT_DAYS BETWEEN 0 AND :crit", "b.LEFT_DAYS", usd: "b.GENERAL_BALANCE_USD"),
            Branch(AlertType.AgingWarning, term + " AND b.LEFT_DAYS > :crit AND b.LEFT_DAYS <= :warn", "b.LEFT_DAYS"),
            Branch(AlertType.ReworkSla, crossed + " AND b.QTY_OPEN_FLOOR > 0 AND b.DAYS_IN > :slaRework", "b.QTY_OPEN_FLOOR", "b.DAYS_IN"),
            Branch(AlertType.ExportSla, crossed + " AND b.QTY_PENDING_EXPORT > 0 AND b.SENT_BACK_DATE IS NOT NULL AND b.DAYS_SENT > :slaExport",
                   "b.QTY_PENDING_EXPORT", "b.DAYS_SENT"),
            Branch(AlertType.HseSla, crossed + " AND b.QTY_PENDING_HSE > 0 AND b.SCRAP_DATE IS NOT NULL AND CAST(:today AS DATE) - TRUNC(b.SCRAP_DATE) > :slaHse",
                   "b.QTY_PENDING_HSE"),
            Branch(AlertType.QtyMismatch, crossed + " AND b.QTY_MISSING > 0", "b.QTY_MISSING", usd: "b.QTY_MISSING * NVL(b.MM60_UNIT_COST_USD, 0)"),
        };
        return $@"WITH b AS (SELECT v.PALLET_ID, v.PALLET_CODE, v.VALUE_STREAM_ID, v.RETURNED_PART_NUMBER, v.MM60_UNIT_COST_USD, v.CROSSING_DATE,
                                    v.LEGAL_DUE_DATE, v.QTY_OPEN_FLOOR, v.QTY_PENDING_EXPORT, v.QTY_PENDING_HSE, v.QTY_MISSING, v.QTY_OPEN_TOTAL,
                                    v.GENERAL_BALANCE_USD, v.SENT_BACK_DATE, v.SCRAP_DATE,
                                    CAST(:today AS DATE) - TRUNC(v.CROSSING_DATE) AS DAYS_IN, TRUNC(v.LEGAL_DUE_DATE) - CAST(:today AS DATE) AS LEFT_DAYS,
                                    CAST(:today AS DATE) - TRUNC(v.SENT_BACK_DATE) AS DAYS_SENT
                               FROM {_db.T("IB_PROCESS_PALLETS_V")} v WHERE v.IS_OPEN = 'Y')
                  SELECT x.*, CASE WHEN EXISTS (SELECT 1 FROM {_db.T("IB_PROCESS_ALERTS")} h WHERE h.PALLET_ID = x.PALLET_ID
                                                  AND h.ALERT_TYPE = x.ALERT_CODE AND h.HANDLED_AT IS NOT NULL) THEN 'Y' ELSE 'N' END AS HANDLED
                    FROM ({string.Join("\n UNION ALL ", branches)}) x";
    }

    /// <summary>Area filter as a fixed list of alert codes (built from the code, not from user text).</summary>
    private static string AreaCondition(AlertFilter f)
    {
        if (string.IsNullOrEmpty(f.Area)) return "1 = 1";
        var codes = Types.Where(t => AlertTexts.Owner(t).Split(" + ").Contains(f.Area)).Select(t => $"'{OracleAlertStore.Code(t)}'").ToList();
        return codes.Count == 0 ? "1 = 0" : $"y.ALERT_CODE IN ({string.Join(", ", codes)})";
    }

    private static List<(string, object?)> Binds(CalcParameters prm, DateTime today)
    {
        var args = new List<(string, object?)>();
        Bind(args, ("today", today.Date));
        Bind(args, ("crit", prm.AlertCriticalDaysLeft));
        Bind(args, ("warn", prm.AlertWarningDaysLeft));
        Bind(args, ("slaRework", prm.SlaReworkDays));
        Bind(args, ("slaExport", prm.SlaExportDays));
        Bind(args, ("slaHse", prm.SlaHseDays));
        return args;
    }

    private static void Bind(List<(string, object?)> args, (string Name, object? Value) arg) => args.Add(arg);

    public AlertTotals Totals(AlertFilter filter, CalcParameters prm, DateTime today)
    {
        var args = Binds(prm, today);
        var row = _db.Query($@"SELECT NVL(SUM(CASE WHEN y.HANDLED = 'N' AND y.SEV = 0 THEN 1 ELSE 0 END), 0) AS CRIT,
                                      NVL(SUM(CASE WHEN y.HANDLED = 'N' AND y.SEV = 1 THEN 1 ELSE 0 END), 0) AS WARN,
                                      NVL(SUM(CASE WHEN y.HANDLED = 'Y' THEN 1 ELSE 0 END), 0) AS DONE, COUNT(*) AS ALL_ROWS
                                 FROM ({Alerts()}) y WHERE {AreaCondition(filter)}",
            r => (Crit: Int(r, "CRIT") ?? 0, Warn: Int(r, "WARN") ?? 0, Done: Int(r, "DONE") ?? 0, All: Int(r, "ALL_ROWS") ?? 0), args.ToArray()).First();
        return new AlertTotals(row.Crit, row.Warn, row.Done, filter.ShowHandled ? row.All : row.All - row.Done);
    }

    public IReadOnlyList<Alert> Page(AlertFilter filter, CalcParameters prm, DateTime today, int skip, int take)
    {
        var args = Binds(prm, today);
        Bind(args, ("skip", Math.Max(0, skip)));
        Bind(args, ("take", Math.Max(0, take)));
        var handled = filter.ShowHandled ? "" : " AND y.HANDLED = 'N'";
        var byCode = Types.ToDictionary(OracleAlertStore.Code);
        return _db.Query($@"SELECT y.* FROM ({Alerts()}) y WHERE {AreaCondition(filter)}{handled}
                             ORDER BY y.OWNER_ORD, y.SEV, NLSSORT(y.PALLET_CODE, 'NLS_SORT=BINARY'), y.ALERT_CODE
                             OFFSET :skip ROWS FETCH NEXT :take ROWS ONLY",
            r => AlertTexts.Make(byCode[Str(r, "ALERT_CODE")!], Int(r, "PALLET_ID") ?? 0, Str(r, "PALLET_CODE") ?? "", Int(r, "VALUE_STREAM_ID") ?? 0,
                Int(r, "N1") ?? 0, Int(r, "N2") ?? 0, Dec(r, "USD") ?? 0m, Str(r, "RETURNED_PART_NUMBER")),
            args.ToArray());
    }
}
