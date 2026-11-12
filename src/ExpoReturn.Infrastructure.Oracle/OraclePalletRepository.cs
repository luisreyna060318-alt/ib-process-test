using System.Text.RegularExpressions;
using ExpoReturn.Application;
using ExpoReturn.Domain;
using Oracle.ManagedDataAccess.Client;
using static ExpoReturn.Infrastructure.Oracle.OracleDb;

namespace ExpoReturn.Infrastructure.Oracle;

/// <summary>
/// IB_PROCESS_PALLET_RETURNS. The list is read once and kept in memory (Refresh reloads it); every save goes to Oracle
/// immediately: INSERT or UPDATE with ROW_VERSION (optimistic concurrency). There is no DELETE anywhere.
/// A calculated status change is also written to IB_PROCESS_STATUS_HISTORY.
/// </summary>
public sealed partial class OraclePalletRepository : IPalletReturnRepository
{
    private sealed record Col(string Name, Func<PalletReturn, object?> Get, Action<PalletReturn, OracleDataReader> Set, int? MaxLength = null);

    private readonly OracleDb _db;
    private readonly CalcParameters _parameters;
    private readonly Func<DateTime> _today;
    private readonly object _gate = new();
    private List<PalletReturn> _items = new();
    private Dictionary<string, int> _categoryIds = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<int, string> _categoryCodes = new();
    private HashSet<string> _departments = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler? Changed;

    public OraclePalletRepository(OracleDb db, CalcParameters parameters, Func<DateTime> today)
    {
        _db = db;
        _parameters = parameters;
        _today = today;
        Refresh();
    }

    /// <summary>Property → column (for the audit log FIELD_NAME).</summary>
    public static IReadOnlyDictionary<string, string> ColumnOfProperty { get; } = new Dictionary<string, string>
    {
        [nameof(PalletReturn.ValueStreamId)] = "VALUE_STREAM_ID", [nameof(PalletReturn.FolioNumber)] = "FOLIO_NUMBER",
        [nameof(PalletReturn.QmmResponsible)] = "QMM_RESPONSIBLE", [nameof(PalletReturn.RequestingDepartment)] = "REQUESTING_DEPARTMENT",
        [nameof(PalletReturn.RequestorName)] = "REQUESTOR_NAME", [nameof(PalletReturn.ReasonId)] = "REASON_ID",
        [nameof(PalletReturn.ReturnReasonDetail)] = "REASON_DETAIL", [nameof(PalletReturn.ReturnedMaterial)] = "RETURNED_PART_NUMBER",
        [nameof(PalletReturn.UnitCostUsd)] = "MM60_UNIT_COST_USD", [nameof(PalletReturn.QtyImported)] = "QTY_IMPORTED",
        [nameof(PalletReturn.OriginalHU)] = "ORIGINAL_HU", [nameof(PalletReturn.OriginalBatch)] = "ORIGINAL_BATCH",
        [nameof(PalletReturn.DateReturnedToWhs)] = "PO_DATE", [nameof(PalletReturn.ReturnPONumber)] = "PO_NUMBER",
        [nameof(PalletReturn.ImportDate)] = "IMPORT_DATE", [nameof(PalletReturn.ImportInvoice)] = "IMPORT_INVOICE",
        [nameof(PalletReturn.ImportDateElpJup1)] = "ELP_JUP1_CROSSING_DATE", [nameof(PalletReturn.DeliveryNumber)] = "DELIVERY_NUMBER",
        [nameof(PalletReturn.NewMaterial)] = "NEW_PART_NUMBER", [nameof(PalletReturn.PhysicalDeliveredQty)] = "PHYSICAL_DELIVERED_QTY",
        [nameof(PalletReturn.NewHU)] = "NEW_HU", [nameof(PalletReturn.ReworkBatch)] = "REWORK_BATCH",
        [nameof(PalletReturn.ReinsertionQty)] = "REINSERTION_QTY", [nameof(PalletReturn.DateSentBack)] = "SENT_BACK_DATE",
        [nameof(PalletReturn.OldMaterialDeliveredQty)] = "OLD_MATERIAL_DELIVERED_QTY", [nameof(PalletReturn.ScrapQty)] = "SCRAP_QTY",
        [nameof(PalletReturn.ScrapDate)] = "SCRAP_DATE", [nameof(PalletReturn.LowExportNumber)] = "LOW_EXPORT_NUMBER",
        [nameof(PalletReturn.LowExportDate)] = "LOW_EXPORT_DATE", [nameof(PalletReturn.CustomsExportReference)] = "CUSTOMS_EXPORT",
        [nameof(PalletReturn.ScrapGrNumber)] = "GR_NUMBER", [nameof(PalletReturn.HseScrapKg)] = "HSE_SCRAP_KG",
        [nameof(PalletReturn.HseConfirmationDate)] = "HSE_CONFIRMATION_DATE", [nameof(PalletReturn.StatusCategoryCode)] = "STATUS_CATEGORY_ID",
        [nameof(PalletReturn.Status)] = "WORKFLOW_STATUS_ID",
    };

    // Captured + calculated columns written on INSERT / UPDATE (identity, virtual and control columns are handled apart).
    private List<Col> Columns() => BuildColumns(CategoryId, id => _categoryCodes.GetValueOrDefault(id));

    /// <summary>Every column INSERT / UPDATE write (checked against 01_create_tables.sql by the tests).</summary>
    public static IReadOnlyList<string> WrittenColumns { get; } =
        BuildColumns(_ => null, _ => null).Select(c => c.Name)
            .Concat(new[] { "CALC_VERSION", "CALCULATED_AT", "IS_LEGACY", "LEGACY_SHEET", "LEGACY_ROW", "CREATED_BY", "MODIFIED_BY", "MODIFIED_AT", "ROW_VERSION" })
            .ToList();

    /// <summary>Text lengths checked before saving (must equal the VARCHAR2 / NVARCHAR2 sizes of the table).</summary>
    public static IReadOnlyDictionary<string, int> MaxLengths { get; } =
        BuildColumns(_ => null, _ => null).Where(c => c.MaxLength is not null).ToDictionary(c => c.Name, c => c.MaxLength!.Value);

    private static List<Col> BuildColumns(Func<string?, object?> categoryId, Func<int, string?> categoryCode) => new()
    {
        new("VALUE_STREAM_ID", p => p.ValueStreamId, (p, r) => p.ValueStreamId = Int(r, "VALUE_STREAM_ID") ?? 0),
        new("FOLIO_NUMBER", p => p.FolioNumber, (p, r) => p.FolioNumber = Int(r, "FOLIO_NUMBER") ?? 0),
        new("QMM_RESPONSIBLE", p => p.QmmResponsible, (p, r) => p.QmmResponsible = Str(r, "QMM_RESPONSIBLE"), 150),
        new("REQUESTING_DEPARTMENT", p => p.RequestingDepartment, (p, r) => p.RequestingDepartment = Str(r, "REQUESTING_DEPARTMENT"), 20),
        new("REQUESTOR_NAME", p => p.RequestorName, (p, r) => p.RequestorName = Str(r, "REQUESTOR_NAME"), 150),
        new("REASON_ID", p => p.ReasonId, (p, r) => p.ReasonId = Int(r, "REASON_ID")),
        new("REASON_DETAIL", p => p.ReturnReasonDetail, (p, r) => p.ReturnReasonDetail = Str(r, "REASON_DETAIL"), 500),
        new("RETURNED_PART_NUMBER", p => p.ReturnedMaterial, (p, r) => p.ReturnedMaterial = Str(r, "RETURNED_PART_NUMBER"), 18),
        new("MM60_UNIT_COST_USD", p => p.UnitCostUsd, (p, r) => p.UnitCostUsd = Dec(r, "MM60_UNIT_COST_USD")),
        new("UNIT_COST_SOURCE", p => p.UnitCostSource, (p, r) => p.UnitCostSource = Str(r, "UNIT_COST_SOURCE") ?? "PENDING", 10),
        new("QTY_IMPORTED", p => p.QtyImported, (p, r) => p.QtyImported = Int(r, "QTY_IMPORTED") ?? 0),
        new("ORIGINAL_HU", p => p.OriginalHU, (p, r) => p.OriginalHU = Str(r, "ORIGINAL_HU"), 20),
        new("ORIGINAL_BATCH", p => p.OriginalBatch, (p, r) => p.OriginalBatch = Str(r, "ORIGINAL_BATCH"), 10),
        new("PO_DATE", p => p.DateReturnedToWhs, (p, r) => p.DateReturnedToWhs = Date(r, "PO_DATE")),
        new("PO_NUMBER", p => p.ReturnPONumber, (p, r) => p.ReturnPONumber = Str(r, "PO_NUMBER"), 20),
        new("IMPORT_DATE", p => p.ImportDate, (p, r) => p.ImportDate = Date(r, "IMPORT_DATE")),
        new("IMPORT_INVOICE", p => p.ImportInvoice, (p, r) => p.ImportInvoice = Str(r, "IMPORT_INVOICE"), 20),
        new("ELP_JUP1_CROSSING_DATE", p => p.ImportDateElpJup1, (p, r) => p.ImportDateElpJup1 = Date(r, "ELP_JUP1_CROSSING_DATE")),
        new("DELIVERY_NUMBER", p => p.DeliveryNumber, (p, r) => p.DeliveryNumber = Str(r, "DELIVERY_NUMBER"), 20),
        new("NEW_PART_NUMBER", p => p.NewMaterial, (p, r) => p.NewMaterial = Str(r, "NEW_PART_NUMBER"), 18),
        new("PHYSICAL_DELIVERED_QTY", p => p.PhysicalDeliveredQty, (p, r) => p.PhysicalDeliveredQty = Int(r, "PHYSICAL_DELIVERED_QTY")),
        new("NEW_HU", p => p.NewHU, (p, r) => p.NewHU = Str(r, "NEW_HU"), 20),
        new("REWORK_BATCH", p => p.ReworkBatch, (p, r) => p.ReworkBatch = Str(r, "REWORK_BATCH"), 10),
        new("REINSERTION_QTY", p => p.ReinsertionQty, (p, r) => p.ReinsertionQty = Int(r, "REINSERTION_QTY")),
        new("SENT_BACK_DATE", p => p.DateSentBack, (p, r) => p.DateSentBack = Date(r, "SENT_BACK_DATE")),
        new("OLD_MATERIAL_DELIVERED_QTY", p => p.OldMaterialDeliveredQty, (p, r) => p.OldMaterialDeliveredQty = Int(r, "OLD_MATERIAL_DELIVERED_QTY")),
        new("SCRAP_QTY", p => p.ScrapQty, (p, r) => p.ScrapQty = Int(r, "SCRAP_QTY")),
        new("SCRAP_DATE", p => p.ScrapDate, (p, r) => p.ScrapDate = Date(r, "SCRAP_DATE")),
        new("LOW_EXPORT_NUMBER", p => p.LowExportNumber, (p, r) => p.LowExportNumber = Int(r, "LOW_EXPORT_NUMBER")),
        new("LOW_EXPORT_DATE", p => p.LowExportDate, (p, r) => p.LowExportDate = Date(r, "LOW_EXPORT_DATE")),
        new("CUSTOMS_EXPORT", p => p.CustomsExportReference, (p, r) => p.CustomsExportReference = Str(r, "CUSTOMS_EXPORT"), 30),
        new("GR_NUMBER", p => p.ScrapGrNumber, (p, r) => p.ScrapGrNumber = Str(r, "GR_NUMBER"), 10),
        new("HSE_SCRAP_KG", p => p.HseScrapKg, (p, r) => p.HseScrapKg = Dec(r, "HSE_SCRAP_KG")),
        new("HSE_CONFIRMATION_DATE", p => p.HseConfirmationDate, (p, r) => p.HseConfirmationDate = Date(r, "HSE_CONFIRMATION_DATE")),
        new("STATUS_CATEGORY_ID", p => categoryId(p.StatusCategoryCode),
            (p, r) => p.StatusCategoryCode = Int(r, "STATUS_CATEGORY_ID") is { } id ? categoryCode(id) : null),
        // Calculated by the application (Stage 5) — stored so reports and SQL users see the same numbers.
        new("QTY_OPEN_FLOOR", p => p.Financials.QtyOpenFloor, (_, _) => { }),
        new("QTY_PENDING_EXPORT", p => p.Financials.QtyPendingExport, (_, _) => { }),
        new("QTY_PENDING_HSE", p => p.Financials.QtyPendingHse, (_, _) => { }),
        new("QTY_MISSING", p => p.Financials.QtyMissing, (_, _) => { }),
        new("MFO_INVENTORY_USD", p => p.Financials.MfoInventoryUsd, (_, _) => { }),
        new("HSE_SCRAP_USD", p => p.Financials.HseScrapUsd, (_, _) => { }),
        new("MFO_LOW_FAILURE_USD", p => p.Financials.MfoLowFailureUsd, (_, _) => { }),
        new("GENERAL_BALANCE_USD", p => p.Financials.BalanceGeneralUsd, (_, _) => { }),
        new("DUTIES_TO_PAY_USD", p => p.Financials.DutiesToPayUsd, (_, _) => { }),
        new("WORKFLOW_STATUS_ID", p => (int)p.Status, (p, r) => p.Status = (WorkflowStatus)(Int(r, "WORKFLOW_STATUS_ID") ?? 1)),
        new("IS_DUTY_RISK", p => p.IsDutyRisk, (p, r) => p.IsDutyRisk = Flag(r, "IS_DUTY_RISK")),
        new("DUTY_RISK_REASON", p => Cut(p.DutyRiskReason, 200), (p, r) => p.DutyRiskReason = Str(r, "DUTY_RISK_REASON")),
        new("LEGAL_DUE_DATE", p => p.LegalDueDate, (p, r) => p.LegalDueDate = Date(r, "LEGAL_DUE_DATE")),
        new("DATA_QUALITY_FLAGS", p => Cut(p.DataQualityText, 400),
            (p, r) => p.DataQualityFlags = (Str(r, "DATA_QUALITY_FLAGS") ?? "").Split("; ", StringSplitOptions.RemoveEmptyEntries).ToList()),
    };

    private object? CategoryId(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : _categoryIds.TryGetValue(code.Trim(), out var id) ? id : null;

    private static string? Cut(string? s, int max) => string.IsNullOrEmpty(s) ? null : s.Length <= max ? s : s[..max];

    // ------------------------------------------------------------------ queries

    public IReadOnlyList<PalletReturn> GetAll() { lock (_gate) return _items.ToList(); }
    public PalletReturn? Get(int id) { lock (_gate) return _items.FirstOrDefault(p => p.Id == id); }

    public void Refresh()
    {
        var cats = _db.Query($"SELECT STATUS_CATEGORY_ID, CODE FROM {_db.T("IB_PROCESS_STATUS_CATEGORIES")}",
            r => (Id: Int(r, "STATUS_CATEGORY_ID")!.Value, Code: Str(r, "CODE")!));
        _categoryIds = cats.ToDictionary(c => c.Code, c => c.Id, StringComparer.OrdinalIgnoreCase);
        _categoryCodes = cats.ToDictionary(c => c.Id, c => c.Code);
        _departments = _db.Query($"SELECT DEPARTMENT_CODE FROM {_db.T("IB_PROCESS_DEPARTMENTS")}", r => Str(r, "DEPARTMENT_CODE")!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var cols = Columns();
        var today = _today();
        var list = _db.Query($"SELECT * FROM {_db.T("IB_PROCESS_PALLET_RETURNS")} ORDER BY VALUE_STREAM_ID, FOLIO_NUMBER", r =>
        {
            var p = new PalletReturn { Id = Int(r, "PALLET_RETURN_ID")!.Value };
            foreach (var c in cols) c.Set(p, r);
            p.IsLegacy = Flag(r, "IS_LEGACY");
            var sheet = Str(r, "LEGACY_SHEET");
            var row = Int(r, "LEGACY_ROW");
            p.LegacySource = sheet is null ? null : $"{sheet}!row {row}";
            p.CreatedBy = Str(r, "CREATED_BY") ?? "";
            p.CreatedAtUtc = Utc(r, "CREATED_AT") ?? DateTime.UtcNow;
            p.ModifiedBy = Str(r, "MODIFIED_BY");
            p.ModifiedAtUtc = Utc(r, "MODIFIED_AT");
            p.RowVersion = Int(r, "ROW_VERSION") ?? 0;
            return p;
        });
        // Financials and status depend on today (aging, duty risk): recalculated on load like the Excel formulas.
        foreach (var p in list) PalletEvaluator.Apply(p, _parameters, today);
        lock (_gate) _items = list;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Next folio of the VS from IB_PROCESS_FOLIO_COUNTERS, never below the highest folio of the VS (imported Excel rows
    /// included; 900000+ is reserved for Excel rows without a usable '#'). Committed on its own: a save that fails
    /// afterwards leaves a gap, never a duplicate.
    /// </summary>
    public int NextFolio(int valueStreamId) => _db.InTransaction((c, tx) =>
    {
        using var cmd = _db.Command(c, $@"UPDATE {_db.T("IB_PROCESS_FOLIO_COUNTERS")}
               SET LAST_FOLIO = GREATEST(LAST_FOLIO, (SELECT NVL(MAX(FOLIO_NUMBER), 0) FROM {_db.T("IB_PROCESS_PALLET_RETURNS")}
                                                      WHERE VALUE_STREAM_ID = :vs AND FOLIO_NUMBER < 900000)) + 1
             WHERE VALUE_STREAM_ID = :vs
         RETURNING LAST_FOLIO INTO :folio", tx);
        cmd.Parameters.Add("vs", valueStreamId);
        var folio = cmd.Parameters.Add("folio", OracleDbType.Int32, System.Data.ParameterDirection.Output);
        if (cmd.ExecuteNonQuery() == 0) throw new DataRuleException($"VS{valueStreamId} has no folio counter (IB_PROCESS_FOLIO_COUNTERS).");
        return Convert.ToInt32(folio.Value.ToString());
    });

    // ------------------------------------------------------------------ commands

    public void Save(PalletReturn pallet)
    {
        PalletReturn? before;
        lock (_gate) before = _items.FirstOrDefault(p => p.Id == pallet.Id);
        if (pallet.Id != 0 && before is null) throw new ConcurrencyException("The record is not in the list any more. Reload and try again.");
        CheckLengths(pallet, out var tooLong);
        if (tooLong.Count > 0) throw new DataRuleException("Too long for the database: " + string.Join(", ", tooLong));

        _db.InTransaction((c, tx) =>
        {
            if (pallet.Id == 0) Insert(c, tx, pallet);
            else Update(c, tx, pallet);
            if (before?.Status != pallet.Status)
                InsertStatusHistory(c, tx, pallet.Id, before?.Status, pallet.Status, pallet.ModifiedBy ?? pallet.CreatedBy,
                    before is null ? "Created" : "Automatic recalculation");
        });
        lock (_gate)
        {
            var i = _items.FindIndex(p => p.Id == pallet.Id);
            if (i >= 0) _items[i] = pallet; else _items.Add(pallet);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Insert(OracleConnection c, OracleTransaction tx, PalletReturn p)
    {
        var cols = Columns();
        var (sheet, row) = SplitLegacy(p.LegacySource);
        var names = cols.Select(x => x.Name).Concat(new[] { "CALC_VERSION", "CALCULATED_AT", "IS_LEGACY", "LEGACY_SHEET", "LEGACY_ROW", "CREATED_BY" }).ToList();
        var sql = $@"INSERT INTO {_db.T("IB_PROCESS_PALLET_RETURNS")} ({string.Join(", ", names)})
                     VALUES ({string.Join(", ", cols.Select((_, i) => $":p{i}"))}, 2, SYSTIMESTAMP, :legacy, :sheet, :legacyRow, :createdBy)
                     RETURNING PALLET_RETURN_ID INTO :newId";
        using var cmd = _db.Command(c, sql, tx);
        for (var i = 0; i < cols.Count; i++) cmd.Parameters.Add($"p{i}", Db(cols[i].Get(p)));
        cmd.Parameters.Add("legacy", p.IsLegacy ? "Y" : "N");
        cmd.Parameters.Add("sheet", Db(sheet));
        cmd.Parameters.Add("legacyRow", Db(row));
        cmd.Parameters.Add("createdBy", string.IsNullOrWhiteSpace(p.CreatedBy) ? "SYSTEM" : p.CreatedBy);
        var id = cmd.Parameters.Add("newId", OracleDbType.Int32, System.Data.ParameterDirection.Output);
        cmd.ExecuteNonQuery();
        p.Id = Convert.ToInt32(id.Value.ToString());
        p.RowVersion = 0;
    }

    private void Update(OracleConnection c, OracleTransaction tx, PalletReturn p)
    {
        var cols = Columns();
        var sql = $@"UPDATE {_db.T("IB_PROCESS_PALLET_RETURNS")}
                        SET {string.Join(", ", cols.Select((x, i) => $"{x.Name} = :p{i}"))},
                            CALCULATED_AT = SYSTIMESTAMP, MODIFIED_BY = :modifiedBy, MODIFIED_AT = SYSTIMESTAMP,
                            ROW_VERSION = ROW_VERSION + 1
                      WHERE PALLET_RETURN_ID = :id AND ROW_VERSION = :rowVersion";
        using var cmd = _db.Command(c, sql, tx);
        for (var i = 0; i < cols.Count; i++) cmd.Parameters.Add($"p{i}", Db(cols[i].Get(p)));
        cmd.Parameters.Add("modifiedBy", string.IsNullOrWhiteSpace(p.ModifiedBy) ? "SYSTEM" : p.ModifiedBy);
        cmd.Parameters.Add("id", p.Id);
        cmd.Parameters.Add("rowVersion", p.RowVersion);
        if (cmd.ExecuteNonQuery() == 0)
            throw new ConcurrencyException($"{p.FolioCode} was changed by someone else after you opened it. Reload it and apply your changes again.");
        p.RowVersion++;
    }

    private void InsertStatusHistory(OracleConnection c, OracleTransaction tx, int palletId, WorkflowStatus? from, WorkflowStatus to, string by, string note)
    {
        using var cmd = _db.Command(c, $@"INSERT INTO {_db.T("IB_PROCESS_STATUS_HISTORY")} (PALLET_RETURN_ID, FROM_STATUS_ID, TO_STATUS_ID, CHANGED_BY, NOTE)
                                          VALUES (:id, :fromId, :toId, :changedBy, :note)", tx);
        cmd.Parameters.Add("id", palletId);
        cmd.Parameters.Add("fromId", from is null ? DBNull.Value : (int)from.Value);
        cmd.Parameters.Add("toId", (int)to);
        cmd.Parameters.Add("changedBy", string.IsNullOrWhiteSpace(by) ? "SYSTEM" : by);
        cmd.Parameters.Add("note", note);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Excel 2.0 rows become working records (IS_LEGACY = 'Y' only marks the origin; they can be edited to homogenize
    /// the data). Each sheet row is inserted once (LEGACY_SHEET + LEGACY_ROW), so a second import never overwrites edits.
    /// A text longer than its column (a note typed in a value cell) is kept whole in DATA_QUALITY_FLAGS. Values that do not fit the schema
    /// are not invented: department / status category not in the catalogs go to NULL with a DQ flag that keeps the text;
    /// a repeated folio gets 900000 + row. Rows the database still refuses are reported, the rest are committed.
    /// </summary>
    public LegacyAppendResult AppendLegacy(IEnumerable<PalletReturn> pallets)
    {
        HashSet<string> loaded;
        HashSet<(int, int)> folios;
        lock (_gate)
        {
            loaded = _items.Where(p => p.LegacySource is not null).Select(p => Key(p.LegacySource!)).ToHashSet();
            folios = _items.Select(p => (p.ValueStreamId, p.FolioNumber)).ToHashSet();
        }
        int added = 0, existing = 0;
        var failed = new List<string>();
        using var c = _db.Open();
        foreach (var p in pallets)
        {
            if (p.LegacySource is null || loaded.Contains(Key(p.LegacySource))) { existing++; continue; }
            FitLegacy(p, folios);
            CheckLengths(p, out var tooLong);
            if (tooLong.Count > 0) { failed.Add($"{p.LegacySource}: too long for {string.Join(", ", tooLong)}"); continue; }
            using var tx = c.BeginTransaction();
            try
            {
                Insert(c, tx, p);
                InsertStatusHistory(c, tx, p.Id, null, p.Status, "MIGRATION", "Excel 2.0 history");
                tx.Commit();
                added++;
                folios.Add((p.ValueStreamId, p.FolioNumber));
                loaded.Add(Key(p.LegacySource));
            }
            catch (OracleException ex)
            {
                tx.Rollback();
                p.Id = 0;
                failed.Add($"{p.LegacySource}: {Translate(ex).Message}");
            }
        }
        Refresh();
        return new LegacyAppendResult(added, existing, failed);
    }

    private void FitLegacy(PalletReturn p, HashSet<(int, int)> folios)
    {
        KeepTooLongInFindings(p);
        if (p.RequestingDepartment is { } d && !_departments.Contains(d))
        {
            p.DataQualityFlags.Add($"DQ-DEPT-NOT-IN-CATALOG:{d}");
            p.RequestingDepartment = null;
        }
        if (p.StatusCategoryCode is { } s && !_categoryIds.ContainsKey(s.Trim()))
        {
            p.DataQualityFlags.Add($"DQ-COMMENTS-NOT-IN-LIST:{s}");
            p.StatusCategoryCode = null;
        }
        if (p.LowExportNumber is > 9999 or <= 0) { p.DataQualityFlags.Add($"DQ-LOWEXPORT-NUMBER:{p.LowExportNumber}"); p.LowExportNumber = null; }
        if (folios.Contains((p.ValueStreamId, p.FolioNumber)))
        {
            p.DataQualityFlags.Add($"DQ-FOLIO-REPEATED:{p.FolioNumber}");
            var (_, row) = SplitLegacy(p.LegacySource);
            p.FolioNumber = 900000 + (row ?? 0);
        }
        if (string.IsNullOrWhiteSpace(p.CreatedBy)) p.CreatedBy = "MIGRATION";
    }

    /// <summary>
    /// A cell longer than its column held a note, not the value (e.g. "En espera de disp, doc RT regular aduana" in
    /// Delivery Number): the field stays empty and the whole text is kept in the findings, to homogenize later.
    /// Returns the columns moved.
    /// </summary>
    public static IReadOnlyList<string> KeepTooLongInFindings(PalletReturn p)
    {
        var moved = new List<string>();
        foreach (var (col, max) in MaxLengths)
        {
            if (PropertyOfColumn(col) is not { } prop || prop.GetValue(p) is not string text || text.Trim().Length <= max) continue;
            p.DataQualityFlags.Add($"DQ-TOO-LONG:{col}={text.Trim().Replace("; ", ", ").Replace("\r", " ").Replace("\n", " ")}");
            prop.SetValue(p, null);
            moved.Add(col);
        }
        return moved;
    }

    private static System.Reflection.PropertyInfo? PropertyOfColumn(string column) =>
        ColumnOfProperty.FirstOrDefault(kv => kv.Value == column).Key is { } name ? typeof(PalletReturn).GetProperty(name) : null;

    private void CheckLengths(PalletReturn p, out List<string> tooLong)
    {
        tooLong = Columns().Where(c => c.MaxLength is { } max && c.Get(p) is string s && s.Trim().Length > max).Select(c => c.Name).ToList();
    }

    private static string Key(string legacySource) => legacySource.Trim().ToUpperInvariant();

    public static (string? Sheet, int? Row) SplitLegacy(string? legacySource)
    {
        if (string.IsNullOrWhiteSpace(legacySource)) return (null, null);
        var m = LegacyRx().Match(legacySource);
        return m.Success ? (m.Groups[1].Value.Trim(), int.Parse(m.Groups[2].Value)) : (legacySource.Length > 31 ? legacySource[..31] : legacySource, null);
    }

    [GeneratedRegex(@"^(.*)!\D*(\d+)\s*$")]
    private static partial Regex LegacyRx();
}
