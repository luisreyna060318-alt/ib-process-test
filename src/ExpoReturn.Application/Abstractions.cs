using ExpoReturn.Domain;

namespace ExpoReturn.Application;

public interface IClock { DateTime Today { get; } DateTime UtcNow { get; } }

public sealed class SystemClock : IClock
{
    public DateTime Today => DateTime.Today;
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>
/// Pallet persistence. Draft: in memory. Production: Oracle IB_PROCESS_PALLET_RETURNS.
/// There is no delete: a record can only be created or updated.
/// </summary>
public interface IPalletReturnRepository
{
    IReadOnlyList<PalletReturn> GetAll();
    PalletReturn? Get(int id);
    /// <summary>Insert (Id = 0) or update. Throws <see cref="ConcurrencyException"/> if someone else saved the record first.</summary>
    void Save(PalletReturn pallet);
    int NextFolio(int valueStreamId);
    /// <summary>Reloads from the store (other users may have saved records).</summary>
    void Refresh();
    /// <summary>
    /// Adds the Excel 2.0 rows as working records (editable, IsLegacy marks the origin). Rows already loaded (same sheet
    /// and row) are skipped, so edits made after a first import are never overwritten; nothing is replaced or deleted.
    /// </summary>
    LegacyAppendResult AppendLegacy(IEnumerable<PalletReturn> pallets);
    event EventHandler? Changed;
}

public sealed record LegacyAppendResult(int Added, int AlreadyLoaded, IReadOnlyList<string> Failed);

/// <summary>The record was changed by another user after it was opened.</summary>
public sealed class ConcurrencyException(string message) : Exception(message);

/// <summary>A rule of the data store was broken (e.g. emptying a filled field, deleting).</summary>
public sealed class DataRuleException(string message) : Exception(message);

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

/// <summary>How an alert was handled (UX-B09): who, when and with which comment (IB_PROCESS_ALERTS).</summary>
public sealed record AlertAcknowledgement(int PalletId, AlertType Type, string NtUser, string Comment, DateTime AtUtc,
                                          Severity Severity = Severity.Info, string? Message = null, string? ResponsibleArea = null);

/// <summary>Reviewer feedback ("Send feedback"): local CSV or IB_PROCESS_FEEDBACK.</summary>
public sealed record FeedbackItem(string NtUser, string Function, string Screen, string Type, string Text, DateTime AtUtc);

public interface IFeedbackStore
{
    string Location { get; }
    void Add(FeedbackItem item);
}

public interface IAlertAcknowledgementStore
{
    void Acknowledge(AlertAcknowledgement ack);
    AlertAcknowledgement? Find(int palletId, AlertType type);
    void Refresh() { }
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
    /// <summary>First run: creates the profile with Viewer access in each selected VS (department may be empty only for an Owner).</summary>
    void Register(string ntUser, string displayName, string? departmentCode, IReadOnlyList<int> valueStreams);
    /// <summary>Reloads users, access and requests from the store (other people may have changed them).</summary>
    void Refresh();
    void UpdateDisplayName(string ntUser, string displayName);
    /// <summary>Later changes of department / VS: stored as a request for the Owner.</summary>
    ProfileRequest RequestChange(string ntUser, string departmentCode, IReadOnlyList<int> valueStreams);
    IReadOnlyList<ProfileRequest> Requests(bool pendingOnly = true);
    ProfileRequest? PendingRequestFor(string ntUser);
    void Decide(int requestId, bool approve, string ownerNtUser, string? comment);
    /// <summary>
    /// Owner saves the whole access grid. Nothing is deleted: rows that disappear are closed (VALID_TO) and kept as history;
    /// a changed function closes the old row and opens a new one.
    /// </summary>
    void ReplaceAccess(IEnumerable<UserAccess> rows, string changedBy = "SYSTEM");
    /// <summary>QMM Champions/Proxies of a Value Stream (QMM Responsible options).</summary>
    IReadOnlyList<string> QmmFor(int valueStreamId);
}

/// <summary>Users, access and requests as stored (active access rows only).</summary>
public sealed record UserStoreSnapshot(IReadOnlyList<AppUser> Users, IReadOnlyList<UserAccess> Access, IReadOnlyList<ProfileRequest> Requests)
{
    public static UserStoreSnapshot Empty { get; } = new(Array.Empty<AppUser>(), Array.Empty<UserAccess>(), Array.Empty<ProfileRequest>());
}

/// <summary>One save of the users directory, as incremental changes. There is no "delete" on purpose.</summary>
public sealed class UserStoreChanges
{
    public string ChangedBy { get; init; } = "SYSTEM";
    public List<AppUser> NewUsers { get; } = new();
    public List<AppUser> UpdatedUsers { get; } = new();
    /// <summary>Access rows that end (closed with VALID_TO / REMOVED_BY; the row stays as history).</summary>
    public List<UserAccess> EndedAccess { get; } = new();
    public List<UserAccess> AddedAccess { get; } = new();
    /// <summary>New requests; their Id is temporary until the store assigns the real one.</summary>
    public List<ProfileRequest> NewRequests { get; } = new();
    public List<ProfileRequest> DecidedRequests { get; } = new();
    public bool IsEmpty => NewUsers.Count + UpdatedUsers.Count + EndedAccess.Count + AddedAccess.Count + NewRequests.Count + DecidedRequests.Count == 0;
}

/// <summary>Where the users directory is kept: JSON file (draft / pilot) or the Oracle IB_PROCESS_ tables.</summary>
public interface IUserStoreBackend
{
    string Description { get; }
    UserStoreSnapshot Load();
    /// <summary>Writes the changes in one transaction. Returns the real Id of each new request (temporary Id → stored Id).</summary>
    IReadOnlyDictionary<int, int> Apply(UserStoreChanges changes, UserStoreSnapshot after);
}

// =====================================================================
// MM60 cost master (IB_PROCESS_MM60_COSTS). Loaded from the MM60 report exported from SAP
// (the same data the Excel kept in the hidden sheet "MM60 COST SAP"); there is no live SAP link.
// =====================================================================

/// <summary>One material of the MM60 export. Unit cost = Price / Price unit (same rule as the Excel).</summary>
public sealed record MaterialCost(string Material, string? Description, decimal Price, decimal PriceUnit, string Currency)
{
    public decimal UnitCostUsd => PriceUnit <= 0 ? 0 : Math.Round(Price / PriceUnit, 6, MidpointRounding.AwayFromZero);
}

/// <summary>Origin of the MM60 data (IB_PROCESS_MM60_COSTS.SOURCE).</summary>
public enum CostSource { Excel, Mm60File }

public interface IMaterialCostCatalog
{
    MaterialCost? Find(string? material);
    int Count { get; }
    string? SourceName { get; }
    DateTime? LoadedAtUtc { get; }
    /// <summary>
    /// Loads a new MM60 export. Materials in the file take the new price; materials not in the file keep the last one
    /// (nothing is deleted). In Oracle a changed price closes the old row (VALID_TO) and adds a new one.
    /// </summary>
    void Replace(IEnumerable<MaterialCost> costs, string sourceName, CostSource source = CostSource.Mm60File);
    /// <summary>Reloads from the store.</summary>
    void Refresh() { }
}
