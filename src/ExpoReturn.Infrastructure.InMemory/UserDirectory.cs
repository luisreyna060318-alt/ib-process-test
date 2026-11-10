using System.Globalization;
using System.Text;
using System.Text.Json;
using ExpoReturn.Application;

namespace ExpoReturn.Infrastructure.InMemory;

/// <summary>
/// Users, access rows and profile requests. Draft persistence: one JSON file (configurable path, so it can sit in a
/// shared network folder for a pilot). Production: the Oracle tables IB_PROCESS_USERS, IB_PROCESS_USER_ACCESS and
/// IB_PROCESS_PROFILE_REQUESTS behind this same interface.
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

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string? _path;
    private readonly HashSet<string> _configuredOwners;
    private readonly object _gate = new();
    private Store _store = new();

    public event EventHandler? Changed;

    /// <param name="path">JSON file; null = memory only (tests).</param>
    /// <param name="owners">Windows users configured as Owner (DOMAIN\user or user).</param>
    /// <param name="includeDemoUsers">Adds the fictitious demo users (one per area and VS).</param>
    public UserDirectory(string? path, IEnumerable<string> owners, bool includeDemoUsers)
    {
        _path = path;
        _configuredOwners = owners.Where(o => !string.IsNullOrWhiteSpace(o)).Select(Normalize).ToHashSet();
        if (_path is not null && File.Exists(_path))
            _store = JsonSerializer.Deserialize<Store>(File.ReadAllText(_path), Json) ?? new Store();
        if (includeDemoUsers) AddDemoUsers();
    }

    public static string Normalize(string ntUser) => ntUser.Trim().ToUpperInvariant();

    /// <summary>Fictitious network user: "Andrea Salinas" → DEMO\ANDREA.SALINAS.</summary>
    public static string DemoNtUser(string name)
    {
        var plain = new string(name.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        var parts = plain.ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => new string(p.Where(char.IsLetterOrDigit).ToArray())).Where(p => p.Length > 0);
        return "DEMO\\" + string.Join(".", parts);
    }

    // ------------------------------------------------------------------ queries

    public AppUser? Find(string ntUser)
    {
        var key = Normalize(ntUser);
        lock (_gate) return _store.Users.FirstOrDefault(u => u.NtUser == key);
    }

    public IReadOnlyList<AppUser> Users() { lock (_gate) return _store.Users.OrderBy(u => u.IsDemo).ThenBy(u => u.DisplayName).ToList(); }
    public IReadOnlyList<UserAccess> Access() { lock (_gate) return _store.Access.ToList(); }

    public bool IsOwner(string ntUser)
    {
        var key = Normalize(ntUser);
        var shortName = key.Contains('\\') ? key[(key.LastIndexOf('\\') + 1)..] : key;
        if (_configuredOwners.Contains(key) || _configuredOwners.Contains(shortName)) return true;
        lock (_gate) return _store.Access.Any(a => a.NtUser == key && a.Function == UserFunction.Owner);
    }

    public UserContext ContextFor(string ntUser)
    {
        var key = Normalize(ntUser);
        var user = Find(key);
        List<AccessGrant> grants;
        lock (_gate)
            grants = _store.Access.Where(a => a.NtUser == key && a.Function != UserFunction.Owner)
                .Select(a => new AccessGrant(a.DepartmentCode, a.ValueStreamId, a.Function)).ToList();
        return new UserContext(key, user?.DisplayName ?? key, user?.DepartmentCode, IsOwner(key), grants);
    }

    public IReadOnlyList<string> QmmFor(int valueStreamId)
    {
        lock (_gate)
            return _store.Access
                .Where(a => a.ValueStreamId == valueStreamId && a.Function is UserFunction.Champion or UserFunction.Proxy
                            && RoleInfo.AreaOfDepartment(a.DepartmentCode) == RoleCode.Qmm)
                .OrderBy(a => a.Function == UserFunction.Champion ? 0 : 1)
                .Select(a => _store.Users.FirstOrDefault(u => u.NtUser == a.NtUser)?.DisplayName ?? a.NtUser)
                .Distinct().ToList();
    }

    public IReadOnlyList<ProfileRequest> Requests(bool pendingOnly = true)
    {
        lock (_gate)
            return _store.Requests.Where(r => !pendingOnly || r.Status == RequestStatus.Pending).OrderByDescending(r => r.RequestedAtUtc).ToList();
    }

    public ProfileRequest? PendingRequestFor(string ntUser)
    {
        var key = Normalize(ntUser);
        lock (_gate) return _store.Requests.LastOrDefault(r => r.NtUser == key && r.Status == RequestStatus.Pending);
    }

    // ------------------------------------------------------------------ commands

    public void Register(string ntUser, string displayName, string departmentCode, IReadOnlyList<int> valueStreams)
    {
        var key = Normalize(ntUser);
        lock (_gate)
        {
            if (_store.Users.Any(u => u.NtUser == key)) throw new InvalidOperationException($"{key} is already registered.");
            _store.Users.Add(new AppUser(key, Clean(displayName, key), departmentCode, valueStreams.Distinct().OrderBy(v => v).ToList(), DateTime.UtcNow));
            foreach (var vs in valueStreams.Distinct())
                _store.Access.Add(new UserAccess(key, departmentCode, vs, UserFunction.Viewer));
        }
        SaveAndNotify();
    }

    public void UpdateDisplayName(string ntUser, string displayName)
    {
        var key = Normalize(ntUser);
        lock (_gate)
        {
            var i = _store.Users.FindIndex(u => u.NtUser == key);
            if (i < 0) return;
            _store.Users[i] = _store.Users[i] with { DisplayName = Clean(displayName, key) };
        }
        SaveAndNotify();
    }

    public ProfileRequest RequestChange(string ntUser, string departmentCode, IReadOnlyList<int> valueStreams)
    {
        var key = Normalize(ntUser);
        ProfileRequest request;
        lock (_gate)
        {
            // A new request replaces the user's previous pending one.
            for (var i = 0; i < _store.Requests.Count; i++)
                if (_store.Requests[i].NtUser == key && _store.Requests[i].Status == RequestStatus.Pending)
                    _store.Requests[i] = _store.Requests[i] with { Status = RequestStatus.Rejected, Comment = "Replaced by a newer request", DecidedAtUtc = DateTime.UtcNow };
            var id = _store.Requests.Count == 0 ? 1 : _store.Requests.Max(r => r.Id) + 1;
            request = new ProfileRequest(id, key, departmentCode, valueStreams.Distinct().OrderBy(v => v).ToList(), DateTime.UtcNow);
            _store.Requests.Add(request);
        }
        SaveAndNotify();
        return request;
    }

    /// <summary>
    /// Approve: the profile takes the new department / VS. VS removed lose their access rows; new VS get Viewer rows.
    /// If the department changes, Champion/Proxy functions go back to Viewer (they belonged to the old department).
    /// </summary>
    public void Decide(int requestId, bool approve, string ownerNtUser, string? comment)
    {
        lock (_gate)
        {
            var i = _store.Requests.FindIndex(r => r.Id == requestId && r.Status == RequestStatus.Pending);
            if (i < 0) return;
            var req = _store.Requests[i];
            _store.Requests[i] = req with
            {
                Status = approve ? RequestStatus.Approved : RequestStatus.Rejected,
                DecidedBy = Normalize(ownerNtUser), DecidedAtUtc = DateTime.UtcNow, Comment = comment,
            };
            if (!approve) { }
            else
            {
                var u = _store.Users.FindIndex(x => x.NtUser == req.NtUser);
                var oldDept = u >= 0 ? _store.Users[u].DepartmentCode : null;
                if (u >= 0) _store.Users[u] = _store.Users[u] with { DepartmentCode = req.DepartmentCode, ValueStreams = req.ValueStreams };
                var deptChanged = !string.Equals(oldDept, req.DepartmentCode, StringComparison.OrdinalIgnoreCase);
                var keep = _store.Access.Where(a => a.NtUser == req.NtUser
                                                    && (a.Function == UserFunction.Owner || (!deptChanged && req.ValueStreams.Contains(a.ValueStreamId))))
                                        .ToList();
                _store.Access.RemoveAll(a => a.NtUser == req.NtUser);
                _store.Access.AddRange(keep);
                foreach (var vs in req.ValueStreams.Where(vs => !keep.Any(k => k.ValueStreamId == vs && k.Function != UserFunction.Owner)))
                    _store.Access.Add(new UserAccess(req.NtUser, req.DepartmentCode, vs, UserFunction.Viewer));
            }
        }
        SaveAndNotify();
    }

    public void ReplaceAccess(IEnumerable<UserAccess> rows)
    {
        var list = rows.Select(r => r with { NtUser = Normalize(r.NtUser) }).ToList();
        lock (_gate)
        {
            var users = list.Select(r => r.NtUser).ToHashSet();
            // Users edited in the grid: their old rows are replaced. Users with no rows left keep their profile (Viewer).
            foreach (var existing in _store.Users.Select(u => u.NtUser).ToList())
                if (!list.Any(r => r.NtUser == existing) && _store.Access.Any(a => a.NtUser == existing)) users.Add(existing);
            _store.Access.RemoveAll(a => users.Contains(a.NtUser));
            _store.Access.AddRange(list);
            foreach (var nt in list.Select(r => r.NtUser).Distinct().Where(nt => _store.Users.All(u => u.NtUser != nt)))
            {
                var first = list.First(r => r.NtUser == nt);
                _store.Users.Add(new AppUser(nt, nt, first.Function == UserFunction.Owner ? null : first.DepartmentCode,
                    list.Where(r => r.NtUser == nt && r.ValueStreamId > 0).Select(r => r.ValueStreamId).Distinct().OrderBy(v => v).ToList(),
                    DateTime.UtcNow, nt.StartsWith("DEMO\\")));
            }
        }
        SaveAndNotify();
    }

    // ------------------------------------------------------------------ helpers

    private static string Clean(string? name, string fallback) => string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();

    private void SaveAndNotify()
    {
        if (_path is not null)
        {
            Store copy;
            lock (_gate)
            {
                var real = _store.Users.Where(u => !u.IsDemo).Select(u => u.NtUser).ToHashSet();
                copy = new Store
                {
                    Users = _store.Users.Where(u => !u.IsDemo).ToList(),
                    Access = _store.Access.Where(a => real.Contains(a.NtUser) || (!a.NtUser.StartsWith("DEMO\\") && a.Function == UserFunction.Owner)).ToList(),
                    Requests = _store.Requests.Where(r => real.Contains(r.NtUser)).ToList(),
                };
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(copy, Json));
            File.Move(tmp, _path, overwrite: true);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Fictitious demo users: a Champion for each area in each VS, plus Proxies (some in several VS).</summary>
    private void AddDemoUsers()
    {
        void Add(string name, string dept, UserFunction fn, params int[] vss)
        {
            var nt = DemoNtUser(name);
            var i = _store.Users.FindIndex(u => u.NtUser == nt);
            if (i < 0) _store.Users.Add(new AppUser(nt, name, dept, vss.ToList(), DateTime.UtcNow, IsDemo: true));
            else _store.Users[i] = _store.Users[i] with { ValueStreams = _store.Users[i].ValueStreams.Concat(vss).Distinct().OrderBy(v => v).ToList() };
            foreach (var vs in vss) _store.Access.Add(new UserAccess(nt, dept, vs, fn));
        }
        //            department     VS1                  VS4                  VS5                  VS7                   VS9
        var champions = new (string Dept, string[] Names)[]
        {
            ("QMM",      new[] { "Andrea Salinas",    "Ivonne Castillo",   "Natalia Ocampo",    "Julián Robles",      "Rodrigo Sandoval" }),
            ("MFQ",      new[] { "Tomás Arriaga",     "Karla Ontiveros",   "Elena Barajas",     "Marcos Valenzuela",  "Sofía Rentería" }),
            ("GS/OSD43", new[] { "Patricia Lozano",   "Adrián Cisneros",   "Beatriz Galindo",   "Rebeca Treviño",     "Hugo Navarrete" }),
            ("LOP",      new[] { "Héctor Villalobos", "Mariana Quiroz",    "Rogelio Bermúdez",  "Lorena Ibarra",      "Fabián Zamora" }),
            ("MFO",      new[] { "Gerardo Peralta",   "Fernanda Ruiz",     "Mauricio Peña",     "Paola Esquer",       "Jorge Montes" }),
            ("LOW",      new[] { "Óscar Medina",      "Alma Rosales",      "Pedro Cázares",     "Verónica Luján",     "Gilberto Saucedo" }),
            ("HSE",      new[] { "Silvia Cárdenas",   "Ignacio Meléndez",  "Claudia Íñiguez",   "Samuel Ochoa",       "Teresa Bustamante" }),
        };
        int[] vsOrder = { 1, 4, 5, 7, 9 };
        foreach (var (dept, names) in champions)
            for (var i = 0; i < vsOrder.Length; i++) Add(names[i], dept, UserFunction.Champion, vsOrder[i]);
        Add("Mónica Trejo", "QMM", UserFunction.Proxy, 1);
        Add("Raúl Quintana", "QMM", UserFunction.Proxy, 4);
        Add("César Domínguez", "QMM", UserFunction.Proxy, 5);
        Add("Andrea Salinas", "QMM", UserFunction.Proxy, 7);          // Champion in VS1 and Proxy in VS7
        Add("Diana Solís", "QMM", UserFunction.Proxy, 9);
        Add("Luis Paredes", "MFO", UserFunction.Proxy, 1);
        Add("Joel Aranda", "MFO", UserFunction.Proxy, 4);
        Add("Daniel Correa", "MFO", UserFunction.Proxy, 5);
        Add("Abril Fuentes", "MFO", UserFunction.Proxy, 9);
        Add("Iván Robledo", "GS/OSD43", UserFunction.Proxy, 1, 4, 5, 7, 9);
        Add("Ernesto Galván", "LOW", UserFunction.Proxy, 1, 4, 5, 7, 9);
        Add("Ramiro Olvera", "HSE", UserFunction.Proxy, 1, 4, 5, 7, 9);
        Add("Consulta Gerencia", "QMM", UserFunction.Viewer, 1, 4, 5, 7, 9);
    }
}
