using ClosedXML.Excel;
using ExpoReturn.Application;
using ExpoReturn.Domain;
using ExpoReturn.Infrastructure.InMemory;
using Xunit;

namespace ExpoReturn.Domain.Tests;

/// <summary>What is calculated from part number + quantity, and the MM60 cost master that replaces SAP.</summary>
public class CostAndCalculationTests
{
    private static readonly DateTime Today = new(2026, 11, 10);

    [Fact]
    public void Part_number_and_quantity_give_the_sap_total_and_the_rest_waits_for_the_crossing()
    {
        // Request: part number + quantity. The cost comes from the MM60 master.
        var catalog = new InMemoryMaterialCostCatalog();
        catalog.Replace(new[] { new MaterialCost("0203306644-250", "Radar", 2999.81m, 100m, "USD") }, "test");   // 29.9981 per piece
        var p = new PalletReturn { ValueStreamId = 4, ReturnedMaterial = "0203.306.644-250", QtyImported = 1440 };
        p.UnitCostUsd = catalog.Find(p.ReturnedMaterial)!.UnitCostUsd;
        Assert.Equal(29.9981m, p.UnitCostUsd);
        Assert.Equal(43197.26m, p.SapUsdTotal);                                   // calculated right away

        var e = PalletEvaluator.Evaluate(p, CalcParameters.Default, Today);
        Assert.Equal(0m, e.Financials.BalanceGeneralUsd);                          // not imported yet: nothing at risk
        Assert.Equal(WorkflowStatus.Draft, e.Status);                              // other required request fields missing

        // Rest of the request → Requested
        p.QmmResponsible = "Ivonne Castillo"; p.RequestingDepartment = "MFQ4"; p.RequestorName = "Montaño, Sofía";
        p.ReasonId = 6; p.OriginalHU = "2104891847";
        Assert.Equal(WorkflowStatus.Requested, PalletEvaluator.Evaluate(p, CalcParameters.Default, Today).Status);

        // Crossing El Paso → JuP1: the IMMEX clock starts and all pieces are on the MFO floor
        p.ImportDate = p.ImportDateElpJup1 = new DateTime(2026, 10, 30);
        p.ImportInvoice = "MPS26000222";
        e = PalletEvaluator.Evaluate(p, CalcParameters.Default, Today);
        Assert.Equal(43197.26m, e.Financials.MfoInventoryUsd);
        Assert.Equal(43197.26m, e.Financials.BalanceGeneralUsd);
        Assert.Equal(0m, e.Financials.DutiesToPayUsd);
        Assert.Equal(new DateTime(2026, 10, 30).AddDays(540), e.Risk.LegalDueDate);
        Assert.Equal(WorkflowStatus.Imported, e.Status);

        // MFO returns everything to LOW: no pedimento yet → MFO-LOW failure and duties to pay
        p.OldMaterialDeliveredQty = 1440; p.ReinsertionQty = 0; p.PhysicalDeliveredQty = 1440;
        e = PalletEvaluator.Evaluate(p, CalcParameters.Default, Today);
        Assert.Equal(WorkflowStatus.PendingExport, e.Status);
        Assert.Equal(43197.26m, e.Financials.MfoLowFailureUsd);
        Assert.Equal(8207.48m, e.Financials.DutiesToPayUsd);                     // × 0.19

        // Customs export with PRyy-#####: reconciled
        p.CustomsExportReference = "PR26-00123";
        e = PalletEvaluator.Evaluate(p, CalcParameters.Default, Today);
        Assert.Equal(WorkflowStatus.Exported, e.Status);
        Assert.Equal(0m, e.Financials.BalanceGeneralUsd);
    }

    [Fact]
    public void Mm60_reader_finds_the_header_and_uses_price_unit()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mm60_{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Sheet1");
                ws.Cell(1, 1).Value = "MM60 export";
                string[] h = { "Unit Cost MM60", "Material", "Material Description", "Price", "ABC Indicator", "Currency", "Base Unit of Measure", "Price unit" };
                for (var i = 0; i < h.Length; i++) ws.Cell(3, i + 1).Value = h[i];
                ws.Cell(4, 2).Value = "0285.500.708-24W"; ws.Cell(4, 4).Value = 126.77; ws.Cell(4, 8).Value = 100;
                ws.Cell(5, 2).Value = "F005.V04.580-5ZS"; ws.Cell(5, 4).Value = 12.84; ws.Cell(5, 8).Value = 1;
                ws.Cell(6, 2).Value = "0263.B63.526-9X"; ws.Cell(6, 4).Value = "n/a";
                wb.SaveAs(path);
            }
            var r = Mm60Reader.Read(path);
            Assert.Equal(2, r.Costs.Count);
            Assert.Equal(1, r.RowsSkipped);
            var catalog = new InMemoryMaterialCostCatalog();
            catalog.Replace(r.Costs, "test");
            Assert.Equal(1.2677m, catalog.Find("0285500708-24W")!.UnitCostUsd);   // found without dots
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Mm60_import_completes_the_cost_of_records_saved_without_it()
    {
        var repo = new InMemoryPalletRepository();
        var audit = new InMemoryAuditTrail();
        var pending = new PalletReturn
        {
            ValueStreamId = 1, FolioNumber = 1, ReturnedMaterial = "0285.500.708-24W", QtyImported = 100, UnitCostSource = "PENDING",
            ImportDate = new DateTime(2026, 10, 1), ImportDateElpJup1 = new DateTime(2026, 10, 1), ImportInvoice = "MPS26000001",
        };
        repo.Save(pending);
        var path = Path.Combine(Path.GetTempPath(), $"mm60_{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("MM60");
                ws.Cell(1, 1).Value = "Material"; ws.Cell(1, 2).Value = "Price"; ws.Cell(1, 3).Value = "Price unit";
                ws.Cell(2, 1).Value = "0285.500.708-24W"; ws.Cell(2, 2).Value = 126.77; ws.Cell(2, 3).Value = 100;
                wb.SaveAs(path);
            }
            var catalog = new InMemoryMaterialCostCatalog();
            var summary = Mm60Import.Run(path, catalog, repo, audit, new SystemClock(), CalcParameters.Default, "BOSCH\\OWNER");
            Assert.Equal(1, summary.RecordsUpdated);
            var saved = repo.GetAll().Single();
            Assert.Equal("MM60", saved.UnitCostSource);
            Assert.Equal(126.77m, saved.SapUsdTotal);
            Assert.Equal(126.77m, saved.Financials.BalanceGeneralUsd);              // recalculated
            Assert.Contains(audit.All(), a => a.Field == nameof(PalletReturn.UnitCostUsd) && a.NtUser == "SYSTEM");
        }
        finally { File.Delete(path); }
    }
}
