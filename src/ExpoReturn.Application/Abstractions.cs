using ExpoReturn.Domain;

namespace ExpoReturn.Application;

public interface IClock { DateTime Today { get; } DateTime UtcNow { get; } }

public sealed class SystemClock : IClock
{
    public DateTime Today => DateTime.Today;
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>
/// IB PROCESS persistence (IB_PROCESS_PROCESSES + IB_PROCESS_PALLETS). The whole process (header and its pallets) is
/// saved in one transaction; a pallet that is no longer in the process is deleted with it.
/// </summary>
public interface IProcessRepository
{
    /// <summary>Every process. Only for small stores and tests: the screens use <see cref="IProcessQueries"/> page by page.</summary>
    IReadOnlyList<IbProcess> GetAll();
    IbProcess? Get(int processId);
    /// <summary>The pallet with its whole process (alerts open a pallet).</summary>
    PalletReturn? GetPallet(int palletId) => GetAll().SelectMany(p => p.Pallets).FirstOrDefault(p => p.Id == palletId);
    /// <summary>
    /// Folios of the OTHER processes that use each PO (<paramref name="excludeProcessId"/> = the process being saved, 0 = none).
    /// Oracle answers with one query; it never loads every process.
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyList<string>> FoliosUsingPo(IReadOnlyCollection<string> poNumbers, int excludeProcessId) =>
        poNumbers.ToDictionary(po => po, po => (IReadOnlyList<string>)GetAll()
            .Where(o => o.Id != excludeProcessId
                        && o.Pallets.Any(x => string.Equals(x.ReturnPONumber?.Trim(), po, StringComparison.OrdinalIgnoreCase)))
            .Select(o => o.FolioCode).ToList(), StringComparer.OrdinalIgnoreCase);
    /// <summary>Processes with a pallet still without MM60 cost (UNIT_COST_SOURCE = PENDING).</summary>
    IReadOnlyList<IbProcess> WithPendingCost() => GetAll().Where(x => x.Pallets.Any(p => p.UnitCostSource == "PENDING")).ToList();
    /// <summary>
    /// Insert (Id = 0) or update the header and every pallet; pallets missing from <paramref name="process"/> are deleted.
    /// Throws <see cref="ConcurrencyException"/> if someone else saved the header or a pallet first.
    /// </summary>
    void Save(IbProcess process);
    /// <summary>Deletes the process with its pallets, status history and alerts.</summary>
    void Delete(int processId);
    int NextFolio(int valueStreamId);
    /// <summary>Reloads from the store (other users may have saved records).</summary>
    void Refresh();
    /// <summary>
    /// Adds the Excel 2.0 rows as working records (one process with one pallet per row; IsLegacy marks the origin).
    /// Rows already loaded (same sheet and row) are skipped, so edits made after a first import are never overwritten.
    /// </summary>
    LegacyAppendResult AppendLegacy(IEnumerable<IbProcess> processes);
    event EventHandler? Changed;
}

/// <summary>A comment on an IB PROCESS (Comments tab). Never edited; deleted only with its process.</summary>
public sealed record ProcessComment(int Id, int ProcessId, string Text, string NtUser, string? DepartmentCode, DateTime CreatedAtUtc)
{
    public DateTime CreatedAtLocal => CreatedAtUtc.ToLocalTime();
    public string Author => DepartmentCode is null ? NtUser : $"{NtUser} · {DepartmentCode}";
}

/// <summary>IB_PROCESS_COMMENTS.</summary>
public interface IProcessComments
{
    /// <summary>Comments of the process, oldest first.</summary>
    IReadOnlyList<ProcessComment> For(int processId);
    ProcessComment Add(int processId, string text, string ntUser, string? departmentCode);
}

public sealed record LegacyAppendResult(int Added, int AlreadyLoaded, IReadOnlyList<string> Failed);

/// <summary>The record was changed by another user after it was opened.</summary>
public sealed class ConcurrencyException(string message) : Exception(message);

/// <summary>A rule of the data store was broken (e.g. emptying a filled field).</summary>
public sealed class DataRuleException(string message) : Exception(message);

/// <param name="PalletId">Pallet changed (0 = a field of the process header, or not a record).</param>
/// <param name="ProcessId">IB PROCESS the entry belongs to (0 = users, MM60, departments…).</param>
/// <param name="Deleted">The record was deleted; <paramref name="Snapshot"/> keeps it as it was.</param>
public sealed record AuditEntry(int PalletId, string FolioCode, string Field, string? OldValue, string? NewValue,
                                string NtUser, DateTime ChangedAtUtc, string? Reason, int ProcessId = 0,
                                bool Deleted = false, string? Snapshot = null)
{
    public DateTime ChangedAtLocal => ChangedAtUtc.ToLocalTime();
}

public interface IAuditTrail
{
    void Record(IEnumerable<AuditEntry> entries);
    /// <summary>Every entry of an IB PROCESS (header and pallets).</summary>
    IReadOnlyList<AuditEntry> For(int processId);
    IReadOnlyList<AuditEntry> All();
    /// <summary>Audit log screen: entries of the filter, most recent first, one page at a time (Oracle: OFFSET … FETCH).</summary>
    IReadOnlyList<AuditEntry> Page(AuditFilter filter, int skip, int take);
    /// <summary>How many entries match the filter (every page).</summary>
    int Count(AuditFilter filter);
    /// <summary>Users and fields that appear in the log (options of the filters).</summary>
    IReadOnlyList<string> Users();
    IReadOnlyList<string> Fields();
}

/// <summary>
/// Filters of the audit log screen (all optional, combined with AND): folio contains the text (case-insensitive), exact
/// user, exact field (property name), and the day the change was made (local time) between From and To, both included.
/// </summary>
public sealed record AuditFilter(string? Folio = null, string? NtUser = null, string? Field = null, DateTime? From = null, DateTime? To = null)
{
    public static AuditFilter All { get; } = new();
    public string? FolioText => string.IsNullOrWhiteSpace(Folio) ? null : Folio.Trim();

    /// <summary>Same rule as the SQL of OracleAuditTrail.</summary>
    public bool Matches(AuditEntry e)
    {
        if (FolioText is { } f && !(e.FolioCode ?? "").Contains(f, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(NtUser) && !string.Equals(e.NtUser, NtUser, StringComparison.Ordinal)) return false;
        if (!string.IsNullOrEmpty(Field) && e.Field != Field) return false;
        var day = e.ChangedAtLocal.Date;
        if (From is { } from && day < from.Date) return false;
        if (To is { } to && day > to.Date) return false;
        return true;
    }
}

/// <summary>How an alert was handled: who, when and with which comment (IB_PROCESS_ALERTS).</summary>
public sealed record AlertAcknowledgement(int PalletId, AlertType Type, string NtUser, string Comment, DateTime AtUtc,
                                          Severity Severity = Severity.Info, string? Message = null, string? ResponsibleArea = null);

/// <summary>
/// Owner maintenance of IB_PROCESS_DEPARTMENTS. A department is never deleted: it is deactivated.
/// Code and area do not change once created (records and access rows use them).
/// </summary>
public interface IDepartmentAdmin
{
    IReadOnlyList<Department> All();
    void Add(Department department);
    void Update(string code, bool canRequest, bool isActive);
}

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
/// <param name="IsRegistration">First run: approving makes the user a Member in the department and VS asked for.</param>
public sealed record ProfileRequest(int Id, string NtUser, string DepartmentCode, IReadOnlyList<int> ValueStreams, DateTime RequestedAtUtc,
                                    RequestStatus Status = RequestStatus.Pending, string? DecidedBy = null,
                                    DateTime? DecidedAtUtc = null, string? Comment = null, bool IsRegistration = false)
{
    public string TypeText => IsRegistration ? "New user" : "Profile change";
    public string ValueStreamText => string.Join(", ", ValueStreams.Select(v => $"VS{v}"));
}

/// <summary>
/// Users, their access and profile requests, in the Oracle tables (a JSON store exists only for the tests).
/// Owners: the built-in Windows user of the application, plus the OWNER rows of IB_PROCESS_USER_ACCESS.
/// </summary>
public interface IUserDirectory
{
    event EventHandler? Changed;
    AppUser? Find(string ntUser);
    IReadOnlyList<AppUser> Users();
    IReadOnlyList<UserAccess> Access();
    bool IsOwner(string ntUser);
    UserContext ContextFor(string ntUser);
    /// <summary>
    /// First run: creates the profile with Viewer access in each selected VS and a registration request for the Owner
    /// (approved = Member). Department may be empty only for an Owner, who needs no request.
    /// </summary>
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
    /// a changed function closes the old row and opens a new one. A user that does not exist yet is created with the name
    /// given in <paramref name="newUserNames"/> (Windows user → name; the Windows user itself when there is none).
    /// </summary>
    void ReplaceAccess(IEnumerable<UserAccess> rows, string changedBy = "SYSTEM", IReadOnlyDictionary<string, string>? newUserNames = null);
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
