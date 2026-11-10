using System.Globalization;
using System.Text;
using System.Text.Json;
using ExpoReturn.Application;

namespace ExpoReturn.Infrastructure.InMemory;

/// <summary>
/// Users, access rows and profile requests. The rules live here; where they are kept is the backend:
/// <see cref="JsonUserStore"/> (draft / pilot) or the Oracle store (IB_PROCESS_USERS, IB_PROCESS_USER_ACCESS,
/// IB_PROCESS_PROFILE_REQUESTS, IB_PROCESS_REQUEST_VS). Every save is sent as incremental changes and nothing is ever
/// deleted: an access row that ends is closed (history), a replaced request is marked rejected.
/// Demo users (fictitious, DEMO\...) live only in memory so the Owner can preview other functions; they are never saved.
/// </summary>
public sealed class UserDirectory : IUserDirectory
{
    private sealed class Store
    {
        public List<AppUser> Users { get; set; } = new();
        public List<UserAccess> Access { get; set; } = new();
        public List<ProfileRequest> Requests { get; set; } = new();
    }

    private readonly IUserStoreBackend? _backend;
    private readonly HashSet<string> _configuredOwners;
    private readonly object _gate = new();
    private Store _real = new();
    private readonly Store _demo = new();

    public event EventHandler? Changed;

    /// <param name="path">JSON file; null = memory only (tests).</param>
    /// <param name="owners">Windows users configured as Owner (DOMAIN\user or user).</param>
    /// <param name="includeDemoUsers">Adds the fictitious demo users (one per area and VS).</param>
    public UserDirectory(string? path, IEnumerable<string> owners, bool includeDemoUsers)
        : this(path is null ? null : new JsonUserStore(path), owners, includeDemoUsers) { }

    public UserDirectory(IUserStoreBackend? backend, IEnumerable<string> owners, bool includeDemoUsers)
    {
        _backend = backend;
        _configuredOwners = owners.Where(o => !string.IsNullOrWhiteSpace(o)).Select(Normalize).ToHashSet();
        Load();
        if (includeDemoUsers) AddDemoUsers();
    }

    public string StoreDescription => _backend?.Description ?? "Memory only";

    public static string Normalize(string ntUser) => ntUser.Trim().ToUpperInvariant();
    public static bool IsDemoUser(string ntUser) => Normalize(ntUser).StartsWith("DEMO\\", StringComparison.Ordinal);

    /// <summary>Fictitious network user: "Andrea Salinas" → DEMO\ANDREA.SALINAS.</summary>
    public static string DemoNtUser(string name)
    {
        var plain = new string(name.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        var parts = plain.ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => new string(p.Where(char.IsLetterOrDigit).ToArray())).Where(p => p.Length > 0);
        return "DEMO\\" + string.Join(".", parts);
    }

    private void Load()
    {
        if (_backend is null) return;
        var snap = _backend.Load();
        lock (_gate)
            _real = new Store
            {
                Users = snap.Users.Select(u => u with { NtUser = Normalize(u.NtUser) }).ToList(),
                Access = snap.Access.Select(a => a with { NtUser = Normalize(a.NtUser) }).ToList(),
                Requests = snap.Requests.Select(r => r with { NtUser = Normalize(r.NtUser) }).ToList(),
            };
    }

    public void Refresh()
    {
        Load();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------------ queries

    private IEnumerable<AppUser> AllUsers => _real.Users.Concat(_demo.Users);
    private IEnumerable<UserAccess> AllAccess => _real.Access.Concat(_demo.Access);
    private Store StoreOf(string key) => IsDemoUser(key) ? _demo : _real;

    public AppUser? Find(string ntUser)
    {
        var key = Normalize(ntUser);
        lock (_gate) return AllUsers.FirstOrDefault(u => u.NtUser == key);
    }

    public IReadOnlyList<AppUser> Users() { lock (_gate) return AllUsers.OrderBy(u => u.IsDemo).ThenBy(u => u.DisplayName).ToList(); }
    public IReadOnlyList<UserAccess> Access() { lock (_gate) return AllAccess.ToList(); }

    public bool IsOwner(string ntUser)
    {
        var key = Normalize(ntUser);
        var shortName = key.Contains('\\') ? key[(key.LastIndexOf('\\') + 1)..] : key;
        if (_configuredOwners.Contains(key) || _configuredOwners.Contains(shortName)) return true;
        lock (_gate) return AllAccess.Any(a => a.NtUser == key && a.Function == UserFunction.Owner);
    }

    public UserContext ContextFor(string ntUser)
    {
        var key = Normalize(ntUser);
        var user = Find(key);
        List<AccessGrant> grants;
        lock (_gate)
            grants = AllAccess.Where(a => a.NtUser == key && a.Function != UserFunction.Owner)
                .Select(a => new AccessGrant(a.DepartmentCode, a.ValueStreamId, a.Function)).ToList();
        return new UserContext(key, user?.DisplayName ?? key, user?.DepartmentCode, IsOwner(key), grants);
    }

    public IReadOnlyList<string> QmmFor(int valueStreamId)
    {
        lock (_gate)
        {
            var users = AllUsers.ToList();
            return AllAccess
                .Where(a => a.ValueStreamId == valueStreamId && a.Function is UserFunction.Champion or UserFunction.Proxy
                            && RoleInfo.AreaOfDepartment(a.DepartmentCode) == RoleCode.Qmm)
                .OrderBy(a => a.Function == UserFunction.Champion ? 0 : 1)
                .Select(a => users.FirstOrDefault(u => u.NtUser == a.NtUser)?.DisplayName ?? a.NtUser)
                .Distinct().ToList();
        }
    }

    public IReadOnlyList<ProfileRequest> Requests(bool pendingOnly = true)
    {
        lock (_gate)
            return _real.Requests.Concat(_demo.Requests)
                .Where(r => !pendingOnly || r.Status == RequestStatus.Pending).OrderByDescending(r => r.RequestedAtUtc).ToList();
    }

    public ProfileRequest? PendingRequestFor(string ntUser)
    {
        var key = Normalize(ntUser);
        lock (_gate) return StoreOf(key).Requests.LastOrDefault(r => r.NtUser == key && r.Status == RequestStatus.Pending);
    }

    // ------------------------------------------------------------------ commands

    public void Register(string ntUser, string displayName, string? departmentCode, IReadOnlyList<int> valueStreams)
    {
        var key = Normalize(ntUser);
        var changes = new UserStoreChanges { ChangedBy = key };
        lock (_gate)
        {
            if (AllUsers.Any(u => u.NtUser == key)) throw new InvalidOperationException($"{key} is already registered.");
            var dept = string.IsNullOrWhiteSpace(departmentCode) ? null : departmentCode.Trim();
            var vss = dept is null ? new List<int>() : valueStreams.Distinct().OrderBy(v => v).ToList();
            var user = new AppUser(key, Clean(displayName, key), dept, vss, DateTime.UtcNow, IsDemoUser(key));
            var store = StoreOf(key);
            store.Users.Add(user);
            changes.NewUsers.Add(user);
            foreach (var vs in vss)
            {
                var row = new UserAccess(key, dept!, vs, UserFunction.Viewer);
                store.Access.Add(row);
                changes.AddedAccess.Add(row);
            }
        }
        Persist(changes);
    }

    public void UpdateDisplayName(string ntUser, string displayName)
    {
        var key = Normalize(ntUser);
        if (string.IsNullOrWhiteSpace(displayName)) throw new DataRuleException("The name cannot be left empty.");
        var changes = new UserStoreChanges { ChangedBy = key };
        lock (_gate)
        {
            var store = StoreOf(key);
            var i = store.Users.FindIndex(u => u.NtUser == key);
            if (i < 0) return;
            store.Users[i] = store.Users[i] with { DisplayName = displayName.Trim() };
            changes.UpdatedUsers.Add(store.Users[i]);
        }
        Persist(changes);
    }

    public ProfileRequest RequestChange(string ntUser, string departmentCode, IReadOnlyList<int> valueStreams)
    {
        var key = Normalize(ntUser);
        if (string.IsNullOrWhiteSpace(departmentCode)) throw new DataRuleException("The department cannot be left empty.");
        var changes = new UserStoreChanges { ChangedBy = key };
        ProfileRequest request;
        var store = StoreOf(key);
        lock (_gate)
        {
            // A new request replaces the user's previous pending one (kept as rejected, not deleted).
            for (var i = 0; i < store.Requests.Count; i++)
                if (store.Requests[i].NtUser == key && store.Requests[i].Status == RequestStatus.Pending)
                {
                    store.Requests[i] = store.Requests[i] with
                    {
                        Status = RequestStatus.Rejected, DecidedBy = "SYSTEM", DecidedAtUtc = DateTime.UtcNow, Comment = "Replaced by a newer request",
                    };
                    changes.DecidedRequests.Add(store.Requests[i]);
                }
            var id = _real.Requests.Concat(_demo.Requests).Select(r => r.Id).DefaultIfEmpty(0).Max() + 1;
            request = new ProfileRequest(id, key, departmentCode.Trim(), valueStreams.Distinct().OrderBy(v => v).ToList(), DateTime.UtcNow);
            store.Requests.Add(request);
            changes.NewRequests.Add(request);
        }
        var ids = Persist(changes);
        if (ids.TryGetValue(request.Id, out var realId) && realId != request.Id)
            lock (_gate)
            {
                var i = store.Requests.FindIndex(r => r.Id == request.Id && r.NtUser == key && r.Status == RequestStatus.Pending);
                request = request with { Id = realId };
                if (i >= 0) store.Requests[i] = request;
            }
        return request;
    }

    /// <summary>
    /// Approve: the profile takes the new department / VS. VS removed lose their access rows (closed); new VS get Viewer rows.
    /// If the department changes, Champion/Proxy functions go back to Viewer (they belonged to the old department).
    /// </summary>
    public void Decide(int requestId, bool approve, string ownerNtUser, string? comment)
    {
        var owner = Normalize(ownerNtUser);
        var changes = new UserStoreChanges { ChangedBy = owner };
        lock (_gate)
        {
            var store = _real.Requests.Any(r => r.Id == requestId && r.Status == RequestStatus.Pending) ? _real : _demo;
            var i = store.Requests.FindIndex(r => r.Id == requestId && r.Status == RequestStatus.Pending);
            if (i < 0) return;
            var req = store.Requests[i];
            store.Requests[i] = req with
            {
                Status = approve ? RequestStatus.Approved : RequestStatus.Rejected,
                DecidedBy = owner, DecidedAtUtc = DateTime.UtcNow, Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            };
            changes.DecidedRequests.Add(store.Requests[i]);
            if (approve)
            {
                var u = store.Users.FindIndex(x => x.NtUser == req.NtUser);
                var oldDept = u >= 0 ? store.Users[u].DepartmentCode : null;
                if (u >= 0)
                {
                    store.Users[u] = store.Users[u] with { DepartmentCode = req.DepartmentCode, ValueStreams = req.ValueStreams };
                    changes.UpdatedUsers.Add(store.Users[u]);
                }
                var deptChanged = !string.Equals(oldDept, req.DepartmentCode, StringComparison.OrdinalIgnoreCase);
                var current = store.Access.Where(a => a.NtUser == req.NtUser).ToList();
                var target = current.Where(a => a.Function == UserFunction.Owner
                                                || (!deptChanged && req.ValueStreams.Contains(a.ValueStreamId))).ToList();
                foreach (var vs in req.ValueStreams.Where(vs => !target.Any(k => k.ValueStreamId == vs && k.Function != UserFunction.Owner)))
                    target.Add(new UserAccess(req.NtUser, req.DepartmentCode, vs, UserFunction.Viewer));
                ApplyAccessDiff(store, current, target, changes);
            }
        }
        Persist(changes);
    }

    public void ReplaceAccess(IEnumerable<UserAccess> rows, string changedBy = "SYSTEM")
    {
        var list = rows.Select(r => r with { NtUser = Normalize(r.NtUser), DepartmentCode = r.DepartmentCode?.Trim() ?? "" })
                       .Distinct().ToList();
        var changes = new UserStoreChanges { ChangedBy = Normalize(changedBy) };
        lock (_gate)
        {
            foreach (var store in new[] { _real, _demo })
            {
                var isDemo = store == _demo;
                var target = list.Where(r => IsDemoUser(r.NtUser) == isDemo).ToList();
                ApplyAccessDiff(store, store.Access.ToList(), target, changes);
                foreach (var nt in target.Select(r => r.NtUser).Distinct().Where(nt => store.Users.All(u => u.NtUser != nt)))
                {
                    var first = target.First(r => r.NtUser == nt);
                    var user = new AppUser(nt, nt, first.Function == UserFunction.Owner ? null : first.DepartmentCode,
                        target.Where(r => r.NtUser == nt && r.ValueStreamId > 0).Select(r => r.ValueStreamId).Distinct().OrderBy(v => v).ToList(),
                        DateTime.UtcNow, isDemo);
                    store.Users.Add(user);
                    changes.NewUsers.Add(user);
                }
            }
        }
        Persist(changes);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Rows in <paramref name="current"/> not in <paramref name="target"/> end; new rows are added. A changed function = end + add.</summary>
    private static void ApplyAccessDiff(Store store, List<UserAccess> current, List<UserAccess> target, UserStoreChanges changes)
    {
        foreach (var gone in current.Where(c => !target.Contains(c)).ToList())
        {
            store.Access.Remove(gone);
            changes.EndedAccess.Add(gone);
        }
        foreach (var added in target.Where(t => !current.Contains(t)).ToList())
        {
            store.Access.Add(added);
            changes.AddedAccess.Add(added);
        }
    }

    private static string Clean(string? name, string fallback) => string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();

    /// <summary>Sends the real-user part of the changes to the backend (demo users stay in memory) and notifies.</summary>
    private IReadOnlyDictionary<int, int> Persist(UserStoreChanges changes)
    {
        IReadOnlyDictionary<int, int> ids = new Dictionary<int, int>();
        if (_backend is not null)
        {
            var real = new UserStoreChanges { ChangedBy = changes.ChangedBy };
            real.NewUsers.AddRange(changes.NewUsers.Where(u => !u.IsDemo && !IsDemoUser(u.NtUser)));
            real.UpdatedUsers.AddRange(changes.UpdatedUsers.Where(u => !IsDemoUser(u.NtUser)));
            real.EndedAccess.AddRange(changes.EndedAccess.Where(a => !IsDemoUser(a.NtUser)));
            real.AddedAccess.AddRange(changes.AddedAccess.Where(a => !IsDemoUser(a.NtUser)));
            real.NewRequests.AddRange(changes.NewRequests.Where(r => !IsDemoUser(r.NtUser)));
            real.DecidedRequests.AddRange(changes.DecidedRequests.Where(r => !IsDemoUser(r.NtUser)));
            if (!real.IsEmpty)
            {
                UserStoreSnapshot after;
                lock (_gate) after = new UserStoreSnapshot(_real.Users.ToList(), _real.Access.ToList(), _real.Requests.ToList());
                try { ids = _backend.Apply(real, after); }
                catch
                {
                    Load();                       // the store did not take the change: go back to what it has
                    Changed?.Invoke(this, EventArgs.Empty);
                    throw;
                }
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return ids;
    }

    /// <summary>Fictitious demo users: a Champion for each area in each VS, plus Proxies (some in several VS).</summary>
    private void AddDemoUsers()
    {
        void Add(string name, string dept, UserFunction fn, params int[] vss)
        {
            var nt = DemoNtUser(name);
            var i = _demo.Users.FindIndex(u => u.NtUser == nt);
            if (i < 0) _demo.Users.Add(new AppUser(nt, name, dept, vss.ToList(), DateTime.UtcNow, IsDemo: true));
            else _demo.Users[i] = _demo.Users[i] with { ValueStreams = _demo.Users[i].ValueStreams.Concat(vss).Distinct().OrderBy(v => v).ToList() };
            foreach (var vs in vss) _demo.Access.Add(new UserAccess(nt, dept, vs, fn));
        }
        //            department     VS1                  VS4                  VS5                  VS7                   VS9
        var champions = new (string Dept, string[] Names)[]
        {
            ("QMM1",     new[] { "Andrea Salinas",    "Ivonne Castillo",   "Natalia Ocampo",    "Julián Robles",      "Rodrigo Sandoval" }),
            ("MFQ8",     new[] { "Tomás Arriaga",     "Karla Ontiveros",   "Elena Barajas",     "Marcos Valenzuela",  "Sofía Rentería" }),
            ("GS/OSD43", new[] { "Patricia Lozano",   "Adrián Cisneros",   "Beatriz Galindo",   "Rebeca Treviño",     "Hugo Navarrete" }),
            ("LOP1",     new[] { "Héctor Villalobos", "Mariana Quiroz",    "Rogelio Bermúdez",  "Lorena Ibarra",      "Fabián Zamora" }),
            ("MFO1",     new[] { "Gerardo Peralta",   "Fernanda Ruiz",     "Mauricio Peña",     "Paola Esquer",       "Jorge Montes" }),
            ("LOW",      new[] { "Óscar Medina",      "Alma Rosales",      "Pedro Cázares",     "Verónica Luján",     "Gilberto Saucedo" }),
            ("HSE",      new[] { "Silvia Cárdenas",   "Ignacio Meléndez",  "Claudia Íñiguez",   "Samuel Ochoa",       "Teresa Bustamante" }),
        };
        int[] vsOrder = { 1, 4, 5, 7, 9 };
        foreach (var (dept, names) in champions)
            for (var i = 0; i < vsOrder.Length; i++) Add(names[i], dept, UserFunction.Champion, vsOrder[i]);
        Add("Mónica Trejo", "QMM1", UserFunction.Proxy, 1);
        Add("Raúl Quintana", "QMM1", UserFunction.Proxy, 4);
        Add("César Domínguez", "QMM1", UserFunction.Proxy, 5);
        Add("Andrea Salinas", "QMM1", UserFunction.Proxy, 7);          // Champion in VS1 and Proxy in VS7
        Add("Diana Solís", "QMM1", UserFunction.Proxy, 9);
        Add("Luis Paredes", "MFO1", UserFunction.Proxy, 1);
        Add("Joel Aranda", "MFO1", UserFunction.Proxy, 4);
        Add("Daniel Correa", "MFO1", UserFunction.Proxy, 5);
        Add("Abril Fuentes", "MFO1", UserFunction.Proxy, 9);
        Add("Iván Robledo", "GS/OSD43", UserFunction.Proxy, 1, 4, 5, 7, 9);
        Add("Ernesto Galván", "LOW", UserFunction.Proxy, 1, 4, 5, 7, 9);
        Add("Ramiro Olvera", "HSE", UserFunction.Proxy, 1, 4, 5, 7, 9);
        Add("Consulta Gerencia", "QMM1", UserFunction.Viewer, 1, 4, 5, 7, 9);
    }
}

/// <summary>
/// Users directory in a JSON file (draft / pilot; the path can be a shared folder). Keeps the closed access rows and
/// every request as history, like the Oracle tables.
/// </summary>
public sealed class JsonUserStore : IUserStoreBackend
{
    private sealed class FileModel
    {
        public List<AppUser> Users { get; set; } = new();
        public List<UserAccess> Access { get; set; } = new();
        public List<ProfileRequest> Requests { get; set; } = new();
        public List<ClosedAccess> AccessHistory { get; set; } = new();
    }

    public sealed record ClosedAccess(UserAccess Access, string RemovedBy, DateTime RemovedAtUtc);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _path;

    public JsonUserStore(string path) => _path = path;
    public string Description => $"File {_path}";

    private FileModel Read() =>
        File.Exists(_path) ? JsonSerializer.Deserialize<FileModel>(File.ReadAllText(_path), Json) ?? new FileModel() : new FileModel();

    public UserStoreSnapshot Load()
    {
        var m = Read();
        return new UserStoreSnapshot(m.Users, m.Access, m.Requests);
    }

    public IReadOnlyDictionary<int, int> Apply(UserStoreChanges changes, UserStoreSnapshot after)
    {
        var history = Read().AccessHistory;
        history.AddRange(changes.EndedAccess.Select(a => new ClosedAccess(a, changes.ChangedBy, DateTime.UtcNow)));
        var model = new FileModel
        {
            Users = after.Users.Where(u => !u.IsDemo).ToList(),
            Access = after.Access.ToList(),
            Requests = after.Requests.ToList(),
            AccessHistory = history,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(model, Json));
        File.Move(tmp, _path, overwrite: true);
        return changes.NewRequests.ToDictionary(r => r.Id, r => r.Id);
    }
}
