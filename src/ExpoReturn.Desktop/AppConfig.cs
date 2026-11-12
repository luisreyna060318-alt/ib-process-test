using System.IO;
using System.Text.Json;
using ExpoReturn.Infrastructure.Oracle;

namespace ExpoReturn.Desktop;

/// <summary>
/// appsettings.json next to the executable. Created with defaults on first run if it does not exist.
/// <code>
/// {
///   "DataSource": "Oracle",                       // "Oracle" = IB_PROCESS_ tables · "Local" = demo in memory + users file
///   "Oracle": {
///     "Server": "IFACTJUP",                       // server title in SharedServersAccess (= TNS alias)
///     "SharedServersDll": "SharedServersAccess.dll",
///     "SharedServersIni": "",                     // optional servers.ini path; empty = the DLL default
///     "TnsAdmin": "",                             // folder of tnsnames.ora; empty = TNS_ADMIN / ORACLE_HOME
///     "Schema": "",                               // owner of the tables if it is not the connected account
///     "MasterKeyEnvVar": "SHARED_SERVERS_CLAVE"   // master key; if empty the app asks for it (never stored)
///   },
///   "Owners": [ "US\\RLN8JU" ],                  // Windows users with the Owner function
///   "UserStorePath": "",                          // Local mode: users file; empty = %LOCALAPPDATA%\ExpoReturn\users.json
///   "DemoData": true,                             // Local mode: load synthetic pallets
///   "DemoUsers": true                             // fictitious users (memory only) so the Owner can preview other functions
/// }
/// </code>
/// </summary>
public sealed class AppConfig
{
    /// <summary>Default Owner: US\RLN8JU (process owner of the application). More can be added here or in IB_PROCESS_USER_ACCESS.</summary>
    public const string Version = "0.3.5";

    /// <summary>"Oracle" (production: IB_PROCESS_ tables through SharedServersAccess) or "Local" (demo).</summary>
    public string DataSource { get; set; } = "Oracle";
    public OracleSettings Oracle { get; set; } = new();
    public List<string> Owners { get; set; } = new() { "US\\RLN8JU" };
    public string? UserStorePath { get; set; }
    public bool DemoData { get; set; } = true;
    public bool DemoUsers { get; set; } = true;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    public bool UsesOracle => string.Equals(DataSource, "Oracle", StringComparison.OrdinalIgnoreCase);

    public string ResolvedUserStorePath => string.IsNullOrWhiteSpace(UserStorePath)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExpoReturn", "users.json")
        : Environment.ExpandEnvironmentVariables(UserStorePath);

    /// <summary>Windows user running the application (DOMAIN\user).</summary>
    public static string CurrentWindowsUser => $"{Environment.UserDomainName}\\{Environment.UserName}".ToUpperInvariant();

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var text = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<AppConfig>(text, Options) ?? new AppConfig();
                // File of an older version: add the new keys (Oracle connection) keeping the values it had.
                if (!text.Contains("\"DataSource\"", StringComparison.Ordinal))
                    try { File.WriteAllText(FilePath, JsonSerializer.Serialize(loaded, Options)); } catch { /* read-only folder */ }
                return loaded;
            }
            var config = new AppConfig();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(config, Options));   // template the Owner can edit
            return config;
        }
        catch
        {
            return new AppConfig();   // read-only folder or invalid file: defaults
        }
    }
}
