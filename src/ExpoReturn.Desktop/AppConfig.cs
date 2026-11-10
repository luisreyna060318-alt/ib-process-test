using System.IO;
using System.Text.Json;

namespace ExpoReturn.Desktop;

/// <summary>
/// appsettings.json next to the executable. Created with defaults on first run if it does not exist.
/// <code>
/// {
///   "Owners": [ "DOMAIN\\owner.user" ],          // Windows users with the Owner function
///   "UserStorePath": "",                          // users file; empty = %LOCALAPPDATA%\ExpoReturn\users.json
///   "DemoData": true,                             // load synthetic pallets
///   "DemoUsers": true                             // fictitious users so the Owner can preview other functions
/// }
/// </code>
/// In production the users file is replaced by the Oracle tables IB_PROCESS_USERS / IB_PROCESS_USER_ACCESS.
/// </summary>
public sealed class AppConfig
{
    public List<string> Owners { get; set; } = new();
    public string? UserStorePath { get; set; }
    public bool DemoData { get; set; } = true;
    public bool DemoUsers { get; set; } = true;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

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
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Options) ?? new AppConfig();
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
