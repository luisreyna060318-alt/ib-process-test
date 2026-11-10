using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExpoReturn.Application;
using ExpoReturn.Domain;

namespace ExpoReturn.Desktop.ViewModels;

public sealed partial class SelectableValueStream : ObservableObject
{
    public SelectableValueStream(int id, string code, bool selected) { Id = id; Code = code; _isSelected = selected; }
    public int Id { get; }
    public string Code { get; }
    [ObservableProperty] private bool _isSelected;
}

/// <summary>
/// Start-up profile: the Windows user is read automatically; the user chooses department and Value Stream(s).
/// First run registers the profile with the Viewer function. Later, a change of department or VS becomes a request
/// that the Owner approves on the Users &amp; Access screen. The function is never chosen here.
/// </summary>
public sealed partial class ProfileViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly AppUser? _existing;

    public ProfileViewModel(AppServices services, string ntUser, bool firstRun)
    {
        _services = services;
        NtUser = ntUser;
        IsFirstRun = firstRun;
        _existing = services.Users.Find(ntUser);
        IsOwner = services.Users.IsOwner(ntUser);
        _displayName = _existing?.DisplayName ?? Environment.UserName;
        DepartmentOptions = Catalogs.Departments.Select(d => new LookupItem(d.Code, d.Code)).ToList();
        var pending = services.Users.PendingRequestFor(ntUser);
        var dept = pending?.DepartmentCode ?? _existing?.DepartmentCode;
        _selectedDepartment = DepartmentOptions.FirstOrDefault(o => (string?)o.Value == dept);
        var vss = pending?.ValueStreams ?? _existing?.ValueStreams ?? Array.Empty<int>();
        ValueStreams = Catalogs.ValueStreams.Select(v => new SelectableValueStream(v.Id, v.Code, vss.Contains(v.Id))).ToList();
        FunctionText = services.Users.ContextFor(ntUser).FunctionText;
        PendingText = pending is null ? null
            : $"Pending request sent {pending.RequestedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}: {pending.DepartmentCode} · {pending.ValueStreamText}. The Owner has not decided yet.";
    }

    public string NtUser { get; }
    public bool IsFirstRun { get; }
    public bool IsOwner { get; }
    public string WindowTitle => IsFirstRun ? "Welcome to Expo & Return FG Follow Up" : "My profile";
    public string Intro => IsFirstRun
        ? "This is the first time you open the application. Tell us your department and the Value Stream(s) you work in. You start as Viewer (read-only); the Owner assigns the Champion or Proxy function."
        : IsOwner
            ? "As Owner, department and Value Stream do not limit your access. Changes are applied directly."
            : "Changes to department or Value Stream are sent to the Owner for approval. Your current access stays as it is until then.";
    public IReadOnlyList<LookupItem> DepartmentOptions { get; }
    public IReadOnlyList<SelectableValueStream> ValueStreams { get; }
    public string FunctionText { get; }
    public string? PendingText { get; }
    public bool HasPending => PendingText is not null;

    [ObservableProperty] private string _displayName;
    [ObservableProperty] private LookupItem? _selectedDepartment;
    [ObservableProperty] private string? _message;

    /// <summary>Set by the view: closes the window with the given result.</summary>
    public Action<bool>? Close { get; set; }

    [RelayCommand]
    private void Save()
    {
        var dept = SelectedDepartment?.Value as string;
        var vss = ValueStreams.Where(v => v.IsSelected).Select(v => v.Id).ToList();
        if (string.IsNullOrWhiteSpace(DisplayName)) { Message = "Enter your name."; return; }
        if (!IsOwner && (dept is null || vss.Count == 0)) { Message = "Select your department and at least one Value Stream."; return; }
        dept ??= "QMM";

        if (_existing is null)
        {
            _services.Users.Register(NtUser, DisplayName, dept, vss);
            Close?.Invoke(true);
            return;
        }

        if (!string.Equals(_existing.DisplayName, DisplayName.Trim(), StringComparison.Ordinal))
            _services.Users.UpdateDisplayName(NtUser, DisplayName);

        var changed = !string.Equals(_existing.DepartmentCode, dept, StringComparison.OrdinalIgnoreCase)
                      || !_existing.ValueStreams.OrderBy(v => v).SequenceEqual(vss.OrderBy(v => v));
        if (changed)
        {
            var request = _services.Users.RequestChange(NtUser, dept, vss);
            if (IsOwner) _services.Users.Decide(request.Id, approve: true, NtUser, "Owner updated own profile");
            else MessageBox.Show("Your request was sent to the Owner. Your current access does not change until it is approved.",
                "Profile change requested", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        Close?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => Close?.Invoke(false);
}
