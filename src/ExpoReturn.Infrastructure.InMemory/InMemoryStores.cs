using ExpoReturn.Application;
using ExpoReturn.Domain;

namespace ExpoReturn.Infrastructure.InMemory;

/// <summary>
/// In-memory repository (draft without database). Reset when the application closes. Same rules as Oracle: no delete,
/// optimistic concurrency with RowVersion.
/// </summary>
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
            if (idx >= 0)
            {
                if (_items[idx].RowVersion != pallet.RowVersion)
                    throw new ConcurrencyException($"{pallet.FolioCode} was changed by someone else. Reload it and apply your changes again.");
                pallet.RowVersion++;
                _items[idx] = pallet;
            }
            else _items.Add(pallet);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Refresh() => Changed?.Invoke(this, EventArgs.Empty);

    public LegacyAppendResult AppendLegacy(IEnumerable<PalletReturn> pallets)
    {
        int added = 0, existing = 0;
        lock (_gate)
            foreach (var p in pallets)
            {
                if (p.LegacySource is not null && _items.Any(i => i.LegacySource == p.LegacySource)) { existing++; continue; }
                p.Id = _nextId++;
                _items.Add(p);
                added++;
            }
        Changed?.Invoke(this, EventArgs.Empty);
        return new LegacyAppendResult(added, existing, Array.Empty<string>());
    }

    public int NextFolio(int valueStreamId)
    {
        // 900000+ is reserved for Excel rows without a usable '#'
        lock (_gate) return _items.Where(p => p.ValueStreamId == valueStreamId && p.FolioNumber < 900000).Select(p => p.FolioNumber).DefaultIfEmpty(0).Max() + 1;
    }

    /// <summary>Demo only (synthetic data / in-memory Excel view). Not available with the database.</summary>
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
    /// <summary>Demo only (memory). The Oracle audit log is append-only.</summary>
    public void Clear() { lock (_gate) _entries.Clear(); }
}

/// <summary>Acknowledged alerts (UX-B09), in memory.</summary>
public sealed class InMemoryAlertAcknowledgementStore : IAlertAcknowledgementStore
{
    private readonly Dictionary<(int, AlertType), AlertAcknowledgement> _items = new();
    private readonly object _gate = new();
    public void Acknowledge(AlertAcknowledgement ack)
    {
        if (string.IsNullOrWhiteSpace(ack.Comment)) throw new DataRuleException("The comment cannot be empty.");
        lock (_gate) _items[(ack.PalletId, ack.Type)] = ack;
    }
    public AlertAcknowledgement? Find(int palletId, AlertType type) { lock (_gate) return _items.GetValueOrDefault((palletId, type)); }
    public void Clear() { lock (_gate) _items.Clear(); }
}
