using System.IO;
using ExpoReturn.Application;
using ExpoReturn.Domain;
using ExpoReturn.Infrastructure.InMemory;
using ExpoReturn.Infrastructure.Oracle;

namespace ExpoReturn.Desktop;

/// <summary>
/// Service composition. The application always works on the Oracle IB_PROCESS_ tables, with the account
/// SharedServersAccess.dll returns: there is no local or demo mode, so nothing can be "saved" somewhere else by mistake.
/// Catalogs (VS, reasons, status categories, departments, status names, field permissions) and parameters come from the
/// database too. Nothing is ever deleted.
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

    public string DataSourceLabel { get; set; } = "";

    /// <summary>Reads the catalogs managed in the database again.</summary>
    private Action? ReloadCatalogs { get; init; }

    /// <summary>
    /// Connects: master key (built-in, or override) → account from SharedServersAccess → check of the IB_PROCESS_ tables →
    /// catalogs, parameters and repositories. Throws if anything fails (the start-up window retries).
    /// </summary>
    public static AppServices Connect(AppConfig config)
    {
        var credentials = new SharedServersCredentials(config.Oracle);
        credentials.Initialize(config.Oracle.ResolveMasterKey());
        var db = new OracleDb(config.Oracle, credentials);
        var check = db.Check();
        OracleCatalogs.LoadAndUse(db);                                 // catalogs are managed in the database
        var clock = new SystemClock();
        var parameters = OracleParameters.Load(db);
        var repo = new OraclePalletRepository(db, parameters, () => clock.Today);
        var audit = new OracleAuditTrail(db, AppConfig.Version);
        var users = new UserDirectory(new OracleUserStore(db), config.Owners, includeDemoUsers: false);
        return new AppServices
        {
            Config = config, Repository = repo, Audit = audit, Users = users, Clock = clock,
            Pallets = new PalletReturnService(repo, audit, clock, parameters),
            Reporting = new ReportingService(repo, clock),
            AlertAcks = new OracleAlertStore(db),
            MaterialCosts = new OracleMaterialCostCatalog(db),
            Feedback = new OracleFeedbackStore(db),
            DataSourceLabel = check,
            ReloadCatalogs = () => OracleCatalogs.LoadAndUse(db),
        };
    }

    // ------------------------------------------------------------------ actions

    /// <summary>Reloads catalogs and what other users saved.</summary>
    public void Reload()
    {
        ReloadCatalogs?.Invoke();
        Repository.Refresh();
        Users.Refresh();
        AlertAcks.Refresh();
        MaterialCosts.Refresh();
    }

    public sealed record LegacyLoad(LegacyImportResult Read, LegacyAppendResult Stored, int CostsCompleted);

    /// <summary>
    /// Excel 2.0 → working records (Owner): the rows not loaded yet are inserted in IB_PROCESS_PALLET_RETURNS and can then
    /// be edited to homogenize the data; rows already loaded are skipped, so edits are never overwritten. The hidden sheet
    /// MM60 COST SAP goes to IB_PROCESS_MM60_COSTS and completes the records without cost.
    /// </summary>
    public LegacyLoad ImportLegacy(string path, string ntUser)
    {
        var result = LegacyExcelImporter.Import(path, Clock.Today);
        if (result.MaterialCosts.Count > 0)
            MaterialCosts.Replace(result.MaterialCosts, $"{Path.GetFileName(path)} · sheet MM60 COST SAP", CostSource.Excel);
        var stored = Repository.AppendLegacy(result.Pallets);
        var costs = Mm60Import.CompletePendingCosts(MaterialCosts, Repository, Audit, Clock, Pallets.Parameters, $"Excel 2.0 import by {ntUser}");
        Audit.Record(new[] { new AuditEntry(0, "MIGRATION", "Excel 2.0 import", null,
            $"{stored.Added} added, {stored.AlreadyLoaded} already loaded, {stored.Failed.Count} refused, {costs} costs completed · {Path.GetFileName(path)}",
            ntUser, Clock.UtcNow, null) });
        return new LegacyLoad(result, stored, costs);
    }

    /// <summary>Owner: loads the MM60 export and completes the cost of open records still without it.</summary>
    public Mm60ImportSummary ImportMm60(string path, string ownerNtUser) =>
        Mm60Import.Run(path, MaterialCosts, Repository, Audit, Clock, Pallets.Parameters, ownerNtUser);
}
