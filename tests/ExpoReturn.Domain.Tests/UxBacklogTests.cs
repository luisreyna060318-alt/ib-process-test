using ClosedXML.Excel;
using ExpoReturn.Application;
using ExpoReturn.Domain;
using ExpoReturn.Infrastructure.InMemory;
using Xunit;

namespace ExpoReturn.Domain.Tests;

/// <summary>Lógica no visual de los pendientes UX-B01, UX-B06 y UX-B10 (matriz Champions VS).</summary>
public class UxBacklogTests
{
    [Fact]
    public void New_draft_required_warnings_are_flagged_as_pending_for_requested()
    {
        var draft = new PalletReturn { ValueStreamId = 1 };
        var issues = PalletValidator.Validate(draft, DateTime.Today);
        var pending = issues.Where(i => i.IsRequiredPending).ToList();
        Assert.True(pending.Count >= 6);
        Assert.All(pending, i => Assert.Equal(Severity.Warning, i.Severity));
        Assert.DoesNotContain(issues, i => i.Severity == Severity.Error);     // un borrador vacío se puede guardar
    }

    [Fact]
    public void Fields_are_faithful_to_the_excel_columns()
    {
        string[] removed = { "ImportPedimento", "DutyPaymentStatus", "IbOrderTypeCode", "IbOrderNumber", "PhysicalDeliveredDate",
            "DeclaredMissing", "GreenConeReported", "PlatformReported", "RmaNumber", "CustomsExportDate", "IsCancelled", "CancelReason",
            "LowExport", "LowExportNote", "Comments", "ReturnPOItem" };
        var props = typeof(PalletReturn).GetProperties().Select(p => p.Name).ToHashSet();
        Assert.All(removed, r => Assert.DoesNotContain(r, props));

        // The LOW export column held two values: it is split into number and date
        Assert.Contains(nameof(PalletReturn.LowExportNumber), props);
        Assert.Contains(nameof(PalletReturn.LowExportDate), props);
        // PO date and PO number are captured in the Request tab and belong to QMM (Excel area row)
        Assert.Contains(nameof(PalletReturn.DateReturnedToWhs), Fields.Stage1);
        Assert.Contains(nameof(PalletReturn.ReturnPONumber), Fields.Stage1);
        Assert.Contains(nameof(PalletReturn.DateReturnedToWhs), FieldPermissions.EditableFields(RoleCode.Qmm));
        Assert.DoesNotContain(nameof(PalletReturn.DateReturnedToWhs), FieldPermissions.EditableFields(RoleCode.Gs));
    }

    [Fact]
    public void Comments_options_follow_each_vs_sheet_of_the_excel()
    {
        Assert.Equal(new[] { "EXPORT", "SCRAP", "INVENTORY", "LOW-NO REPORTADO (PLATAFORMA,CONO,RMA)", "MFO-NO REPORTADO (PLATAFORMA,CONO,RMA)",
                             "ENVIO NACIONAL", "SCRAP SIN SALIDA FISICA" }, Catalogs.CommentsOptions(1));
        Assert.Equal(9, Catalogs.CommentsOptions(4).Count);
        Assert.Contains("NO REPORTADO CON CONO VERDE", Catalogs.CommentsOptions(1, "NO REPORTADO CON CONO VERDE"));   // valor previo se conserva
        Assert.Equal("SCRAP", Catalogs.FindCategory("scrap")!.Code);
        Assert.True(Catalogs.FindCategory("NO LLEGO CON CONO")!.AffectsDuty);
    }

    [Fact]
    public void A_user_without_value_stream_cannot_edit()
    {
        var pallet = new PalletReturn { ValueStreamId = 4 };
        var noVs = new UserContext("T\\LOW", "No VS", "LOW", false, Array.Empty<AccessGrant>());
        Assert.False(FieldPermissions.CanEdit(noVs, pallet, nameof(PalletReturn.LowExportNumber)));
        Assert.Equal("No VS", noVs.ValueStreamText);
    }

    [Fact]
    public void Inbox_export_writes_a_protected_workbook()
    {
        var repo = new InMemoryPalletRepository();
        SampleData.Seed(repo, new InMemoryAuditTrail(), new UserDirectory((string?)null, Array.Empty<string>(), includeDemoUsers: true), new SystemClock());
        var path = Path.Combine(Path.GetTempPath(), $"export_{Guid.NewGuid():N}.xlsx");
        try
        {
            ExcelExporter.ExportPallets(repo.GetAll(), path, DateTime.Today, "Todos los VS · Todos");
            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheet("Returns");
            Assert.True(ws.IsProtected);
            Assert.Equal("Folio", ws.Cell(3, 1).GetString());
            Assert.Equal(repo.GetAll().Count, ws.LastRowUsed()!.RowNumber() - 3);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Reasons_and_statuses_are_shown_in_english_as_in_the_excel()
    {
        Assert.All(Catalogs.Reasons, r => Assert.Equal(r.Description, r.Label));
        Assert.Equal("To change FG P/N with reflash", Catalogs.FindReason(3)!.Label);
        Assert.Equal("Duty risk", WorkflowStatus.DutyRisk.DisplayName());
    }
}
