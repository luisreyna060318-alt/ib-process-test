using ExpoReturn.Application;
using ExpoReturn.Domain;

namespace ExpoReturn.Infrastructure.InMemory;

/// <summary>In-memory repository (draft without database). Reset when the application closes.</summary>
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

/// <summary>Acknowledged alerts (UX-B09), in memory.</summary>
public sealed class InMemoryAlertAcknowledgementStore : IAlertAcknowledgementStore
{
    private readonly Dictionary<(int, AlertType), AlertAcknowledgement> _items = new();
    private readonly object _gate = new();
    public void Acknowledge(AlertAcknowledgement ack) { lock (_gate) _items[(ack.PalletId, ack.Type)] = ack; }
    public AlertAcknowledgement? Find(int palletId, AlertType type) { lock (_gate) return _items.GetValueOrDefault((palletId, type)); }
    public void Clear() { lock (_gate) _items.Clear(); }
}
