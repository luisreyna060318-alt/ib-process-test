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
    private List<Person> _people = DefaultPeople();

    public IReadOnlyList<Person> All() => _people;
    public IReadOnlyList<string> QmmFor(int vs) =>
        _people.Where(p => p.Role == RoleCode.Qmm && p.ValueStreamId == vs && p.Name != "TBD").Select(p => p.Name).Distinct().ToList();

    public void Replace(IEnumerable<Person> people)
    {
        var list = people.ToList();
        if (list.Count > 0) _people = list;
    }

    public IReadOnlyList<UserContext> DemoUsers()
    {
        string Pick(RoleCode r, int vs, string fallback) =>
            _people.FirstOrDefault(p => p.Role == r && p.ValueStreamId == vs && p.Assignment == "Champion" && p.Name != "TBD")?.Name ?? fallback;
        return new List<UserContext>
        {
            new("DEMO\\ADMIN", "Admin Process Excellence", RoleCode.AdminQmmPe, Array.Empty<int>()),
            new("DEMO\\QMM7", Pick(RoleCode.Qmm, 7, "Andrea Salinas"), RoleCode.Qmm, new[] { 7 }),
            new("DEMO\\QMM9", Pick(RoleCode.Qmm, 9, "Rodrigo Sandoval"), RoleCode.Qmm, new[] { 9 }),
            new("DEMO\\MFQ4", "Karla Ontiveros (MFQ4)", RoleCode.Requestor, new[] { 4 }),
            new("DEMO\\GS", Pick(RoleCode.Gs, 1, "Patricia Lozano"), RoleCode.Gs, Array.Empty<int>()),
            new("DEMO\\LOP", "Héctor Villalobos (LOP2)", RoleCode.Lop, Array.Empty<int>()),
            new("DEMO\\MFO7", Pick(RoleCode.Mfo, 7, "Gerardo Peralta"), RoleCode.Mfo, new[] { 7 }),
            new("DEMO\\LOW", Pick(RoleCode.Low, 1, "Óscar Medina"), RoleCode.Low, Array.Empty<int>()),
            new("DEMO\\HSE", "Silvia Cárdenas (HSE)", RoleCode.Hse, Array.Empty<int>()),
            new("DEMO\\VIEW", "Consulta Gerencia", RoleCode.Viewer, Array.Empty<int>()),
        };
    }

    private static List<Person> DefaultPeople()
    {
        var data = new (int Vs, string MfoC, string MfoP, string QmmC, string QmmP)[]
        {
            (1, "Gerardo Peralta", "Luis Paredes", "Andrea Salinas", "Mónica Trejo"),
            (4, "Fernanda Ruiz", "Joel Aranda", "Ivonne Castillo", "Raúl Quintana"),
            (5, "Mauricio Peña", "Daniel Correa", "Natalia Ocampo", "César Domínguez"),
            (7, "Paola Esquer", "TBD", "Andrea Salinas", "Julián Robles"),
            (9, "Jorge Montes", "Abril Fuentes", "Rodrigo Sandoval", "Diana Solís"),
        };
        var list = new List<Person>();
        foreach (var d in data)
        {
            list.Add(new(d.MfoC, RoleCode.Mfo, d.Vs, "Champion"));
            list.Add(new(d.MfoP, RoleCode.Mfo, d.Vs, "Proxy"));
            list.Add(new(d.QmmC, RoleCode.Qmm, d.Vs, "Champion"));
            list.Add(new(d.QmmP, RoleCode.Qmm, d.Vs, "Proxy"));
        }
        list.Add(new("Óscar Medina", RoleCode.Low, 1, "Champion"));
        list.Add(new("Ernesto Galván", RoleCode.Low, 1, "Proxy"));
        list.Add(new("Patricia Lozano", RoleCode.Gs, 1, "Champion"));
        list.Add(new("Iván Robledo", RoleCode.Gs, 1, "Proxy"));
        return list;
    }
}
