using System.Text.Json;
using ExpoReturn.Application;
using ExpoReturn.Domain;
using ExpoReturn.Infrastructure.InMemory;
using ExpoReturn.Infrastructure.Oracle;
using Xunit;

namespace ExpoReturn.Domain.Tests;

/// <summary>
/// Only when EXPORETURN_SQL_PARITY_DIR is set: writes the stored rows (calculated on a SAVE date) and what the
/// application calculates on a later QUERY date, plus the SQL of the inbox filters. tools/database/check_views_parity.py
/// runs 05_views.sql on that data (DuckDB, via sqlglot) and compares: the views must give what the application gives.
/// </summary>
public class SqlParityDump
{
    private sealed class Clock(DateTime today) : IClock
    {
        public DateTime Today { get; set; } = today;
        public DateTime UtcNow => Today.ToUniversalTime();
    }

    private static string? D(DateTime? d) => d?.ToString("yyyy-MM-dd");

    [Fact]
    public void Dump_data_and_expected_results_for_the_view_parity_check()
    {
        var dir = Environment.GetEnvironmentVariable("EXPORETURN_SQL_PARITY_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        var saveDate = new DateTime(2026, 3, 1);
        var queryDate = new DateTime(2026, 11, 17);
        var repo = new InMemoryProcessRepository();
        var clock = new Clock(saveDate);
        SampleData.Seed(repo, new InMemoryAuditTrail(), new UserDirectory((string?)null, Array.Empty<string>(), includeDemoUsers: true), clock);
        var xlsx = Environment.GetEnvironmentVariable("EXPORETURN_LEGACY_XLSX");
        if (!string.IsNullOrWhiteSpace(xlsx) && File.Exists(xlsx))
        {
            var imported = LegacyExcelImporter.Import(xlsx, saveDate).Processes;
            foreach (var p in imported) PalletEvaluator.Apply(p, CalcParameters.Default, saveDate);
            repo.AppendLegacy(imported);
        }
        // Stored values = calculated on the save date (as in the database after the last save).
        foreach (var p in repo.GetAll()) PalletEvaluator.Apply(p, CalcParameters.Default, saveDate);
        var cats = Catalogs.StatusCategories.Select((c, i) => (Id: i + 1, c)).ToList();
        int? CatId(string? code) => cats.FirstOrDefault(x => string.Equals(x.c.Code, code, StringComparison.OrdinalIgnoreCase)).Id is var id and > 0 ? id : null;

        var data = new
        {
            SaveDate = D(saveDate), QueryDate = D(queryDate),
            Categories = cats.Select(x => new { STATUS_CATEGORY_ID = x.Id, CODE = x.c.Code, AFFECTS_DUTY = x.c.AffectsDuty ? "Y" : "N" }).ToList(),
            Processes = repo.GetAll().Select(p => new
            {
                PROCESS_ID = p.Id, VALUE_STREAM_ID = p.ValueStreamId, FOLIO_NUMBER = p.FolioNumber, FOLIO_CODE = p.FolioCode,
                QMM_RESPONSIBLE = p.QmmResponsible, REQUESTOR_NAME = p.RequestorName, REQUESTING_DEPARTMENT = p.RequestingDepartment,
                IMPORT_INVOICE = p.ImportInvoice, ELP_JUP1_CROSSING_DATE = D(p.ImportDateElpJup1), DELIVERY_NUMBER = p.DeliveryNumber,
                IS_LEGACY = p.IsLegacy ? "Y" : "N", OPEN_FINDINGS = p.IsLegacy ? PalletValidator.ValidateProcess(p, queryDate).Count : 0,
            }).ToList(),
            Pallets = repo.GetAll().SelectMany(p => p.Pallets).Select(x => new
            {
                PALLET_ID = x.Id, PROCESS_ID = x.Process.Id, LINE_NO = x.LineNo, RETURNED_PART_NUMBER = x.ReturnedMaterial,
                QTY_IMPORTED = x.QtyImported, MM60_UNIT_COST_USD = x.UnitCostUsd, SAP_TOTAL_USD = x.SapUsdTotal, ORIGINAL_HU = x.OriginalHU,
                ORIGINAL_BATCH = x.OriginalBatch, PO_NUMBER = x.ReturnPONumber, CUSTOMS_EXPORT = x.CustomsExportReference,
                STATUS_CATEGORY_ID = CatId(x.StatusCategoryCode), NEW_PART_NUMBER = x.NewMaterial, NEW_HU = x.NewHU,
                REINSERTION_QTY = x.ReinsertionQty, SCRAP_QTY = x.ScrapQty, SENT_BACK_DATE = D(x.DateSentBack), SCRAP_DATE = D(x.ScrapDate),
                QTY_OPEN_FLOOR = x.Financials.QtyOpenFloor, QTY_PENDING_EXPORT = x.Financials.QtyPendingExport,
                QTY_PENDING_HSE = x.Financials.QtyPendingHse, QTY_MISSING = x.Financials.QtyMissing,
                MFO_INVENTORY_USD = x.Financials.MfoInventoryUsd, HSE_SCRAP_USD = x.Financials.HseScrapUsd,
                MFO_LOW_FAILURE_USD = x.Financials.MfoLowFailureUsd, GENERAL_BALANCE_USD = x.Financials.BalanceGeneralUsd,
                WORKFLOW_STATUS_ID = (int)x.Status, IS_LEGACY = x.Process.IsLegacy ? "Y" : "N",
                DATA_QUALITY_FLAGS = x.DataQualityText.Length == 0 ? null : x.DataQualityText,
            }).ToList(),     // materialized now: these are the values stored on the save date
        };

        // What the application calculates on the query date.
        clock.Today = queryDate;
        foreach (var p in repo.GetAll()) p.OpenFindings = p.IsLegacy ? PalletValidator.ValidateProcess(p, queryDate).Count : 0;
        var q = new InMemoryProcessQueries(repo, clock);
        var rows = q.Page(ProcessFilter.All, ProcessSort.Default, 0, int.MaxValue);
        var filters = new (string Name, ProcessFilter F, ProcessSort S)[]
        {
            ("all-default", ProcessFilter.All, ProcessSort.Default),
            ("vs7-open-balance-desc", new ProcessFilter(ValueStreamId: 7, OnlyOpen: true), new ProcessSort("Balance", true)),
            ("risk-due", new ProcessFilter(OnlyDutyRisk: true), new ProcessSort("DueDate")),
            ("status-exported-folio", new ProcessFilter(Status: WorkflowStatus.Exported), new ProcessSort("FolioCode", true)),
            ("findings-qmm", new ProcessFilter(OnlyFindings: true), new ProcessSort("Qmm")),
            ("search-mps26", new ProcessFilter(Search: "mps26"), new ProcessSort("Invoice")),
            ("search-pallet-01", new ProcessFilter(Search: "-01"), new ProcessSort("DaysInJuP1", true)),
            ("search-underscore", new ProcessFilter(Search: "_"), ProcessSort.Default),
            ("material-asc", ProcessFilter.All, new ProcessSort("Material")),
            ("status-asc", ProcessFilter.All, new ProcessSort("Status")),
        };
        var expected = new
        {
            Rows = rows,
            Pallets = q.PalletsOf(rows.Select(r => r.Id).ToList()),
            Balance = q.Balance(null),
            Balance2026 = q.Balance(2026),
            Status = q.StatusDistribution(),
            Queries = filters.Select(f =>
            {
                var (where, args) = OracleProcessQueries.Where(f.F, t => t);
                return new
                {
                    f.Name, Where = where, OrderBy = OracleProcessQueries.OrderBy(f.S),
                    Args = args.ToDictionary(a => a.Name, a => a.Value),
                    Ids = q.Page(f.F, f.S, 0, int.MaxValue).Select(r => r.Id), Totals = q.Totals(f.F),
                };
            }),
        };
        var json = new JsonSerializerOptions { WriteIndented = false };
        File.WriteAllText(Path.Combine(dir, "data.json"), JsonSerializer.Serialize(data, json));
        File.WriteAllText(Path.Combine(dir, "expected.json"), JsonSerializer.Serialize(expected, json));
    }
}
