using ExpoReturn.Application;
using ExpoReturn.Domain;

namespace ExpoReturn.Infrastructure.InMemory;

/// <summary>Repositorio en memoria (borrador sin base de datos). Se reinicia al cerrar la aplicación.</summary>
public sealed class InMemoryPalletRepository : IPalletReturnRepository
{
    private readonly List<PalletReturn> _items = new();
    private readonly object _gate = new();
    private int _nextId = 1;
    public event EventHandler? Changed;

    public IReadOnlyList<PalletReturn> GetAll() { lock (_gate) return _items.ToList(); }
    public PalletReturn? Get(int id) { lock (_gate) return _items.FirstOrDefault(p => p.Id == id); }

    public void Save(PalletReturn pallet)
    {
        lock (_gate)
        {
            if (pallet.Id == 0) pallet.Id = _nextId++;
            var idx = _items.FindIndex(p => p.Id == pallet.Id);
            if (idx >= 0) _items[idx] = pallet; else _items.Add(pallet);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public int NextFolio(int valueStreamId)
    {
        lock (_gate) return _items.Where(p => p.ValueStreamId == valueStreamId).Select(p => p.FolioNumber).DefaultIfEmpty(0).Max() + 1;
    }

    public void ReplaceAll(IEnumerable<PalletReturn> pallets)
    {
        lock (_gate)
        {
            _items.Clear();
            _nextId = 1;
            foreach (var p in pallets) { p.Id = _nextId++; _items.Add(p); }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class InMemoryAuditTrail : IAuditTrail
{
    private readonly List<AuditEntry> _entries = new();
    private readonly object _gate = new();
    public void Record(IEnumerable<AuditEntry> entries) { lock (_gate) _entries.AddRange(entries); }
    public IReadOnlyList<AuditEntry> For(int palletId) { lock (_gate) return _entries.Where(e => e.PalletId == palletId).OrderByDescending(e => e.ChangedAtUtc).ToList(); }
    public IReadOnlyList<AuditEntry> All() { lock (_gate) return _entries.OrderByDescending(e => e.ChangedAtUtc).ToList(); }
    public void Clear() { lock (_gate) _entries.Clear(); }
}

/// <summary>
/// Directorio de Champions/Proxies. Por defecto con nombres FICTICIOS; al importar el Excel real
/// se reemplaza con la hoja "Champions VS" (sólo en memoria, no se guarda en el repositorio).
/// </summary>
public sealed class ChampionsDirectory : IChampionsDirectory
{
    private static readonly RoleCode[] RoleOrder =
        { RoleCode.Qmm, RoleCode.Requestor, RoleCode.Gs, RoleCode.Lop, RoleCode.Mfo, RoleCode.Low, RoleCode.Hse };

    private List<Person> _people = DefaultPeople();

    /// <summary>Se dispara al reemplazar la matriz (guardar, restaurar o importar): la app recalcula usuarios y permisos.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<Person> All() => _people;
    public IReadOnlyList<string> QmmFor(int vs) =>
        _people.Where(p => p.Role == RoleCode.Qmm && p.ValueStreamId == vs && !p.IsPlaceholder)
               .OrderBy(p => p.Assignment == "Champion" ? 0 : 1).Select(p => p.Name).Distinct().ToList();

    public void Replace(IEnumerable<Person> people)
    {
        _people = people.ToList();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Usuarios simulados = cada persona de la matriz con su área; sus VS son TODOS los VS donde está asignada
    /// (Champion o Proxy). Se agregan Admin y Consulta, que ven todos los VS. En producción el usuario sale de
    /// Active Directory y sus VS de sec.Champions_Matrix con la misma regla.
    /// </summary>
    public IReadOnlyList<UserContext> DemoUsers()
    {
        var users = new List<UserContext> { new("DEMO\\admin.pe", "Admin Process Excellence", RoleCode.AdminQmmPe, Array.Empty<int>()) };
        users.AddRange(_people.Where(p => !p.IsPlaceholder)
            .GroupBy(p => (Name: p.Name.Trim(), p.Role))
            .OrderBy(g => Array.IndexOf(RoleOrder, g.Key.Role)).ThenBy(g => g.Count() > 1 ? 1 : 0)
            .ThenBy(g => g.Min(p => p.ValueStreamId)).ThenBy(g => g.Any(p => p.Assignment == "Champion") ? 0 : 1)
            .ThenBy(g => g.Key.Name, StringComparer.CurrentCulture)
            .Select(g => new UserContext(DemoNtUser(g.Key.Name), g.Key.Name, g.Key.Role,
                                         g.Select(p => p.ValueStreamId).Distinct().OrderBy(v => v).ToList())));
        users.Add(new("DEMO\\consulta", "Consulta Gerencia", RoleCode.Viewer, Array.Empty<int>()));
        return users;
    }

    /// <summary>Usuario de red simulado: "Andrea Salinas" → DEMO\andrea.salinas.</summary>
    public static string DemoNtUser(string name)
    {
        var plain = new string(name.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
        var parts = plain.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => new string(p.Where(char.IsLetterOrDigit).ToArray())).Where(p => p.Length > 0);
        return "DEMO\\" + string.Join(".", parts);
    }

    /// <summary>
    /// Matriz de demostración con nombres FICTICIOS: al menos un Champion por área y por VS (dedicado a ese VS),
    /// más Proxies; algunos cubren varios VS para mostrar que una persona puede estar asignada a más de uno.
    /// </summary>
    private static List<Person> DefaultPeople()
    {
        var list = new List<Person>();
        void Add(string name, RoleCode role, string assignment, params int[] vss)
        {
            foreach (var vs in vss) list.Add(new(name, role, vs, assignment));
        }
        //            Área                 VS1                  VS4                  VS5                  VS7                   VS9
        var champions = new (RoleCode Role, string[] Names)[]
        {
            (RoleCode.Qmm,       new[] { "Andrea Salinas",    "Ivonne Castillo",   "Natalia Ocampo",    "Julián Robles",      "Rodrigo Sandoval" }),
            (RoleCode.Requestor, new[] { "Tomás Arriaga",     "Karla Ontiveros",   "Elena Barajas",     "Marcos Valenzuela",  "Sofía Rentería" }),
            (RoleCode.Gs,        new[] { "Patricia Lozano",   "Adrián Cisneros",   "Beatriz Galindo",   "Rebeca Treviño",     "Hugo Navarrete" }),
            (RoleCode.Lop,       new[] { "Héctor Villalobos", "Mariana Quiroz",    "Rogelio Bermúdez",  "Lorena Ibarra",      "Fabián Zamora" }),
            (RoleCode.Mfo,       new[] { "Gerardo Peralta",   "Fernanda Ruiz",     "Mauricio Peña",     "Paola Esquer",       "Jorge Montes" }),
            (RoleCode.Low,       new[] { "Óscar Medina",      "Alma Rosales",      "Pedro Cázares",     "Verónica Luján",     "Gilberto Saucedo" }),
            (RoleCode.Hse,       new[] { "Silvia Cárdenas",   "Ignacio Meléndez",  "Claudia Íñiguez",   "Samuel Ochoa",       "Teresa Bustamante" }),
        };
        int[] vsOrder = { 1, 4, 5, 7, 9 };
        foreach (var (role, names) in champions)
            for (var i = 0; i < vsOrder.Length; i++) Add(names[i], role, "Champion", vsOrder[i]);

        // Proxies
        Add("Mónica Trejo", RoleCode.Qmm, "Proxy", 1);
        Add("Raúl Quintana", RoleCode.Qmm, "Proxy", 4);
        Add("César Domínguez", RoleCode.Qmm, "Proxy", 5);
        Add("Andrea Salinas", RoleCode.Qmm, "Proxy", 7);          // Champion en VS1 y Proxy en VS7
        Add("Diana Solís", RoleCode.Qmm, "Proxy", 9);
        Add("Luis Paredes", RoleCode.Mfo, "Proxy", 1);
        Add("Joel Aranda", RoleCode.Mfo, "Proxy", 4);
        Add("Daniel Correa", RoleCode.Mfo, "Proxy", 5);
        Add("TBD", RoleCode.Mfo, "Proxy", 7);                      // como en el Excel: vacante, no genera usuario
        Add("Abril Fuentes", RoleCode.Mfo, "Proxy", 9);
        Add("Iván Robledo", RoleCode.Gs, "Proxy", 1, 4, 5, 7, 9);
        Add("Ernesto Galván", RoleCode.Low, "Proxy", 1, 4, 5, 7, 9);
        Add("Ramiro Olvera", RoleCode.Hse, "Proxy", 1, 4, 5, 7, 9);
        return list;
    }
}

/// <summary>Alertas atendidas (UX-B09), en memoria.</summary>
public sealed class InMemoryAlertAcknowledgementStore : IAlertAcknowledgementStore
{
    private readonly Dictionary<(int, AlertType), AlertAcknowledgement> _items = new();
    private readonly object _gate = new();
    public void Acknowledge(AlertAcknowledgement ack) { lock (_gate) _items[(ack.PalletId, ack.Type)] = ack; }
    public AlertAcknowledgement? Find(int palletId, AlertType type) { lock (_gate) return _items.GetValueOrDefault((palletId, type)); }
    public void Clear() { lock (_gate) _items.Clear(); }
}
