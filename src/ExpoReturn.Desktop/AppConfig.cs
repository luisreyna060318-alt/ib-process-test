using ExpoReturn.Infrastructure.Oracle;

namespace ExpoReturn.Desktop;

/// <summary>
/// Application settings, fixed in the code: there is no configuration file. The user only needs ExpoReturn.Desktop.exe
/// (SharedServersAccess.dll travels inside it). What changes over time is managed in the database instead: catalogs,
/// field permissions, parameters and who is Owner (OWNER rows of IB_PROCESS_USER_ACCESS).
/// </summary>
public sealed class AppConfig
{
    public const string Version = "0.4.2";

    /// <summary>IFACTJUP through SharedServersAccess; tnsnames.ora from TNS_ADMIN or %ORACLE_HOME%\network\admin.</summary>
    public OracleSettings Oracle { get; } = new();

    /// <summary>First Owner (process owner of the application). More Owners: OWNER rows in IB_PROCESS_USER_ACCESS.</summary>
    public IReadOnlyList<string> Owners { get; } = new[] { "US\\RLN8JU" };

    /// <summary>Windows user running the application (DOMAIN\user).</summary>
    public static string CurrentWindowsUser => $"{Environment.UserDomainName}\\{Environment.UserName}".ToUpperInvariant();
}
