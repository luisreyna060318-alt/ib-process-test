using System.IO;
using System.Text.Json;
using ExpoReturn.Infrastructure.Oracle;

namespace ExpoReturn.Desktop;

/// <summary>
/// appsettings.json next to the executable. Created with defaults on first run if it does not exist.
/// The application always works on the Oracle database (there is no local mode): if it cannot connect, it does not open.
/// <code>
/// {
///   "Oracle": {
///     "Server": "IFACTJUP",                       // server title in SharedServersAccess (= TNS alias)
///     "SharedServersDll": "SharedServersAccess.dll",
///     "SharedServersIni": "",                     // optional servers.ini path; empty = the DLL default
///     "TnsAdmin": "",                             // folder of tnsnames.ora; empty = TNS_ADMIN / ORACLE_HOME
///     "Schema": "",                               // owner of the tables if it is not the connected account
///     "MasterKeyEnvVar": "SHARED_SERVERS_CLAVE",  // optional override of the master key
///     "MasterKey": null                           // optional override; empty = built-in key (automatic connection)
///   },
///   "Owners": [ "US\\RLN8JU" ]                   // Windows users with the Owner function
/// }
/// </code>
/// </summary>
public sealed class AppConfig
{
    public const string Version = "0.4.0";

    public OracleSettings Oracle { get; set; } = new();
    /// <summary>Default Owner: US\RLN8JU (process owner of the application). More can be added here or in IB_PROCESS_USER_ACCESS.</summary>
    public List<string> Owners { get; set; } = new() { "US\\RLN8JU" };

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

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
                // File of an older version (local mode keys): rewrite it with the current keys, keeping Oracle and Owners.
                if (text.Contains("\"DataSource\"", StringComparison.Ordinal) || text.Contains("\"DemoData\"", StringComparison.Ordinal))
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
