using ClosedXML.Excel;
using ExpoReturn.Application;
using ExpoReturn.Domain;
using ExpoReturn.Infrastructure.InMemory;
using Xunit;

namespace ExpoReturn.Domain.Tests;

/// <summary>Lógica no visual de los pendientes UX-B01, UX-B06 y UX-B10.</summary>
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
    public void Champions_with_closed_validity_are_not_offered_as_qmm_responsible()
    {
        var dir = new ChampionsDirectory();
        dir.Replace(new[]
        {
            new Person("Vigente", RoleCode.Qmm, 7, "Champion", DateTime.Today.AddDays(-30)),
            new Person("Dado de baja", RoleCode.Qmm, 7, "Proxy", DateTime.Today.AddDays(-90), DateTime.Today.AddDays(-1)),
            new Person("Futuro", RoleCode.Qmm, 7, "Proxy", DateTime.Today.AddDays(10)),
        });
        Assert.Equal(new[] { "Vigente" }, dir.QmmFor(7));
    }

    [Fact]
    public void Inbox_export_writes_a_protected_workbook()
    {
        var repo = new InMemoryPalletRepository();
        SampleData.Seed(repo, new InMemoryAuditTrail(), new ChampionsDirectory(), new SystemClock());
        var path = Path.Combine(Path.GetTempPath(), $"export_{Guid.NewGuid():N}.xlsx");
        try
        {
            ExcelExporter.ExportPallets(repo.GetAll(), path, DateTime.Today, "Todos los VS · Todos");
            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheet("Retornos");
            Assert.True(ws.IsProtected);
            Assert.Equal("Folio", ws.Cell(3, 1).GetString());
            Assert.Equal(repo.GetAll().Count, ws.LastRowUsed()!.RowNumber() - 3);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Reasons_have_spanish_label_and_keep_excel_text()
    {
        Assert.All(Catalogs.Reasons, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Label));
            Assert.NotEqual(r.Description, r.Label);
        });
        Assert.Equal("To change FG P/N with reflash", Catalogs.FindReason(3)!.Description);
    }
}
