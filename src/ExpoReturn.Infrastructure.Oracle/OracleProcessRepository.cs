using System.Text.RegularExpressions;
using ExpoReturn.Application;
using ExpoReturn.Domain;
using Oracle.ManagedDataAccess.Client;
using static ExpoReturn.Infrastructure.Oracle.OracleDb;

namespace ExpoReturn.Infrastructure.Oracle;

/// <summary>
/// IB_PROCESS_PROCESSES (header) + IB_PROCESS_PALLETS. Nothing is kept in memory: every read goes to the database (the
/// inbox reads page by page through <see cref="OracleProcessQueries"/>; a process is read whole when it is opened), so
/// what other users saved is always what is shown. Every save goes to Oracle in one transaction, compared with the
/// version in the database at that moment: the header only if it changed, each pallet only if it changed, each with its own
/// ROW_VERSION (two areas can work on the same process at the same time). Pallets that are no longer in the process are
/// deleted; deleting a process deletes its pallets, status history and alerts (ON DELETE CASCADE).
/// </summary>
public sealed partial class OracleProcessRepository : IProcessRepository
{
    private sealed record HeaderCol(string Name, Func<IbProcess, object?> Get, Action<IbProcess, OracleDataReader> Set, int? MaxLength = null);
    private sealed record PalletCol(string Name, Func<PalletReturn, object?> Get, Action<PalletReturn, OracleDataReader> Set, int? MaxLength = null);

    private readonly OracleDb _db;
    private readonly CalcParameters _parameters;
    private readonly Func<DateTime> _today;
    private Dictionary<string, int> _categoryIds = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<int, string> _categoryCodes = new();
    private HashSet<string> _departments = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler? Changed;

    public OracleProcessRepository(OracleDb db, CalcParameters parameters, Func<DateTime> today)
    {
        _db = db;
        _parameters = parameters;
        _today = today;
        Refresh();
    }

    /// <summary>Property → column (audit log FIELD_NAME). Header and pallet columns have different names.</summary>
    public static IReadOnlyDictionary<string, string> ColumnOfProperty { get; } = new Dictionary<string, string>
    {
        [nameof(IbProcess.ValueStreamId)] = "VALUE_STREAM_ID", [nameof(IbProcess.FolioNumber)] = "FOLIO_NUMBER",
        [nameof(IbProcess.QmmResponsible)] = "QMM_RESPONSIBLE", [nameof(IbProcess.QmmResponsibleNtUser)] = "QMM_RESPONSIBLE_NT",
        [nameof(IbProcess.RequestingDepartment)] = "REQUESTING_DEPARTMENT", [nameof(IbProcess.RequestorName)] = "REQUESTOR_NAME",
        [nameof(IbProcess.RequestorNtUser)] = "REQUESTOR_NT", [nameof(IbProcess.ReasonId)] = "REASON_ID",
        [nameof(IbProcess.ReturnReasonDetail)] = "REASON_DETAIL", [nameof(IbProcess.ImportDate)] = "IMPORT_DATE",
        [nameof(IbProcess.ImportInvoice)] = "IMPORT_INVOICE", [nameof(IbProcess.ImportDateElpJup1)] = "ELP_JUP1_CROSSING_DATE",
        [nameof(IbProcess.DeliveryNumber)] = "DELIVERY_NUMBER",
        [nameof(PalletReturn.ReturnedMaterial)] = "RETURNED_PART_NUMBER", [nameof(PalletReturn.UnitCostUsd)] = "MM60_UNIT_COST_USD",
        [nameof(PalletReturn.QtyImported)] = "QTY_IMPORTED", [nameof(PalletReturn.OriginalHU)] = "ORIGINAL_HU",
        [nameof(PalletReturn.OriginalBatch)] = "ORIGINAL_BATCH", [nameof(PalletReturn.DateReturnedToWhs)] = "PO_DATE",
        [nameof(PalletReturn.ReturnPONumber)] = "PO_NUMBER", [nameof(PalletReturn.NewMaterial)] = "NEW_PART_NUMBER",
        [nameof(PalletReturn.PhysicalDeliveredQty)] = "PHYSICAL_DELIVERED_QTY", [nameof(PalletReturn.NewHU)] = "NEW_HU",
        [nameof(PalletReturn.ReworkBatch)] = "REWORK_BATCH", [nameof(PalletReturn.ReinsertionQty)] = "REINSERTION_QTY",
        [nameof(PalletReturn.DateSentBack)] = "SENT_BACK_DATE", [nameof(PalletReturn.OldMaterialDeliveredQty)] = "OLD_MATERIAL_DELIVERED_QTY",
        [nameof(PalletReturn.ScrapQty)] = "SCRAP_QTY", [nameof(PalletReturn.ScrapDate)] = "SCRAP_DATE",
        [nameof(PalletReturn.LowExportNumber)] = "LOW_EXPORT_NUMBER", [nameof(PalletReturn.LowExportDate)] = "LOW_EXPORT_DATE",
        [nameof(PalletReturn.CustomsExportReference)] = "CUSTOMS_EXPORT", [nameof(PalletReturn.ScrapGrNumber)] = "GR_NUMBER",
        [nameof(PalletReturn.HseScrapKg)] = "HSE_SCRAP_KG", [nameof(PalletReturn.HseConfirmationDate)] = "HSE_CONFIRMATION_DATE",
        [nameof(PalletReturn.StatusCategoryCode)] = "STATUS_CATEGORY_ID", [nameof(PalletReturn.Status)] = "WORKFLOW_STATUS_ID",
    };

    /// <summary>Columns of IB_PROCESS_PROCESSES written by INSERT / UPDATE (identity, virtual and control columns apart).</summary>
    private static List<HeaderCol> HeaderColumns() => new()
    {
        new("VALUE_STREAM_ID", p => p.ValueStreamId, (p, r) => p.ValueStreamId = Int(r, "VALUE_STREAM_ID") ?? 0),
        new("FOLIO_NUMBER", p => p.FolioNumber, (p, r) => p.FolioNumber = Int(r, "FOLIO_NUMBER") ?? 0),
        new("QMM_RESPONSIBLE", p => p.QmmResponsible, (p, r) => p.QmmResponsible = Str(r, "QMM_RESPONSIBLE"), 150),
        new("QMM_RESPONSIBLE_NT", p => p.QmmResponsibleNtUser, (p, r) => p.QmmResponsibleNtUser = Str(r, "QMM_RESPONSIBLE_NT"), 64),
        new("REQUESTING_DEPARTMENT", p => p.RequestingDepartment, (p, r) => p.RequestingDepartment = Str(r, "REQUESTING_DEPARTMENT"), 20),
        new("REQUESTOR_NAME", p => p.RequestorName, (p, r) => p.RequestorName = Str(r, "REQUESTOR_NAME"), 150),
        new("REQUESTOR_NT", p => p.RequestorNtUser, (p, r) => p.RequestorNtUser = Str(r, "REQUESTOR_NT"), 64),
        new("REASON_ID", p => p.ReasonId, (p, r) => p.ReasonId = Int(r, "REASON_ID")),
        new("REASON_DETAIL", p => p.ReturnReasonDetail, (p, r) => p.ReturnReasonDetail = Str(r, "REASON_DETAIL"), 500),
        new("IMPORT_DATE", p => p.ImportDate, (p, r) => p.ImportDate = Date(r, "IMPORT_DATE")),
        new("IMPORT_INVOICE", p => p.ImportInvoice, (p, r) => p.ImportInvoice = Str(r, "IMPORT_INVOICE"), 20),
        new("ELP_JUP1_CROSSING_DATE", p => p.ImportDateElpJup1, (p, r) => p.ImportDateElpJup1 = Date(r, "ELP_JUP1_CROSSING_DATE")),
        new("DELIVERY_NUMBER", p => p.DeliveryNumber, (p, r) => p.DeliveryNumber = Str(r, "DELIVERY_NUMBER"), 20),
        // Calculated by the application (inbox filter To homogenize).
        new("OPEN_FINDINGS", p => p.OpenFindings, (p, r) => p.OpenFindings = Int(r, "OPEN_FINDINGS") ?? 0),
    };

    /// <summary>Columns of IB_PROCESS_PALLETS written by INSERT / UPDATE.</summary>
    private static List<PalletCol> PalletColumns(Func<string?, object?> categoryId, Func<int, string?> categoryCode) => new()
    {
        new("LINE_NO", p => p.LineNo, (p, r) => p.LineNo = Int(r, "LINE_NO") ?? 1),
        new("RETURNED_PART_NUMBER", p => p.ReturnedMaterial, (p, r) => p.ReturnedMaterial = Str(r, "RETURNED_PART_NUMBER"), 18),
        new("MM60_UNIT_COST_USD", p => p.UnitCostUsd, (p, r) => p.UnitCostUsd = Dec(r, "MM60_UNIT_COST_USD")),
        new("UNIT_COST_SOURCE", p => p.UnitCostSource, (p, r) => p.UnitCostSource = Str(r, "UNIT_COST_SOURCE") ?? "PENDING", 10),
        new("QTY_IMPORTED", p => p.QtyImported, (p, r) => p.QtyImported = Int(r, "QTY_IMPORTED") ?? 0),
        new("ORIGINAL_HU", p => p.OriginalHU, (p, r) => p.OriginalHU = Str(r, "ORIGINAL_HU"), 20),
        new("ORIGINAL_BATCH", p => p.OriginalBatch, (p, r) => p.OriginalBatch = Str(r, "ORIGINAL_BATCH"), 10),
        new("PO_DATE", p => p.DateReturnedToWhs, (p, r) => p.DateReturnedToWhs = Date(r, "PO_DATE")),
        new("PO_NUMBER", p => p.ReturnPONumber, (p, r) => p.ReturnPONumber = Str(r, "PO_NUMBER"), 20),
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
        // Calculated by the application — stored so reports and SQL users see the same numbers.
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

    private List<PalletCol> PalletColumns() => PalletColumns(CategoryId, id => _categoryCodes.GetValueOrDefault(id));

    /// <summary>Every column INSERT / UPDATE write, per table (checked against 01_create_tables.sql by the tests).</summary>
    public static IReadOnlyList<string> WrittenProcessColumns { get; } = HeaderColumns().Select(c => c.Name)
        .Concat(new[] { "IS_LEGACY", "LEGACY_SHEET", "LEGACY_ROW", "CREATED_BY", "MODIFIED_BY", "MODIFIED_AT", "ROW_VERSION" }).ToList();
    public static IReadOnlyList<string> WrittenPalletColumns { get; } = PalletColumns(_ => null, _ => null).Select(c => c.Name)
        .Concat(new[] { "PROCESS_ID", "CALC_VERSION", "CALCULATED_AT", "IS_LEGACY", "CREATED_BY", "MODIFIED_BY", "MODIFIED_AT", "ROW_VERSION" }).ToList();

    /// <summary>Text lengths checked before saving (must equal the VARCHAR2 / NVARCHAR2 sizes of each table).</summary>
    public static IReadOnlyDictionary<string, int> ProcessMaxLengths { get; } =
        HeaderColumns().Where(c => c.MaxLength is not null).ToDictionary(c => c.Name, c => c.MaxLength!.Value);
    public static IReadOnlyDictionary<string, int> PalletMaxLengths { get; } =
        PalletColumns(_ => null, _ => null).Where(c => c.MaxLength is not null).ToDictionary(c => c.Name, c => c.MaxLength!.Value);

    private object? CategoryId(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : _categoryIds.TryGetValue(code.Trim(), out var id) ? id : null;

    private static string? Cut(string? s, int max) => string.IsNullOrEmpty(s) ? null : s.Length <= max ? s : s[..max];

    // ------------------------------------------------------------------ queries

    public IReadOnlyList<IbProcess> GetAll() => LoadWhere("1 = 1");
    public IbProcess? Get(int id) => LoadWhere("prc.PROCESS_ID = :id", ("id", id)).FirstOrDefault();

    public PalletReturn? GetPallet(int palletId) =>
        LoadWhere($"prc.PROCESS_ID = (SELECT x.PROCESS_ID FROM {_db.T("IB_PROCESS_PALLETS")} x WHERE x.PALLET_ID = :palletId)", ("palletId", palletId))
            .SelectMany(p => p.Pallets).FirstOrDefault(p => p.Id == palletId);

    public IReadOnlyDictionary<string, IReadOnlyList<string>> FoliosUsingPo(IReadOnlyCollection<string> poNumbers, int excludeProcessId)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var po in poNumbers.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            result[po] = _db.Query($@"SELECT DISTINCT prc.FOLIO_CODE FROM {_db.T("IB_PROCESS_PALLETS")} pal
                                        JOIN {_db.T("IB_PROCESS_PROCESSES")} prc ON prc.PROCESS_ID = pal.PROCESS_ID
                                       WHERE UPPER(TRIM(pal.PO_NUMBER)) = UPPER(TRIM(:po)) AND prc.PROCESS_ID <> :exclude
                                       ORDER BY prc.FOLIO_CODE", r => Str(r, "FOLIO_CODE")!, ("po", po.Trim()), ("exclude", excludeProcessId));
        return result;
    }

    public IReadOnlyList<IbProcess> WithPendingCost() =>
        LoadWhere($"EXISTS (SELECT 1 FROM {_db.T("IB_PROCESS_PALLETS")} x WHERE x.PROCESS_ID = prc.PROCESS_ID AND x.UNIT_COST_SOURCE = 'PENDING')");

    /// <summary>Catalogs used to translate ids (status categories, departments); the data itself is never cached.</summary>
    public void Refresh()
    {
        var cats = _db.Query($"SELECT STATUS_CATEGORY_ID, CODE FROM {_db.T("IB_PROCESS_STATUS_CATEGORIES")}",
            r => (Id: Int(r, "STATUS_CATEGORY_ID")!.Value, Code: Str(r, "CODE")!));
        _categoryIds = cats.ToDictionary(c => c.Code, c => c.Id, StringComparer.OrdinalIgnoreCase);
        _categoryCodes = cats.ToDictionary(c => c.Id, c => c.Code);
        _departments = _db.Query($"SELECT DEPARTMENT_CODE FROM {_db.T("IB_PROCESS_DEPARTMENTS")}", r => Str(r, "DEPARTMENT_CODE")!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Whole processes (header + pallets) whose header matches <paramref name="where"/> (alias <c>prc</c>). Financials,
    /// status and duty risk are recalculated with today's date, like the views; OPEN_FINDINGS keeps the stored value so a
    /// save can tell whether it changed.
    /// </summary>
    internal List<IbProcess> LoadWhere(string where, params (string Name, object? Value)[] args)
    {
        var header = HeaderColumns();
        var processes = _db.Query($"SELECT prc.* FROM {_db.T("IB_PROCESS_PROCESSES")} prc WHERE {where} ORDER BY prc.VALUE_STREAM_ID, prc.FOLIO_NUMBER", r =>
        {
            var p = new IbProcess { Id = Int(r, "PROCESS_ID")!.Value };
            foreach (var c in header) c.Set(p, r);
            p.IsLegacy = Flag(r, "IS_LEGACY");
            var sheet = Str(r, "LEGACY_SHEET");
            p.LegacySource = sheet is null ? null : $"{sheet}!row {Int(r, "LEGACY_ROW")}";
            p.CreatedBy = Str(r, "CREATED_BY") ?? "";
            p.CreatedAtUtc = Utc(r, "CREATED_AT") ?? DateTime.UtcNow;
            p.ModifiedBy = Str(r, "MODIFIED_BY");
            p.ModifiedAtUtc = Utc(r, "MODIFIED_AT");
            p.RowVersion = Int(r, "ROW_VERSION") ?? 0;
            return p;
        }, args);
        if (processes.Count == 0) return processes;
        var byId = processes.ToDictionary(p => p.Id);

        var cols = PalletColumns();
        foreach (var (processId, pallet) in _db.Query($@"SELECT pal.* FROM {_db.T("IB_PROCESS_PALLETS")} pal
                     WHERE pal.PROCESS_ID IN (SELECT prc.PROCESS_ID FROM {_db.T("IB_PROCESS_PROCESSES")} prc WHERE {where})
                     ORDER BY pal.PROCESS_ID, pal.LINE_NO", r =>
                 {
                     var p = new PalletReturn { Id = Int(r, "PALLET_ID")!.Value };
                     foreach (var c in cols) c.Set(p, r);
                     p.CreatedBy = Str(r, "CREATED_BY") ?? "";
                     p.CreatedAtUtc = Utc(r, "CREATED_AT") ?? DateTime.UtcNow;
                     p.ModifiedBy = Str(r, "MODIFIED_BY");
                     p.ModifiedAtUtc = Utc(r, "MODIFIED_AT");
                     p.RowVersion = Int(r, "ROW_VERSION") ?? 0;
                     return (Int(r, "PROCESS_ID")!.Value, p);
                 }, args))
            if (byId.TryGetValue(processId, out var process)) process.AddPallet(pallet);

        var today = _today();
        foreach (var p in processes)
        {
            foreach (var pallet in p.Pallets) PalletEvaluator.Apply(pallet, _parameters, today);
            p.RaiseTotals();
        }
        return processes;
    }

    /// <summary>
    /// Next process folio of the VS (IB_PROCESS_FOLIO_COUNTERS, never below the highest folio of the VS; 900000+ is
    /// reserved for Excel rows without a usable '#'). Committed on its own: a failed save leaves a gap, never a duplicate.
    /// </summary>
    public int NextFolio(int valueStreamId) => _db.InTransaction((c, tx) =>
    {
        using var cmd = _db.Command(c, $@"UPDATE {_db.T("IB_PROCESS_FOLIO_COUNTERS")}
               SET LAST_FOLIO = GREATEST(LAST_FOLIO, (SELECT NVL(MAX(FOLIO_NUMBER), 0) FROM {_db.T("IB_PROCESS_PROCESSES")}
                                                      WHERE VALUE_STREAM_ID = :vs AND FOLIO_NUMBER < 900000)) + 1
             WHERE VALUE_STREAM_ID = :vs
         RETURNING LAST_FOLIO INTO :folio", tx);
        cmd.Parameters.Add("vs", valueStreamId);
        var folio = cmd.Parameters.Add("folio", OracleDbType.Int32, System.Data.ParameterDirection.Output);
        if (cmd.ExecuteNonQuery() == 0) throw new DataRuleException($"VS{valueStreamId} has no folio counter (IB_PROCESS_FOLIO_COUNTERS).");
        return Convert.ToInt32(folio.Value.ToString());
    });

    // ------------------------------------------------------------------ commands

    public void Save(IbProcess process)
    {
        var before = process.Id == 0 ? null : Get(process.Id);              // the version in the database right now
        if (process.Id != 0 && before is null) throw new ConcurrencyException("The IB process was deleted by someone else. Reload the list.");
        var tooLong = TooLong(process);
        if (tooLong.Count > 0) throw new DataRuleException("Too long for the database: " + string.Join(", ", tooLong));

        var headerVersion = process.RowVersion;
        var palletVersions = process.Pallets.ToDictionary(p => p, p => p.RowVersion);
        try
        {
            _db.InTransaction((c, tx) =>
            {
                if (process.Id == 0) InsertHeader(c, tx, process);
                else if (ProcessService.DiffHeader(before, process).Any() || before!.OpenFindings != process.OpenFindings) UpdateHeader(c, tx, process);

                foreach (var gone in before?.Pallets.Where(o => process.Pallets.All(p => p.Id != o.Id)) ?? Enumerable.Empty<PalletReturn>())
                {
                    using var del = _db.Command(c, $"DELETE FROM {_db.T("IB_PROCESS_PALLETS")} WHERE PALLET_ID = :id", tx);
                    del.Parameters.Add("id", gone.Id);
                    del.ExecuteNonQuery();
                }
                foreach (var p in process.Pallets)
                {
                    var old = p.Id == 0 ? null : before?.Pallets.FirstOrDefault(o => o.Id == p.Id);
                    if (p.Id == 0) InsertPallet(c, tx, process, p);
                    else if (old is null || PalletChanged(old, p)) UpdatePallet(c, tx, p);
                    else continue;
                    if (old?.Status != p.Status)
                        InsertStatusHistory(c, tx, p.Id, old?.Status, p.Status, p.ModifiedBy ?? p.CreatedBy, old is null ? "Created" : "Automatic recalculation");
                }
            });
        }
        catch
        {
            // nothing was committed: the copy keeps its versions so the user can try again after reloading
            process.RowVersion = headerVersion;
            foreach (var (p, v) in palletVersions) p.RowVersion = v;
            if (before is null) { process.Id = 0; foreach (var p in process.Pallets) p.Id = 0; }
            else foreach (var p in process.Pallets.Where(p => before.Pallets.All(o => o.Id != p.Id))) p.Id = 0;
            throw;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Delete(int processId)
    {
        _db.InTransaction((c, tx) =>
        {
            using var cmd = _db.Command(c, $"DELETE FROM {_db.T("IB_PROCESS_PROCESSES")} WHERE PROCESS_ID = :id", tx);
            cmd.Parameters.Add("id", processId);
            if (cmd.ExecuteNonQuery() == 0) throw new ConcurrencyException("The IB process was already deleted. Reload the list.");
        });
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool PalletChanged(PalletReturn a, PalletReturn b) =>
        ProcessService.DiffPallet(a, b).Any()
        || a.LineNo != b.LineNo || a.Status != b.Status || a.IsDutyRisk != b.IsDutyRisk || a.Financials != b.Financials
        || a.LegalDueDate != b.LegalDueDate || a.UnitCostUsd != b.UnitCostUsd || a.UnitCostSource != b.UnitCostSource
        || a.DutyRiskReason != b.DutyRiskReason || a.DataQualityText != b.DataQualityText;

    private void InsertHeader(OracleConnection c, OracleTransaction tx, IbProcess p)
    {
        var cols = HeaderColumns();
        var (sheet, row) = SplitLegacy(p.LegacySource);
        var names = cols.Select(x => x.Name).Concat(new[] { "IS_LEGACY", "LEGACY_SHEET", "LEGACY_ROW", "CREATED_BY" });
        using var cmd = _db.Command(c, $@"INSERT INTO {_db.T("IB_PROCESS_PROCESSES")} ({string.Join(", ", names)})
            VALUES ({string.Join(", ", cols.Select((_, i) => $":h{i}"))}, :legacy, :sheet, :legacyRow, :createdBy)
            RETURNING PROCESS_ID INTO :newId", tx);
        for (var i = 0; i < cols.Count; i++) cmd.Parameters.Add($"h{i}", Db(cols[i].Get(p)));
        cmd.Parameters.Add("legacy", p.IsLegacy ? "Y" : "N");
        cmd.Parameters.Add("sheet", Db(sheet));
        cmd.Parameters.Add("legacyRow", Db(row));
        cmd.Parameters.Add("createdBy", string.IsNullOrWhiteSpace(p.CreatedBy) ? "SYSTEM" : p.CreatedBy);
        var id = cmd.Parameters.Add("newId", OracleDbType.Int32, System.Data.ParameterDirection.Output);
        cmd.ExecuteNonQuery();
        p.Id = Convert.ToInt32(id.Value.ToString());
        p.RowVersion = 0;
    }

    private void UpdateHeader(OracleConnection c, OracleTransaction tx, IbProcess p)
    {
        var cols = HeaderColumns();
        using var cmd = _db.Command(c, $@"UPDATE {_db.T("IB_PROCESS_PROCESSES")}
               SET {string.Join(", ", cols.Select((x, i) => $"{x.Name} = :h{i}"))},
                   MODIFIED_BY = :modifiedBy, MODIFIED_AT = SYSTIMESTAMP, ROW_VERSION = ROW_VERSION + 1
             WHERE PROCESS_ID = :id AND ROW_VERSION = :rowVersion", tx);
        for (var i = 0; i < cols.Count; i++) cmd.Parameters.Add($"h{i}", Db(cols[i].Get(p)));
        cmd.Parameters.Add("modifiedBy", string.IsNullOrWhiteSpace(p.ModifiedBy) ? "SYSTEM" : p.ModifiedBy);
        cmd.Parameters.Add("id", p.Id);
        cmd.Parameters.Add("rowVersion", p.RowVersion);
        if (cmd.ExecuteNonQuery() == 0)
            throw new ConcurrencyException($"{p.FolioCode} was changed by someone else after you opened it. Reload it and apply your changes again.");
        p.RowVersion++;
    }

    private void InsertPallet(OracleConnection c, OracleTransaction tx, IbProcess process, PalletReturn p)
    {
        var cols = PalletColumns();
        var names = cols.Select(x => x.Name).Concat(new[] { "PROCESS_ID", "CALC_VERSION", "CALCULATED_AT", "IS_LEGACY", "CREATED_BY" });
        using var cmd = _db.Command(c, $@"INSERT INTO {_db.T("IB_PROCESS_PALLETS")} ({string.Join(", ", names)})
            VALUES ({string.Join(", ", cols.Select((_, i) => $":p{i}"))}, :processId, 2, SYSTIMESTAMP, :legacy, :createdBy)
            RETURNING PALLET_ID INTO :newId", tx);
        for (var i = 0; i < cols.Count; i++) cmd.Parameters.Add($"p{i}", Db(cols[i].Get(p)));
        cmd.Parameters.Add("processId", process.Id);
        cmd.Parameters.Add("legacy", process.IsLegacy ? "Y" : "N");
        cmd.Parameters.Add("createdBy", string.IsNullOrWhiteSpace(p.CreatedBy) ? "SYSTEM" : p.CreatedBy);
        var id = cmd.Parameters.Add("newId", OracleDbType.Int32, System.Data.ParameterDirection.Output);
        cmd.ExecuteNonQuery();
        p.Id = Convert.ToInt32(id.Value.ToString());
        p.RowVersion = 0;
    }

    private void UpdatePallet(OracleConnection c, OracleTransaction tx, PalletReturn p)
    {
        var cols = PalletColumns();
        using var cmd = _db.Command(c, $@"UPDATE {_db.T("IB_PROCESS_PALLETS")}
               SET {string.Join(", ", cols.Select((x, i) => $"{x.Name} = :p{i}"))},
                   CALCULATED_AT = SYSTIMESTAMP, MODIFIED_BY = :modifiedBy, MODIFIED_AT = SYSTIMESTAMP, ROW_VERSION = ROW_VERSION + 1
             WHERE PALLET_ID = :id AND ROW_VERSION = :rowVersion", tx);
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
        using var cmd = _db.Command(c, $@"INSERT INTO {_db.T("IB_PROCESS_STATUS_HISTORY")} (PALLET_ID, FROM_STATUS_ID, TO_STATUS_ID, CHANGED_BY, NOTE)
                                          VALUES (:id, :fromId, :toId, :changedBy, :note)", tx);
        cmd.Parameters.Add("id", palletId);
        cmd.Parameters.Add("fromId", from is null ? DBNull.Value : (int)from.Value);
        cmd.Parameters.Add("toId", (int)to);
        cmd.Parameters.Add("changedBy", string.IsNullOrWhiteSpace(by) ? "SYSTEM" : by);
        cmd.Parameters.Add("note", note);
        cmd.ExecuteNonQuery();
    }

    // ------------------------------------------------------------------ Excel 2.0

    /// <summary>
    /// Excel 2.0 rows become working records: one process with one pallet each (IS_LEGACY = 'Y' marks the origin).
    /// Each sheet row is inserted once (LEGACY_SHEET + LEGACY_ROW), so a second import never overwrites edits. Values that
    /// do not fit are not invented: department / status category not in the catalogs go to NULL with a DQ flag, a repeated
    /// folio gets 900000 + row, a text longer than its column is kept whole in DATA_QUALITY_FLAGS.
    /// </summary>
    public LegacyAppendResult AppendLegacy(IEnumerable<IbProcess> processes)
    {
        var loaded = _db.Query($"SELECT LEGACY_SHEET, LEGACY_ROW FROM {_db.T("IB_PROCESS_PROCESSES")} WHERE LEGACY_SHEET IS NOT NULL",
            r => Key($"{Str(r, "LEGACY_SHEET")}!row {Int(r, "LEGACY_ROW")}")).ToHashSet();
        var folios = _db.Query($"SELECT VALUE_STREAM_ID, FOLIO_NUMBER FROM {_db.T("IB_PROCESS_PROCESSES")}",
            r => (Int(r, "VALUE_STREAM_ID")!.Value, Int(r, "FOLIO_NUMBER")!.Value)).ToHashSet();
        var today = _today();
        int added = 0, existing = 0;
        var failed = new List<string>();
        using var c = _db.Open();
        foreach (var process in processes)
        {
            if (process.LegacySource is null || loaded.Contains(Key(process.LegacySource))) { existing++; continue; }
            FitLegacy(process, folios);
            process.OpenFindings = PalletValidator.ValidateProcess(process, today).Count;
            var tooLong = TooLong(process);
            if (tooLong.Count > 0) { failed.Add($"{process.LegacySource}: too long for {string.Join(", ", tooLong)}"); continue; }
            using var tx = c.BeginTransaction();
            try
            {
                InsertHeader(c, tx, process);
                foreach (var p in process.Pallets)
                {
                    InsertPallet(c, tx, process, p);
                    InsertStatusHistory(c, tx, p.Id, null, p.Status, "MIGRATION", "Excel 2.0 history");
                }
                tx.Commit();
                added++;
                folios.Add((process.ValueStreamId, process.FolioNumber));
                loaded.Add(Key(process.LegacySource));
            }
            catch (OracleException ex)
            {
                tx.Rollback();
                process.Id = 0;
                foreach (var p in process.Pallets) p.Id = 0;
                failed.Add($"{process.LegacySource}: {Translate(ex).Message}");
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return new LegacyAppendResult(added, existing, failed);
    }

    private void FitLegacy(IbProcess process, HashSet<(int, int)> folios)
    {
        var first = process.Pallets.FirstOrDefault();
        void FlagFirst(string text) { first?.DataQualityFlags.Add(text); }
        foreach (var p in process.Pallets) KeepTooLongInFindings(p);
        if (process.RequestingDepartment is { } d && !_departments.Contains(d))
        {
            FlagFirst($"DQ-DEPT-NOT-IN-CATALOG:{d}");
            process.RequestingDepartment = null;
        }
        foreach (var p in process.Pallets)
        {
            if (p.StatusCategoryCode is { } s && !_categoryIds.ContainsKey(s.Trim()))
            {
                p.DataQualityFlags.Add($"DQ-COMMENTS-NOT-IN-LIST:{s}");
                p.StatusCategoryCode = null;
            }
            if (p.LowExportNumber is > 9999 or <= 0) { p.DataQualityFlags.Add($"DQ-LOWEXPORT-NUMBER:{p.LowExportNumber}"); p.LowExportNumber = null; }
        }
        if (folios.Contains((process.ValueStreamId, process.FolioNumber)))
        {
            FlagFirst($"DQ-FOLIO-REPEATED:{process.FolioNumber}");
            var (_, row) = SplitLegacy(process.LegacySource);
            process.FolioNumber = 900000 + (row ?? 0);
        }
        if (string.IsNullOrWhiteSpace(process.CreatedBy)) process.CreatedBy = "MIGRATION";
        foreach (var p in process.Pallets.Where(p => string.IsNullOrWhiteSpace(p.CreatedBy))) p.CreatedBy = "MIGRATION";
    }

    /// <summary>
    /// A cell longer than its column held a note, not the value (e.g. "En espera de disp, doc RT regular aduana" in
    /// Delivery Number): the field stays empty and the whole text is kept in the findings of the pallet. Header fields
    /// are checked through the pallet's process. Returns the columns moved.
    /// </summary>
    public static IReadOnlyList<string> KeepTooLongInFindings(PalletReturn p)
    {
        var moved = new List<string>();
        foreach (var (col, max) in ProcessMaxLengths.Concat(PalletMaxLengths))
        {
            if (PropertyOfColumn(col) is not { } prop) continue;
            var target = prop.DeclaringType == typeof(IbProcess) ? (object)p.Process : p;
            if (prop.GetValue(target) is not string text || text.Trim().Length <= max) continue;
            p.DataQualityFlags.Add($"DQ-TOO-LONG:{col}={text.Trim().Replace("; ", ", ").Replace("\r", " ").Replace("\n", " ")}");
            prop.SetValue(target, null);
            moved.Add(col);
        }
        return moved;
    }

    private static System.Reflection.PropertyInfo? PropertyOfColumn(string column)
    {
        var name = ColumnOfProperty.FirstOrDefault(kv => kv.Value == column).Key;
        if (name is null) return null;
        return Fields.Process.Contains(name) || Fields.Linked.ContainsKey(name)
            ? typeof(IbProcess).GetProperty(name) : typeof(PalletReturn).GetProperty(name);
    }

    private List<string> TooLong(IbProcess process) =>
        HeaderColumns().Where(c => c.MaxLength is { } max && c.Get(process) is string s && s.Trim().Length > max).Select(c => c.Name)
            .Concat(process.Pallets.SelectMany(p => PalletColumns()
                .Where(c => c.MaxLength is { } max && c.Get(p) is string s && s.Trim().Length > max).Select(c => $"{c.Name} (pallet {p.LineNo})")))
            .ToList();

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
