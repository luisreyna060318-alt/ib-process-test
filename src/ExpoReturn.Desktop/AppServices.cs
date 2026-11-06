using ExpoReturn.Application;
using ExpoReturn.Infrastructure.InMemory;

namespace ExpoReturn.Desktop;

/// <summary>
/// Composición de servicios. En el borrador todo vive en memoria; en producción este es el único
/// lugar que cambia (cliente HTTP hacia la Web API en lugar de repositorios en memoria).
/// </summary>
public sealed class AppServices
{
    public required InMemoryPalletRepository Repository { get; init; }
    public required InMemoryAuditTrail Audit { get; init; }
    public required ChampionsDirectory Champions { get; init; }
    public required IClock Clock { get; init; }
    public required PalletReturnService Pallets { get; init; }
    public required ReportingService Reporting { get; init; }
    public required InMemoryAlertAcknowledgementStore AlertAcks { get; init; }
    public UserPreferences Preferences { get; } = UserPreferences.Load();

    public string DataSourceLabel { get; set; } = "Datos de demostración (sintéticos)";

    public static AppServices CreateDemo()
    {
        var repo = new InMemoryPalletRepository();
        var audit = new InMemoryAuditTrail();
        var champions = new ChampionsDirectory();
        var clock = new SystemClock();
        SampleData.Seed(repo, audit, champions, clock);
        return new AppServices
        {
            Repository = repo, Audit = audit, Champions = champions, Clock = clock,
            Pallets = new PalletReturnService(repo, audit, clock),
            Reporting = new ReportingService(repo, clock),
            AlertAcks = new InMemoryAlertAcknowledgementStore(),
        };
    }

    public void ResetDemo()
    {
        Audit.Clear();
        AlertAcks.Clear();
        Champions.Replace(new ChampionsDirectory().All());
        SampleData.Seed(Repository, Audit, Champions, Clock);
        DataSourceLabel = "Datos de demostración (sintéticos)";
    }

    public LegacyImportResult ImportLegacy(string path)
    {
        var result = LegacyExcelImporter.Import(path, Clock.Today);
        Audit.Clear();
        AlertAcks.Clear();
        Repository.ReplaceAll(result.Pallets);
        Champions.Replace(result.Champions);
        DataSourceLabel = $"Excel importado (sólo lectura, en memoria): {System.IO.Path.GetFileName(path)}";
        return result;
    }
}
