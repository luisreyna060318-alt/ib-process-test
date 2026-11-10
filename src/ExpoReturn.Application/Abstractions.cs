using ExpoReturn.Domain;

namespace ExpoReturn.Application;

public interface IClock { DateTime Today { get; } DateTime UtcNow { get; } }

public sealed class SystemClock : IClock
{
    public DateTime Today => DateTime.Today;
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>Persistencia de pallets. Borrador: en memoria. Producción: EF Core + SQL Server.</summary>
public interface IPalletReturnRepository
{
    IReadOnlyList<PalletReturn> GetAll();
    PalletReturn? Get(int id);
    void Save(PalletReturn pallet);
    int NextFolio(int valueStreamId);
    void ReplaceAll(IEnumerable<PalletReturn> pallets);
    event EventHandler? Changed;
}

public sealed record AuditEntry(int PalletId, string FolioCode, string Field, string? OldValue, string? NewValue,
                                string NtUser, DateTime ChangedAtUtc, string? Reason)
{
    public DateTime ChangedAtLocal => ChangedAtUtc.ToLocalTime();
}

public interface IAuditTrail
{
    void Record(IEnumerable<AuditEntry> entries);
    IReadOnlyList<AuditEntry> For(int palletId);
    IReadOnlyList<AuditEntry> All();
}

/// <summary>Atención de una alerta (UX-B09): quién la atendió, cuándo y con qué comentario.</summary>
public sealed record AlertAcknowledgement(int PalletId, AlertType Type, string NtUser, string Comment, DateTime AtUtc);

public interface IAlertAcknowledgementStore
{
    void Acknowledge(AlertAcknowledgement ack);
    AlertAcknowledgement? Find(int palletId, AlertType type);
    void Clear();
}

// =====================================================================
// Users and access (IB_PROCESS_USERS, IB_PROCESS_USER_ACCESS, IB_PROCESS_PROFILE_REQUESTS)
// =====================================================================

/// <summary>User profile. NtUser is read from Windows (DOMAIN\user); department and Value Streams are chosen
/// by the user on first run. IsDemo = fictitious demo user that is never saved.</summary>
public sealed record AppUser(string NtUser, string DisplayName, string? DepartmentCode, IReadOnlyList<int> ValueStreams,
                             DateTime RegisteredAtUtc, bool IsDemo = false)
{
    public string ValueStreamText => ValueStreams.Count == 0 ? "—" : string.Join(", ", ValueStreams.Select(v => $"VS{v}"));
}

/// <summary>One access row: user × department × Value Stream × function. Owner rows use department "" and VS 0.</summary>
public sealed record UserAccess(string NtUser, string DepartmentCode, int ValueStreamId, UserFunction Function)
{
    public string Describe => Function == UserFunction.Owner
        ? $"{NtUser} · Owner"
        : $"{NtUser} · {DepartmentCode} · VS{ValueStreamId} · {Function}";
}

public enum RequestStatus { Pending, Approved, Rejected }

/// <summary>A user's request to change department or Value Streams; applied only when the Owner approves it.</summary>
public sealed record ProfileRequest(int Id, string NtUser, string DepartmentCode, IReadOnlyList<int> ValueStreams, DateTime RequestedAtUtc,
                                    RequestStatus Status = RequestStatus.Pending, string? DecidedBy = null,
                                    DateTime? DecidedAtUtc = null, string? Comment = null)
{
    public string ValueStreamText => string.Join(", ", ValueStreams.Select(v => $"VS{v}"));
}

/// <summary>
/// Users, their access and profile requests. Draft: JSON file (path in appsettings.json). Production: Oracle tables.
/// Owners are configured by Windows user (appsettings.json "Owners") or by an Owner access row.
/// </summary>
public interface IUserDirectory
{
    event EventHandler? Changed;
    AppUser? Find(string ntUser);
    IReadOnlyList<AppUser> Users();
    IReadOnlyList<UserAccess> Access();
    bool IsOwner(string ntUser);
    UserContext ContextFor(string ntUser);
    /// <summary>First run: creates the profile with Viewer access in each selected VS.</summary>
    void Register(string ntUser, string displayName, string departmentCode, IReadOnlyList<int> valueStreams);
    void UpdateDisplayName(string ntUser, string displayName);
    /// <summary>Later changes of department / VS: stored as a request for the Owner.</summary>
    ProfileRequest RequestChange(string ntUser, string departmentCode, IReadOnlyList<int> valueStreams);
    IReadOnlyList<ProfileRequest> Requests(bool pendingOnly = true);
    ProfileRequest? PendingRequestFor(string ntUser);
    void Decide(int requestId, bool approve, string ownerNtUser, string? comment);
    /// <summary>Owner saves the access table (replaces every row of the users included).</summary>
    void ReplaceAccess(IEnumerable<UserAccess> rows);
    /// <summary>QMM Champions/Proxies of a Value Stream (QMM Responsible options).</summary>
    IReadOnlyList<string> QmmFor(int valueStreamId);
}
