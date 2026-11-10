using System.IO;
using System.Text.Json;

namespace ExpoReturn.Desktop;

/// <summary>Last inbox filters per user (UX-B06), stored in %LOCALAPPDATA%\ExpoReturn\preferences.json.</summary>
public sealed class ListFilterPreference
{
    public int? ValueStreamId { get; set; }
    public string? Status { get; set; }
    public string? Search { get; set; }
    public bool OnlyDutyRisk { get; set; }
}

public sealed class UserPreferences
{
    public Dictionary<string, ListFilterPreference> ListFilters { get; set; } = new();

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExpoReturn", "preferences.json");

    public static UserPreferences Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(FilePath)) ?? new UserPreferences()
                : new UserPreferences();
        }
        catch { return new UserPreferences(); }     // preferencia dañada: se ignora
    }

    public ListFilterPreference? GetListFilter(string ntUser) => ListFilters.GetValueOrDefault(ntUser);

    public void SetListFilter(string ntUser, ListFilterPreference pref)
    {
        ListFilters[ntUser] = pref;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* no bloquear la captura por un problema de disco */ }
    }
}
