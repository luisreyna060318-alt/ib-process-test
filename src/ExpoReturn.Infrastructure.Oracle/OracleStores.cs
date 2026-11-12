using System.Globalization;
using System.Text.RegularExpressions;
using ExpoReturn.Application;
using ExpoReturn.Domain;
using ExpoReturn.Domain.Rules;
using Oracle.ManagedDataAccess.Client;
using static ExpoReturn.Infrastructure.Oracle.OracleDb;

namespace ExpoReturn.Infrastructure.Oracle;

/// <summary>IB_PROCESS_AUDIT_LOG: insert only (the table trigger refuses UPDATE / DELETE).</summary>
public sealed class OracleAuditTrail : IAuditTrail
{
    private readonly OracleDb _db;
    private readonly string _appVersion;
    private static readonly Dictionary<string, string> PropertyOfColumn =
        OraclePalletRepository.ColumnOfProperty.ToDictionary(kv => kv.Value, kv => kv.Key);

    public OracleAuditTrail(OracleDb db, string appVersion) { _db = db; _appVersion = appVersion; }

    /// <summary>Entries without a pallet (users, MM60) are stored against their own table.</summary>
    private static (string Table, string RecordId) Target(AuditEntry e) => e.PalletId > 0
        ? ("IB_PROCESS_PALLET_RETURNS", e.PalletId.ToString(CultureInfo.InvariantCulture))
        : e.FolioCode switch
        {
            "USERS" => ("IB_PROCESS_USER_ACCESS", "USERS"),
            "MM60" => ("IB_PROCESS_MM60_COSTS", "MM60"),
            _ => ("IB_PROCESS_PALLET_RETURNS", e.FolioCode),
        };

    public void Record(IEnumerable<AuditEntry> entries)
    {
        var list = entries.ToList();
        if (list.Count == 0) return;
        var correlation = Guid.NewGuid().ToString();
        _db.InTransaction((c, tx) =>
        {
            foreach (var e in list)
            {
                var (table, id) = Target(e);
                using var cmd = _db.Command(c, $@"INSERT INTO {_db.T("IB_PROCESS_AUDIT_LOG")}
                    (TABLE_NAME, RECORD_ID, FOLIO_CODE, ACTION, FIELD_NAME, OLD_VALUE, NEW_VALUE, NT_USER, CLIENT_MACHINE, APP_VERSION, CORRELATION_ID, CHANGE_REASON)
                    VALUES (:t, :id, :folio, :action, :field, :oldValue, :newValue, :nt, :machine, :version, :corr, :reason)", tx);
                cmd.Parameters.Add("t", table);
                cmd.Parameters.Add("id", id);
                cmd.Parameters.Add("folio", Db(Cut(e.FolioCode, 14)));
                cmd.Parameters.Add("action", e.OldValue is null ? "I" : "U");
                cmd.Parameters.Add("field", Db(Cut(OraclePalletRepository.ColumnOfProperty.GetValueOrDefault(e.Field, e.Field), 30)));
                cmd.Parameters.Add("oldValue", Db(Cut(e.OldValue, 2000)));
                cmd.Parameters.Add("newValue", Db(Cut(e.NewValue, 2000)));
                cmd.Parameters.Add("nt", e.NtUser);
                cmd.Parameters.Add("machine", Cut(Environment.MachineName, 64));
                cmd.Parameters.Add("version", _appVersion);
                cmd.Parameters.Add("corr", correlation);
                cmd.Parameters.Add("reason", Db(Cut(e.Reason, 500)));
                cmd.ExecuteNonQuery();
            }
        });
    }

    public IReadOnlyList<AuditEntry> For(int palletId) => Read(
        $"SELECT * FROM {_db.T("IB_PROCESS_AUDIT_LOG")} WHERE TABLE_NAME = 'IB_PROCESS_PALLET_RETURNS' AND RECORD_ID = :id ORDER BY CHANGED_AT DESC, AUDIT_ID DESC",
        ("id", palletId.ToString(CultureInfo.InvariantCulture)));

    public IReadOnlyList<AuditEntry> All() => Read(
        $"SELECT * FROM (SELECT * FROM {_db.T("IB_PROCESS_AUDIT_LOG")} ORDER BY CHANGED_AT DESC, AUDIT_ID DESC) WHERE ROWNUM <= :n",
        ("n", Math.Max(100, _db.Settings.AuditRowsShown)));

    private List<AuditEntry> Read(string sql, params (string, object?)[] args) => _db.Query(sql, r =>
    {
        var table = Str(r, "TABLE_NAME");
        var record = Str(r, "RECORD_ID") ?? "";
        var palletId = table == "IB_PROCESS_PALLET_RETURNS" && int.TryParse(record, out var id) ? id : 0;
        var field = Str(r, "FIELD_NAME") ?? "";
        return new AuditEntry(palletId, Str(r, "FOLIO_CODE") ?? record, PropertyOfColumn.GetValueOrDefault(field, field),
            Str(r, "OLD_VALUE"), Str(r, "NEW_VALUE"), Str(r, "NT_USER") ?? "", Utc(r, "CHANGED_AT") ?? DateTime.UtcNow, Str(r, "CHANGE_REASON"));
    }, args);

    internal static string? Cut(string? s, int max) => string.IsNullOrEmpty(s) ? s : s.Length <= max ? s : s[..max];
}

/// <summary>IB_PROCESS_ALERTS: each handling is a new row (who, when, comment). Alerts themselves are calculated live.</summary>
public sealed partial class OracleAlertStore : IAlertAcknowledgementStore
{
    private readonly OracleDb _db;
    private readonly object _gate = new();
    private Dictionary<(int, AlertType), AlertAcknowledgement> _items = new();

    public OracleAlertStore(OracleDb db) { _db = db; Refresh(); }

    public static string Code(AlertType t) => SnakeRx().Replace(t.ToString(), "$1_$2").ToUpperInvariant();
    private static AlertType? TypeOf(string? code) =>
        Enum.GetValues<AlertType>().Cast<AlertType?>().FirstOrDefault(t => Code(t!.Value) == code);

    public void Refresh()
    {
        var rows = _db.Query($@"SELECT PALLET_RETURN_ID, ALERT_TYPE, HANDLED_BY, HANDLED_AT, HANDLED_COMMENT, SEVERITY, MESSAGE, RESPONSIBLE_AREA
                                  FROM {_db.T("IB_PROCESS_ALERTS")} WHERE HANDLED_AT IS NOT NULL ORDER BY HANDLED_AT", r =>
        {
            var type = TypeOf(Str(r, "ALERT_TYPE"));
            return type is null ? null : new AlertAcknowledgement(Int(r, "PALLET_RETURN_ID")!.Value, type.Value, Str(r, "HANDLED_BY") ?? "",
                Str(r, "HANDLED_COMMENT") ?? "", Utc(r, "HANDLED_AT") ?? DateTime.UtcNow,
                Str(r, "SEVERITY") switch { "CRITICAL" => Severity.Error, "WARNING" => Severity.Warning, _ => Severity.Info },
                Str(r, "MESSAGE"), Str(r, "RESPONSIBLE_AREA"));
        });
        var map = new Dictionary<(int, AlertType), AlertAcknowledgement>();
        foreach (var a in rows.OfType<AlertAcknowledgement>()) map[(a.PalletId, a.Type)] = a;     // latest wins
        lock (_gate) _items = map;
    }

    public void Acknowledge(AlertAcknowledgement ack)
    {
        if (string.IsNullOrWhiteSpace(ack.Comment)) throw new DataRuleException("The comment cannot be empty.");
        _db.InTransaction((c, tx) =>
        {
            using var cmd = _db.Command(c, $@"INSERT INTO {_db.T("IB_PROCESS_ALERTS")}
                (PALLET_RETURN_ID, ALERT_TYPE, SEVERITY, MESSAGE, RESPONSIBLE_AREA, HANDLED_BY, HANDLED_AT, HANDLED_COMMENT)
                VALUES (:id, :type, :sev, :msg, :area, :changedBy, SYSTIMESTAMP, :handledComment)", tx);
            cmd.Parameters.Add("id", ack.PalletId);
            cmd.Parameters.Add("type", Code(ack.Type));
            cmd.Parameters.Add("sev", ack.Severity switch { Severity.Error => "CRITICAL", Severity.Warning => "WARNING", _ => "INFO" });
            cmd.Parameters.Add("msg", OracleAuditTrail.Cut(string.IsNullOrWhiteSpace(ack.Message) ? ack.Type.ToString() : ack.Message, 400));
            cmd.Parameters.Add("area", Db(OracleAuditTrail.Cut(ack.ResponsibleArea, 30)));
            cmd.Parameters.Add("changedBy", ack.NtUser);
            cmd.Parameters.Add("handledComment", OracleAuditTrail.Cut(ack.Comment.Trim(), 500));
            cmd.ExecuteNonQuery();
        });
        lock (_gate) _items[(ack.PalletId, ack.Type)] = ack;
    }

    public AlertAcknowledgement? Find(int palletId, AlertType type) { lock (_gate) return _items.GetValueOrDefault((palletId, type)); }

    [GeneratedRegex("([a-z])([A-Z])")]
    private static partial Regex SnakeRx();
}

/// <summary>IB_PROCESS_FEEDBACK.</summary>
public sealed class OracleFeedbackStore : IFeedbackStore
{
    private readonly OracleDb _db;
    public OracleFeedbackStore(OracleDb db) => _db = db;
    public string Location => $"Oracle {_db.Server} · IB_PROCESS_FEEDBACK";

    public void Add(FeedbackItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Text)) throw new DataRuleException("Write a comment.");
        _db.InTransaction((c, tx) =>
        {
            using var cmd = _db.Command(c, $@"INSERT INTO {_db.T("IB_PROCESS_FEEDBACK")} (NT_USER, USER_FUNCTION, SCREEN, FEEDBACK_TYPE, FEEDBACK_TEXT)
                                              VALUES (:nt, :fn, :screen, :type, :text)", tx);
            cmd.Parameters.Add("nt", item.NtUser);
            cmd.Parameters.Add("fn", Db(OracleAuditTrail.Cut(item.Function, 10)));
            cmd.Parameters.Add("screen", Db(OracleAuditTrail.Cut(item.Screen, 60)));
            cmd.Parameters.Add("type", Db(OracleAuditTrail.Cut(item.Type, 40)));
            cmd.Parameters.Add("text", OracleAuditTrail.Cut(item.Text.Trim(), 2000));
            cmd.ExecuteNonQuery();
        });
    }
}

/// <summary>Calculation parameters in force today (IB_PROCESS_PARAMETERS); missing keys keep the default value.</summary>
public static class OracleParameters
{
    public static CalcParameters Load(OracleDb db)
    {
        var rows = db.Query($@"SELECT PARAM_KEY, NUMERIC_VALUE FROM {db.T("IB_PROCESS_PARAMETERS")}
                               WHERE VALID_FROM <= TRUNC(SYSDATE) AND (VALID_TO IS NULL OR VALID_TO >= TRUNC(SYSDATE))
                               ORDER BY VALID_FROM", r => (Key: Str(r, "PARAM_KEY")!, Value: Dec(r, "NUMERIC_VALUE")))
            .Where(x => x.Value is not null).GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.Last().Value!.Value);
        var d = CalcParameters.Default;
        int I(string k, int def) => rows.TryGetValue(k, out var v) ? (int)v : def;
        return new CalcParameters
        {
            DutyRate = rows.GetValueOrDefault("DUTY_RATE", d.DutyRate),
            ImmexLegalTermDays = I("IMMEX_LEGAL_TERM_DAYS", d.ImmexLegalTermDays),
            AlertWarningDaysLeft = I("ALERT_WARNING_DAYS_LEFT", d.AlertWarningDaysLeft),
            AlertCriticalDaysLeft = I("ALERT_CRITICAL_DAYS_LEFT", d.AlertCriticalDaysLeft),
            QtyTolerancePcs = I("QTY_TOLERANCE_PCS", d.QtyTolerancePcs),
            SlaReworkDays = I("INTERNAL_SLA_REWORK_DAYS", d.SlaReworkDays),
            SlaExportDays = I("INTERNAL_SLA_EXPORT_DAYS", d.SlaExportDays),
            SlaHseDays = I("INTERNAL_SLA_HSE_DAYS", d.SlaHseDays),
        };
    }
}

/// <summary>
/// IB_PROCESS_MM60_COSTS. The active price of each material is the row with VALID_TO NULL. A new MM60 file adds new
/// materials and, when a price changes, closes the old row (VALID_TO = yesterday) and inserts the new one; the same day
/// it updates the row in place. Materials not in the file keep their last price. Nothing is deleted.
/// </summary>
public sealed class OracleMaterialCostCatalog : IMaterialCostCatalog
{
    private sealed record Row(MaterialCost Cost, DateTime ValidFrom);

    private readonly OracleDb _db;
    private readonly object _gate = new();
    private Dictionary<string, Row> _items = new();

    public OracleMaterialCostCatalog(OracleDb db) { _db = db; Refresh(); }

    public int Count { get { lock (_gate) return _items.Count; } }
    public string? SourceName { get; private set; }
    public DateTime? LoadedAtUtc { get; private set; }

    private static string? Key(string? material) =>
        string.IsNullOrWhiteSpace(material) ? null : Identifiers.NormalizeMaterial(material.Trim().ToUpperInvariant());

    public MaterialCost? Find(string? material)
    {
        var key = Key(material);
        if (key is null) return null;
        lock (_gate) return _items.GetValueOrDefault(key)?.Cost;
    }

    public void Refresh()
    {
        var rows = _db.Query($@"SELECT MATERIAL, DESCRIPTION, PRICE, PRICE_UNIT, CURRENCY, VALID_FROM
                                  FROM {_db.T("IB_PROCESS_MM60_COSTS")} WHERE VALID_TO IS NULL", r =>
            new Row(new MaterialCost(Str(r, "MATERIAL")!, Str(r, "DESCRIPTION"), Dec(r, "PRICE") ?? 0, Dec(r, "PRICE_UNIT") ?? 1,
                                     (Str(r, "CURRENCY") ?? "USD").Trim()), Date(r, "VALID_FROM")!.Value));
        var map = new Dictionary<string, Row>();
        foreach (var r in rows.OrderBy(r => r.ValidFrom)) map[Key(r.Cost.Material) ?? r.Cost.Material] = r;
        lock (_gate) _items = map;
        var last = rows.Count == 0 ? (DateTime?)null : rows.Max(r => r.ValidFrom);
        SourceName = $"Oracle IB_PROCESS_MM60_COSTS" + (last is null ? "" : $" · last change {last:dd/MM/yyyy}");
        LoadedAtUtc = DateTime.UtcNow;
    }

    public void Replace(IEnumerable<MaterialCost> costs, string sourceName, CostSource source = CostSource.Mm60File)
    {
        var incoming = new Dictionary<string, MaterialCost>();
        foreach (var c in costs)
            if (Key(c.Material) is { Length: <= 40 } k && c.PriceUnit > 0) incoming[k] = c with { Material = k };
        Dictionary<string, Row> current;
        lock (_gate) current = new Dictionary<string, Row>(_items);

        var today = DateTime.Today;
        var inserts = new List<MaterialCost>();
        var sameDay = new List<MaterialCost>();
        var close = new List<string>();
        foreach (var (k, c) in incoming)
        {
            if (!current.TryGetValue(k, out var old)) { inserts.Add(c); continue; }
            var o = old.Cost;
            if (o.Price == c.Price && o.PriceUnit == Math.Round(c.PriceUnit) && string.Equals(o.Currency, Currency(c.Currency), StringComparison.OrdinalIgnoreCase)) continue;
            if (old.ValidFrom.Date >= today) sameDay.Add(c);
            else { close.Add(k); inserts.Add(c); }
        }
        var src = source == CostSource.Excel ? "EXCEL" : "MM60_FILE";
        _db.InTransaction((cn, tx) =>
        {
            if (close.Count > 0)
            {
                using var cmd = _db.Command(cn, $@"UPDATE {_db.T("IB_PROCESS_MM60_COSTS")} SET VALID_TO = TRUNC(SYSDATE) - 1
                                                    WHERE MATERIAL = :m AND VALID_TO IS NULL", tx);
                cmd.ArrayBindCount = close.Count;
                cmd.Parameters.Add("m", OracleDbType.Varchar2, close.ToArray(), System.Data.ParameterDirection.Input);
                cmd.ExecuteNonQuery();
            }
            if (sameDay.Count > 0)
            {
                using var cmd = _db.Command(cn, $@"UPDATE {_db.T("IB_PROCESS_MM60_COSTS")}
                       SET PRICE = :price, PRICE_UNIT = :unit, CURRENCY = :cur, DESCRIPTION = NVL(:descr, DESCRIPTION), SOURCE = :src
                     WHERE MATERIAL = :m AND VALID_TO IS NULL", tx);
                Bind(cmd, sameDay, src);
                cmd.ExecuteNonQuery();
            }
            if (inserts.Count > 0)
            {
                using var cmd = _db.Command(cn, $@"INSERT INTO {_db.T("IB_PROCESS_MM60_COSTS")} (MATERIAL, VALID_FROM, DESCRIPTION, PRICE, PRICE_UNIT, CURRENCY, SOURCE)
                                                    VALUES (:m, TRUNC(SYSDATE), :descr, :price, :unit, :cur, :src)", tx);
                Bind(cmd, inserts, src);
                cmd.ExecuteNonQuery();
            }
        });
        Refresh();
        SourceName = $"{sourceName} → Oracle ({inserts.Count - close.Count} new, {close.Count + sameDay.Count} price changes)";
    }

    private static string Currency(string? c) => string.IsNullOrWhiteSpace(c) ? "USD" : c.Trim().ToUpperInvariant() is var u && u.Length > 3 ? u[..3] : c.Trim().ToUpperInvariant();

    private static void Bind(OracleCommand cmd, List<MaterialCost> rows, string src)
    {
        cmd.ArrayBindCount = rows.Count;
        cmd.Parameters.Add("m", OracleDbType.Varchar2, rows.Select(r => r.Material).ToArray(), System.Data.ParameterDirection.Input);
        var descr = rows.Select(r => OracleAuditTrail.Cut(r.Description?.Trim(), 80)).ToArray();
        var p = cmd.Parameters.Add("descr", OracleDbType.Varchar2, descr.Select(d => d ?? "").ToArray(), System.Data.ParameterDirection.Input);
        p.ArrayBindStatus = descr.Select(d => string.IsNullOrEmpty(d) ? OracleParameterStatus.NullInsert : OracleParameterStatus.Success).ToArray();
        cmd.Parameters.Add("price", OracleDbType.Decimal, rows.Select(r => Math.Round(r.Price, 4)).ToArray(), System.Data.ParameterDirection.Input);
        cmd.Parameters.Add("unit", OracleDbType.Decimal, rows.Select(r => Math.Max(1, Math.Round(r.PriceUnit))).ToArray(), System.Data.ParameterDirection.Input);
        cmd.Parameters.Add("cur", OracleDbType.Char, rows.Select(r => Currency(r.Currency)).ToArray(), System.Data.ParameterDirection.Input);
        cmd.Parameters.Add("src", OracleDbType.Varchar2, rows.Select(_ => src).ToArray(), System.Data.ParameterDirection.Input);
    }
}

/// <summary>Catalogs read from the database so they are managed there, without a new executable.</summary>
public static class OracleCatalogs
{
    /// <summary>
    /// IB_PROCESS_DEPARTMENTS, every row (inactive ones too: historical records still use them). Order: process
    /// departments first, then cross-functional, each alphabetical.
    /// </summary>
    public static IReadOnlyList<Department> LoadDepartments(OracleDb db) => db.Query(
        $@"SELECT DEPARTMENT_CODE, AREA, CAN_REQUEST, DEPARTMENT_GROUP, IS_ACTIVE FROM {db.T("IB_PROCESS_DEPARTMENTS")}
            ORDER BY CASE DEPARTMENT_GROUP WHEN 'PROCESS' THEN 0 ELSE 1 END, DEPARTMENT_CODE",
        r => ToDepartment(Str(r, "DEPARTMENT_CODE")!, Str(r, "AREA")!, Str(r, "CAN_REQUEST"), Str(r, "DEPARTMENT_GROUP"), Str(r, "IS_ACTIVE")));

    public static Department ToDepartment(string code, string area, string? canRequest, string? group, string? isActive) =>
        new(code.Trim(), area.Trim(), canRequest == "Y",
            string.Equals(group?.Trim(), Department.CrossFunctional, StringComparison.OrdinalIgnoreCase) ? Department.CrossFunctional : Department.Process,
            isActive != "N");
}
