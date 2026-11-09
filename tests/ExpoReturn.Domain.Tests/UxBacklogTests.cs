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
    public void Simulated_users_take_their_value_streams_from_the_matrix()
    {
        var dir = new ChampionsDirectory();
        dir.Replace(new[]
        {
            new Person("Ana Uno", RoleCode.Qmm, 1, "Champion"),
            new Person("Ana Uno", RoleCode.Qmm, 7, "Proxy"),
            new Person("Gus Dos", RoleCode.Gs, 4, "Champion"),
            new Person("TBD", RoleCode.Mfo, 7, "Proxy"),
        });
        var users = dir.DemoUsers();
        var ana = Assert.Single(users, u => u.DisplayName == "Ana Uno");
        Assert.Equal(new[] { 1, 7 }, ana.ValueStreams);
        Assert.Equal("DEMO\\ana.uno", ana.NtUser);

        var gs = Assert.Single(users, u => u.Role == RoleCode.Gs);       // Comercio Exterior también lo delimita la matriz
        Assert.True(gs.CoversValueStream(4));
        Assert.False(gs.CoversValueStream(1));
        Assert.DoesNotContain(users, u => u.DisplayName == "TBD");
        Assert.Contains(users, u => u.IsAdmin && u.CoversValueStream(9));
        Assert.Equal(new[] { "Ana Uno" }, dir.QmmFor(7));
    }

    [Fact]
    public void Removing_an_assignment_updates_users_and_notifies()
    {
        var dir = new ChampionsDirectory();
        var changed = 0;
        dir.Changed += (_, _) => changed++;
        var andrea = dir.DemoUsers().Single(u => u.DisplayName == "Andrea Salinas");
        Assert.Equal(new[] { 1, 7 }, andrea.ValueStreams);                // una persona en varios VS

        dir.Replace(dir.All().Where(p => !(p.Name == "Andrea Salinas" && p.ValueStreamId == 7)));
        Assert.Equal(1, changed);
        Assert.Equal(new[] { 1 }, dir.DemoUsers().Single(u => u.DisplayName == "Andrea Salinas").ValueStreams);
        Assert.DoesNotContain("Andrea Salinas", dir.QmmFor(7));
    }

    [Fact]
    public void Demo_matrix_has_a_dedicated_user_per_area_and_value_stream()
    {
        var users = new ChampionsDirectory().DemoUsers();
        var areas = new[] { RoleCode.Qmm, RoleCode.Requestor, RoleCode.Gs, RoleCode.Lop, RoleCode.Mfo, RoleCode.Low, RoleCode.Hse };
        foreach (var area in areas)
            foreach (var vs in Catalogs.ValueStreams.Select(v => v.Id))
                Assert.Contains(users, u => u.Role == area && u.ValueStreams.SequenceEqual(new[] { vs }));
        Assert.Contains(users, u => u.ValueStreams.Count > 1);                  // y personas en varios VS
    }

    [Fact]
    public void Fields_are_faithful_to_the_excel_columns()
    {
        string[] removed = { "ImportPedimento", "DutyPaymentStatus", "IbOrderTypeCode", "IbOrderNumber", "PhysicalDeliveredDate",
            "DeclaredMissing", "GreenConeReported", "PlatformReported", "RmaNumber", "CustomsExportDate", "IsCancelled", "CancelReason",
            "ReturnPOItem", "LowExportNumber", "LowExportDate", "LowExportNote", "Comments" };
        var props = typeof(PalletReturn).GetProperties().Select(p => p.Name).ToHashSet();
        Assert.All(removed, r => Assert.DoesNotContain(r, props));

        // Date PO y PO Number se capturan en la Solicitud y son de QMM (fila de áreas del Excel)
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
        var noVs = new UserContext("T\\LOW", "Sin VS", RoleCode.Low, Array.Empty<int>());
        Assert.False(FieldPermissions.CanEdit(noVs, pallet, nameof(PalletReturn.LowExport)));
        Assert.Equal("Sin VS asignado", noVs.ValueStreamText);
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
            Assert.Equal("#", ws.Cell(3, 1).GetString());
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
