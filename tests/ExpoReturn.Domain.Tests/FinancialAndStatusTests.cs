using ExpoReturn.Domain;
using Xunit;

namespace ExpoReturn.Domain.Tests;

/// <summary>Casos golden (doc 03 §6), tomados de registros del Excel 2.0.</summary>
public class FinancialAndStatusTests
{
    private static readonly DateTime Today = new(2026, 11, 5);

    private static PalletReturn Base(int vs, string pn, int qty, decimal cost) => new()
    {
        ValueStreamId = vs, FolioNumber = 1, QmmResponsible = "QMM", RequestingDepartment = "MFQ", RequestorName = "Solicitante",
        ReasonId = 7, ReturnedMaterial = pn, QtyImported = qty, UnitCostUsd = cost, OriginalBatch = "AA23743599",
    };

    [Fact]
    public void VS7_formula_in_customs_export_is_pending_export_not_closed()
    {
        var p = Base(7, "F00H.J04.154-24W", 576, 20.6467m);
        p.ImportDate = p.ImportDateElpJup1 = new DateTime(2026, 10, 30);
        p.ImportInvoice = "MPS26000222";
        p.PhysicalDeliveredQty = 576; p.OldMaterialDeliveredQty = 576; p.ReinsertionQty = 0;
        p.LowExportNote = "Incluido en expo #4 de el dia 03/11/2026"; // sin PRyy-#####

        var e = PalletEvaluator.Evaluate(p, CalcParameters.Default, Today);

        Assert.Equal(WorkflowStatus.PendingExport, e.Status);
        Assert.Equal(11892.50m, e.Financials.MfoLowFailureUsd);
        Assert.Equal(11892.50m, e.Financials.BalanceGeneralUsd);
        Assert.Equal(2259.58m, e.Financials.DutyExposureUsd);
    }

    [Fact]
    public void VS9_missing_piece_is_duty_risk_and_fix_closes_it()
    {
        var p = Base(9, "0265.957.012-24W", 528, 23.5871m);
        p.ImportDate = p.ImportDateElpJup1 = new DateTime(2026, 3, 2);
        p.ImportInvoice = "MPS26000036";
        p.PhysicalDeliveredQty = 527; p.OldMaterialDeliveredQty = 527;
        p.CustomsExportReference = "PR26-00027";
        p.ScrapGrNumber = "4951422891"; p.HseScrapKg = 0.5m; p.HseConfirmationDate = new DateTime(2026, 3, 6);

        var e = PalletEvaluator.Evaluate(p, CalcParameters.Default, Today);
        Assert.Equal(1, e.Financials.QtyMissing);
        Assert.Equal(23.59m, e.Financials.BalanceGeneralUsd);
        Assert.Equal(WorkflowStatus.DutyRisk, e.Status);
        Assert.Contains(e.Issues, i => i.Code == "VAL-041");

        p.ScrapQty = 1; p.ScrapDate = new DateTime(2026, 3, 4);
        var fixedEval = PalletEvaluator.Evaluate(p, CalcParameters.Default, Today);
        Assert.Equal(0m, fixedEval.Financials.BalanceGeneralUsd);
        Assert.Equal(WorkflowStatus.Exported, fixedEval.Status);
    }

    [Fact]
    public void Not_imported_has_no_exposure()
    {
        var p = Base(1, "0285.500.708-24W", 60, 1.2677m);
        var e = PalletEvaluator.Evaluate(p, CalcParameters.Default, Today);
        Assert.Equal(WorkflowStatus.Requested, e.Status);
        Assert.Equal(0m, e.Financials.BalanceGeneralUsd);
    }

    [Fact]
    public void Scrap_is_released_by_hse_confirmation_not_by_pedimento()
    {
        var p = Base(4, "0203.306.644-250", 100, 10m);
        p.ImportDate = p.ImportDateElpJup1 = new DateTime(2026, 9, 1);
        p.ImportInvoice = "MPS26000100";
        p.PhysicalDeliveredQty = 0; p.OldMaterialDeliveredQty = 0; p.ScrapQty = 100; p.ScrapDate = new DateTime(2026, 9, 3);

        Assert.Equal(WorkflowStatus.PendingHse, PalletEvaluator.Evaluate(p, CalcParameters.Default, Today).Status);
        p.ScrapGrNumber = "4951400001"; p.HseScrapKg = 35; p.HseConfirmationDate = new DateTime(2026, 9, 10);
        var e = PalletEvaluator.Evaluate(p, CalcParameters.Default, Today);
        Assert.Equal(WorkflowStatus.Scrapped, e.Status);
        Assert.Equal(0m, e.Financials.BalanceGeneralUsd);
    }

    [Fact]
    public void Aging_close_to_legal_term_is_duty_risk()
    {
        var p = Base(5, "0261.S10.8CU-6DY", 168, 84.3378m);
        p.ImportDate = p.ImportDateElpJup1 = Today.AddDays(-525);
        p.ImportInvoice = $"MPS{p.ImportDate:yy}000124";
        var e = PalletEvaluator.Evaluate(p, CalcParameters.Default, Today);
        Assert.Equal(WorkflowStatus.DutyRisk, e.Status);
        Assert.True(e.Risk.DaysToLegalDue <= 30);
    }

    [Theory]
    [InlineData("PR26-00095", ClosureDocumentType.ExportPedimento)]
    [InlineData("pr25-00104", ClosureDocumentType.ExportPedimento)]
    [InlineData("F4", ClosureDocumentType.CambioRegimenF4)]
    [InlineData("V1", ClosureDocumentType.PedimentoVirtualV1)]
    [InlineData("scrap marzo", ClosureDocumentType.Unrecognized)]
    [InlineData(null, ClosureDocumentType.None)]
    public void Closure_document_classification(string? reference, ClosureDocumentType expected) =>
        Assert.Equal(expected, ClosureDocument.Classify(reference));

    [Fact]
    public void Balance_invariant_holds()
    {
        var rnd = new Random(7);
        for (var i = 0; i < 500; i++)
        {
            var qty = rnd.Next(1, 1500);
            var scrap = rnd.Next(0, qty / 3 + 1);
            var old = rnd.Next(0, qty - scrap + 1);
            var p = Base(1, "0285.500.708-24W", qty, (decimal)Math.Round(rnd.NextDouble() * 90, 4));
            p.ImportDate = p.ImportDateElpJup1 = Today.AddDays(-rnd.Next(1, 300));
            p.OldMaterialDeliveredQty = old; p.ScrapQty = scrap;
            if (rnd.Next(2) == 0) p.PhysicalDeliveredQty = old;
            var exported = rnd.Next(2) == 0; if (exported) p.CustomsExportReference = "PR26-00001";
            var hse = rnd.Next(2) == 0; if (hse) { p.ScrapGrNumber = "4951400001"; p.HseScrapKg = 1; p.HseConfirmationDate = Today; }

            var f = FinancialCalculator.Calculate(p, CalcParameters.Default);
            var expected = p.SapUsdTotal - (exported ? old : 0) * p.UnitCostUsd!.Value - (hse ? scrap : 0) * p.UnitCostUsd!.Value;
            Assert.InRange(f.BalanceGeneralUsd - expected, -0.03m, 0.03m);
            Assert.True(f.MfoInventoryUsd >= 0 && f.HseScrapUsd >= 0 && f.MfoLowFailureUsd >= 0);
        }
    }
}
