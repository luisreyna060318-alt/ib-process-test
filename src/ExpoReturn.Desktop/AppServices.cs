using ExpoReturn.Application;
using ExpoReturn.Infrastructure.InMemory;

namespace ExpoReturn.Desktop;

/// <summary>
/// Service composition. In the draft pallets live in memory and users in a JSON file; in production this is the only
/// place that changes (Oracle-backed repositories instead of the in-memory / file ones).
/// </summary>
public sealed class AppServices
{
    public required AppConfig Config { get; init; }
    public required InMemoryPalletRepository Repository { get; init; }
    public required InMemoryAuditTrail Audit { get; init; }
    public required IUserDirectory Users { get; init; }
    public required IClock Clock { get; init; }
    public required PalletReturnService Pallets { get; init; }
    public required ReportingService Reporting { get; init; }
    public required InMemoryAlertAcknowledgementStore AlertAcks { get; init; }
    public required IMaterialCostCatalog MaterialCosts { get; init; }
    public UserPreferences Preferences { get; } = UserPreferences.Load();

    public string DataSourceLabel { get; set; } = "Demo data (synthetic)";

    public static AppServices Create(AppConfig config)
    {
        var repo = new InMemoryPalletRepository();
        var audit = new InMemoryAuditTrail();
        var users = new UserDirectory(config.ResolvedUserStorePath, config.Owners, config.DemoUsers);
        var clock = new SystemClock();
        var costs = new InMemoryMaterialCostCatalog();
        if (config.DemoData)
        {
            SampleData.Seed(repo, audit, users, clock);
            costs.Replace(SampleData.DemoCosts(), "Demo MM60 data");
        }
        return new AppServices
        {
            Config = config, Repository = repo, Audit = audit, Users = users, Clock = clock,
            Pallets = new PalletReturnService(repo, audit, clock),
            Reporting = new ReportingService(repo, clock),
            AlertAcks = new InMemoryAlertAcknowledgementStore(),
            MaterialCosts = costs,
            DataSourceLabel = config.DemoData ? "Demo data (synthetic)" : "No data loaded",
        };
    }

    public void ResetDemo()
    {
        Audit.Clear();
        AlertAcks.Clear();
        SampleData.Seed(Repository, Audit, Users, Clock);
        MaterialCosts.Replace(SampleData.DemoCosts(), "Demo MM60 data");
        DataSourceLabel = "Demo data (synthetic)";
    }

    public LegacyImportResult ImportLegacy(string path)
    {
        var result = LegacyExcelImporter.Import(path, Clock.Today);
        Audit.Clear();
        AlertAcks.Clear();
        Repository.ReplaceAll(result.Pallets);
        if (result.MaterialCosts.Count > 0) MaterialCosts.Replace(result.MaterialCosts, $"{System.IO.Path.GetFileName(path)} · sheet MM60 COST SAP");
        DataSourceLabel = $"Imported Excel (read-only, in memory): {System.IO.Path.GetFileName(path)}";
        return result;
    }

    /// <summary>Owner: loads the MM60 export and completes the cost of open records still without it.</summary>
    public Mm60ImportSummary ImportMm60(string path, string ownerNtUser) =>
        Mm60Import.Run(path, MaterialCosts, Repository, Audit, Clock, Pallets.Parameters, ownerNtUser);
}
