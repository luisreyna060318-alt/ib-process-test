using ExpoReturn.Application;
using ExpoReturn.Domain;
using ExpoReturn.Domain.Rules;
using ExpoReturn.Infrastructure.InMemory;
using Xunit;
using Xunit.Abstractions;

namespace ExpoReturn.Domain.Tests;

public class ParsingAndImportTests
{
    private readonly ITestOutputHelper _out;
    public ParsingAndImportTests(ITestOutputHelper output) => _out = output;

    [Theory]
    [InlineData("0203306644-250", "0203.306.644-250")]
    [InlineData("F005V04580-5ZS", "F005.V04.580-5ZS")]
    [InlineData("020330704824W", "0203.307.048-24W")]
    [InlineData("F00H.M00.196.2ZB", "F00H.M00.196-2ZB")]
    [InlineData("0285.009.016-24w", "0285.009.016-24W")]
    [InlineData("F005.V01.908-99", "F005.V01.908-99")]
    public void Material_numbers_are_normalized(string raw, string expected)
    {
        var n = Identifiers.NormalizeMaterial(raw);
        Assert.Equal(expected, n);
        Assert.True(Identifiers.IsValidMaterial(n));
    }

    [Theory]
    [InlineData("JAN.20.2025", 2025, 1, 20)]
    [InlineData("DIC.11.24 MPS24000258", 2024, 12, 11)]
    [InlineData("Juio/16/2026", 2026, 7, 16)]
    [InlineData("17/07/2026", 2026, 7, 17)]
    [InlineData("Ene/23/2025", 2025, 1, 23)]
    [InlineData("17/Dic/2025", 2025, 12, 17)]
    [InlineData("29/11/24", 2024, 11, 29)]
    [InlineData("ENE.01.2025  MPS2500001", 2025, 1, 1)]
    public void Legacy_dates_are_parsed(string raw, int y, int m, int d) =>
        Assert.Equal(new DateTime(y, m, d), LegacyDate.Parse(raw));

    [Theory]
    [InlineData("N/A")]
    [InlineData("plataforma amarilla")]
    [InlineData("ECUs for destructive tests, will be scrapped")]
    public void Non_dates_return_null(string raw) => Assert.Null(LegacyDate.Parse(raw));

    [Fact]
    public void Sample_data_covers_every_open_and_closed_status()
    {
        var repo = new InMemoryPalletRepository();
        SampleData.Seed(repo, new InMemoryAuditTrail(), new ChampionsDirectory(), new SystemClock());
        var statuses = repo.GetAll().Select(p => p.Status).Distinct().ToList();
        foreach (var s in new[] { WorkflowStatus.Draft, WorkflowStatus.Requested, WorkflowStatus.Imported, WorkflowStatus.InRework,
                                  WorkflowStatus.PendingExport, WorkflowStatus.PendingHse, WorkflowStatus.DutyRisk,
                                  WorkflowStatus.Exported, WorkflowStatus.Scrapped, WorkflowStatus.ClosedDutyPaid })
            Assert.Contains(s, statuses);
    }

    [Fact]
    public void Save_respects_field_permissions_and_audits_changes()
    {
        var repo = new InMemoryPalletRepository();
        var audit = new InMemoryAuditTrail();
        var clock = new SystemClock();
        SampleData.Seed(repo, audit, new ChampionsDirectory(), clock);
        var svc = new PalletReturnService(repo, audit, clock);
        var open = repo.GetAll().First(p => p.Status == WorkflowStatus.Imported);

        var mfoOtherVs = new UserContext("T\\MFO", "MFO", RoleCode.Mfo, new[] { open.ValueStreamId == 1 ? 4 : 1 });
        var edit = open.Clone(); edit.ImportInvoice = "MPS26999999";
        var denied = svc.Save(edit, mfoOtherVs);
        Assert.False(denied.Success);
        Assert.Contains(nameof(PalletReturn.ImportInvoice), denied.DeniedFields);

        var gs = new UserContext("T\\GS", "GS", RoleCode.Gs, new[] { open.ValueStreamId });
        var edit2 = open.Clone(); edit2.ImportInvoice = $"MPS{open.ImportDate:yy}999999";
        var ok = svc.Save(edit2, gs);
        Assert.True(ok.Success, ok.Summary);
        Assert.Contains(audit.For(open.Id), a => a.Field == nameof(PalletReturn.ImportInvoice) && a.NtUser == "T\\GS");
    }

    /// <summary>Se ejecuta sólo si EXPORETURN_LEGACY_XLSX apunta al Excel real (no se versiona).</summary>
    [Fact]
    public void Real_excel_import_when_available()
    {
        var path = Environment.GetEnvironmentVariable("EXPORETURN_LEGACY_XLSX");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { _out.WriteLine("Omitido: sin archivo real."); return; }

        var result = LegacyExcelImporter.Import(path, new DateTime(2026, 11, 5));
        _out.WriteLine(result.Summary);
        foreach (var kv in result.IssueCounts.OrderByDescending(k => k.Value)) _out.WriteLine($"  {kv.Key}: {kv.Value}");
        foreach (var g in result.Pallets.GroupBy(p => p.ValueStreamId).OrderBy(g => g.Key))
            _out.WriteLine($"  VS{g.Key}: {g.Count()} reg · importado USD {g.Where(p => p.ImportDateElpJup1 != null).Sum(p => p.SapUsdTotal):N0} · balance v2 USD {g.Sum(p => p.Financials.BalanceGeneralUsd):N0} · " +
                           string.Join(", ", g.GroupBy(p => p.Status).OrderBy(s => s.Key).Select(s => $"{s.Key.DisplayName()} {s.Count()}")));
        _out.WriteLine($"  Champions leídos: {result.Champions.Count}");

        Assert.Equal(82, result.RowsPerSheet["VS1"]);
        Assert.Equal(149, result.RowsPerSheet["VS4"]);
        Assert.Equal(98, result.RowsPerSheet["VS5"]);
        Assert.Equal(194, result.RowsPerSheet["VS7"]);
        Assert.Equal(102, result.RowsPerSheet["VS9"]);
        Assert.True(result.Champions.Count > 10);
    }
}
