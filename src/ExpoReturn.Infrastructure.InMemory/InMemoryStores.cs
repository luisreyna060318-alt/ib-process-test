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
            .OrderBy(g => Array.IndexOf(RoleOrder, g.Key.Role)).ThenBy(g => g.Key.Name, StringComparer.CurrentCulture)
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

    /// <summary>Matriz de demostración con nombres FICTICIOS. Varias personas cubren más de un VS.</summary>
    private static List<Person> DefaultPeople()
    {
        var list = new List<Person>();
        void Add(string name, RoleCode role, string assignment, params int[] vss)
        {
            foreach (var vs in vss) list.Add(new(name, role, vs, assignment));
        }
        // QMM
        Add("Andrea Salinas", RoleCode.Qmm, "Champion", 1, 7);
        Add("Mónica Trejo", RoleCode.Qmm, "Proxy", 1);
        Add("Ivonne Castillo", RoleCode.Qmm, "Champion", 4);
        Add("Raúl Quintana", RoleCode.Qmm, "Proxy", 4);
        Add("Natalia Ocampo", RoleCode.Qmm, "Champion", 5);
        Add("César Domínguez", RoleCode.Qmm, "Proxy", 5);
        Add("Julián Robles", RoleCode.Qmm, "Proxy", 7);
        Add("Rodrigo Sandoval", RoleCode.Qmm, "Champion", 9);
        Add("Diana Solís", RoleCode.Qmm, "Proxy", 9);
        // MFQ (solicitantes)
        Add("Karla Ontiveros", RoleCode.Requestor, "Champion", 4, 5);
        Add("Tomás Arriaga", RoleCode.Requestor, "Champion", 1, 7, 9);
        // Comercio Exterior GS/OSD43
        Add("Patricia Lozano", RoleCode.Gs, "Champion", 1, 4, 5);
        Add("Rebeca Treviño", RoleCode.Gs, "Champion", 7, 9);
        Add("Iván Robledo", RoleCode.Gs, "Proxy", 1, 4, 5, 7, 9);
        // LOP
        Add("Héctor Villalobos", RoleCode.Lop, "Champion", 1, 4, 5);
        Add("Lorena Ibarra", RoleCode.Lop, "Champion", 7, 9);
        // MFO
        Add("Gerardo Peralta", RoleCode.Mfo, "Champion", 1);
        Add("Luis Paredes", RoleCode.Mfo, "Proxy", 1);
        Add("Fernanda Ruiz", RoleCode.Mfo, "Champion", 4);
        Add("Joel Aranda", RoleCode.Mfo, "Proxy", 4);
        Add("Mauricio Peña", RoleCode.Mfo, "Champion", 5);
        Add("Daniel Correa", RoleCode.Mfo, "Proxy", 5);
        Add("Paola Esquer", RoleCode.Mfo, "Champion", 7);
        Add("TBD", RoleCode.Mfo, "Proxy", 7);
        Add("Jorge Montes", RoleCode.Mfo, "Champion", 9);
        Add("Abril Fuentes", RoleCode.Mfo, "Proxy", 9);
        // LOW
        Add("Óscar Medina", RoleCode.Low, "Champion", 1, 4, 5, 7);
        Add("Gilberto Saucedo", RoleCode.Low, "Champion", 9);
        Add("Ernesto Galván", RoleCode.Low, "Proxy", 1, 4, 5, 7, 9);
        // HSE
        Add("Silvia Cárdenas", RoleCode.Hse, "Champion", 1, 4, 5, 7, 9);
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
