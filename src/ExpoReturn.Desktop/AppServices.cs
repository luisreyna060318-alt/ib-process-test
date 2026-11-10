using System.IO;
using System.Text;
using ExpoReturn.Application;
using ExpoReturn.Domain;
using ExpoReturn.Infrastructure.InMemory;
using ExpoReturn.Infrastructure.Oracle;

namespace ExpoReturn.Desktop;

/// <summary>
/// Service composition: the only place that knows where data lives.
/// Oracle mode: IB_PROCESS_ tables with the account SharedServersAccess.dll returns (nothing is ever deleted).
/// Local mode: demo pallets in memory and users in a JSON file.
/// </summary>
public sealed class AppServices
{
    public required AppConfig Config { get; init; }
    public required IPalletReturnRepository Repository { get; init; }
    public required IAuditTrail Audit { get; init; }
    public required IUserDirectory Users { get; init; }
    public required IClock Clock { get; init; }
    public required PalletReturnService Pallets { get; init; }
    public required ReportingService Reporting { get; init; }
    public required IAlertAcknowledgementStore AlertAcks { get; init; }
    public required IMaterialCostCatalog MaterialCosts { get; init; }
    public required IFeedbackStore Feedback { get; init; }
    public UserPreferences Preferences { get; } = UserPreferences.Load();

    /// <summary>True when the data is in Oracle (shared, permanent).</summary>
    public bool IsDatabase { get; private init; }
    public string DataSourceLabel { get; set; } = "Demo data (synthetic)";

    // ------------------------------------------------------------------ composition

    /// <summary>Local (demo) mode: synthetic pallets in memory, users in the JSON file.</summary>
    public static AppServices CreateLocal(AppConfig config)
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
            Feedback = new CsvFeedbackStore(),
            DataSourceLabel = config.DemoData ? "Demo data (synthetic)" : "No data loaded",
        };
    }

    /// <summary>Oracle mode. <paramref name="credentials"/> already has the master key (see App startup).</summary>
    public static AppServices CreateOracle(AppConfig config, SharedServersCredentials credentials)
    {
        var db = new OracleDb(config.Oracle, credentials);
        var check = db.Check();
        var clock = new SystemClock();
        var parameters = OracleParameters.Load(db);
        var repo = new OraclePalletRepository(db, parameters, () => clock.Today);
        var audit = new OracleAuditTrail(db, AppConfig.Version);
        var users = new UserDirectory(new OracleUserStore(db), config.Owners, config.DemoUsers);
        return new AppServices
        {
            Config = config, Repository = repo, Audit = audit, Users = users, Clock = clock,
            Pallets = new PalletReturnService(repo, audit, clock, parameters),
            Reporting = new ReportingService(repo, clock),
            AlertAcks = new OracleAlertStore(db),
            MaterialCosts = new OracleMaterialCostCatalog(db),
            Feedback = new OracleFeedbackStore(db),
            IsDatabase = true,
            DataSourceLabel = check,
        };
    }

    // ------------------------------------------------------------------ actions

    /// <summary>Reloads what other users saved (Oracle) or just refreshes the screens (local).</summary>
    public void Reload()
    {
        Repository.Refresh();
        Users.Refresh();
        AlertAcks.Refresh();
        MaterialCosts.Refresh();
    }

    /// <summary>Local mode only: back to the synthetic data. Not available with the database (nothing is ever deleted).</summary>
    public void ResetDemo()
    {
        if (IsDatabase || Repository is not InMemoryPalletRepository repo) throw new InvalidOperationException("Not available with the database.");
        (Audit as InMemoryAuditTrail)?.Clear();
        (AlertAcks as InMemoryAlertAcknowledgementStore)?.Clear();
        SampleData.Seed(repo, Audit, Users, Clock);
        MaterialCosts.Replace(SampleData.DemoCosts(), "Demo MM60 data");
        DataSourceLabel = "Demo data (synthetic)";
    }

    public sealed record LegacyLoad(LegacyImportResult Read, LegacyAppendResult? Stored);

    /// <summary>
    /// Excel 2.0. Local: replaces the demo data in memory (read-only view). Oracle (Owner): adds the historical rows that are
    /// not in the database yet; rows already loaded are skipped and nothing is replaced. The MM60 sheet updates the cost master.
    /// </summary>
    public LegacyLoad ImportLegacy(string path, string ntUser)
    {
        var result = LegacyExcelImporter.Import(path, Clock.Today);
        var source = $"{Path.GetFileName(path)} · sheet MM60 COST SAP";
        if (!IsDatabase && Repository is InMemoryPalletRepository repo)
        {
            (Audit as InMemoryAuditTrail)?.Clear();
            (AlertAcks as InMemoryAlertAcknowledgementStore)?.Clear();
            repo.ReplaceAll(result.Pallets);
            if (result.MaterialCosts.Count > 0) MaterialCosts.Replace(result.MaterialCosts, source, CostSource.Excel);
            DataSourceLabel = $"Imported Excel (read-only, in memory): {Path.GetFileName(path)}";
            return new LegacyLoad(result, null);
        }
        if (result.MaterialCosts.Count > 0) MaterialCosts.Replace(result.MaterialCosts, source, CostSource.Excel);
        var stored = Repository.AppendLegacy(result.Pallets);
        Audit.Record(new[] { new AuditEntry(0, "MIGRATION", "Excel 2.0 history", null,
            $"{stored.Added} added, {stored.AlreadyLoaded} already loaded, {stored.Failed.Count} refused · {Path.GetFileName(path)}",
            ntUser, Clock.UtcNow, null) });
        return new LegacyLoad(result, stored);
    }

    /// <summary>Owner: loads the MM60 export and completes the cost of open records still without it.</summary>
    public Mm60ImportSummary ImportMm60(string path, string ownerNtUser) =>
        Mm60Import.Run(path, MaterialCosts, Repository, Audit, Clock, Pallets.Parameters, ownerNtUser);
}

/// <summary>Local mode: feedback in %LOCALAPPDATA%\ExpoReturn\feedback.csv.</summary>
public sealed class CsvFeedbackStore : IFeedbackStore
{
    public string Location { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExpoReturn", "feedback.csv");

    public void Add(FeedbackItem item)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Location)!);
        var isNew = !File.Exists(Location);
        static string Q(string s) => "\"" + s.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
        var sb = new StringBuilder();
        if (isNew) sb.AppendLine("Date,WindowsUser,Function,Screen,Type,Comment");
        sb.AppendLine(string.Join(",", Q(item.AtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")), Q(item.NtUser), Q(item.Function),
            Q(item.Screen), Q(item.Type), Q(item.Text.Trim())));
        File.AppendAllText(Location, sb.ToString(), new UTF8Encoding(true));
    }
}
