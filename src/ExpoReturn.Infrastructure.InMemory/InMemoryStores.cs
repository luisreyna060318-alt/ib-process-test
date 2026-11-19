using ExpoReturn.Application;
using ExpoReturn.Domain;

namespace ExpoReturn.Infrastructure.InMemory;

/// <summary>
/// In-memory IB processes (tests). Same rules as Oracle: header and pallets saved together, optimistic concurrency with
/// RowVersion, pallets missing from a saved process are deleted, processes can be deleted.
/// </summary>
public sealed class InMemoryProcessRepository : IProcessRepository
{
    private readonly List<IbProcess> _items = new();
    private readonly object _gate = new();
    private int _nextId = 1, _nextPalletId = 1;
    public event EventHandler? Changed;

    public IReadOnlyList<IbProcess> GetAll() { lock (_gate) return _items.ToList(); }
    public IbProcess? Get(int id) { lock (_gate) return _items.FirstOrDefault(p => p.Id == id); }
    public IReadOnlyList<PalletReturn> AllPallets() { lock (_gate) return _items.SelectMany(p => p.Pallets).ToList(); }
    public PalletReturn? GetPallet(int palletId) { lock (_gate) return _items.SelectMany(p => p.Pallets).FirstOrDefault(p => p.Id == palletId); }

    public void Save(IbProcess process)
    {
        lock (_gate)
        {
            if (process.Id == 0) process.Id = _nextId++;
            var idx = _items.FindIndex(p => p.Id == process.Id);
            if (idx >= 0)
            {
                if (_items[idx].RowVersion != process.RowVersion)
                    throw new ConcurrencyException($"{process.FolioCode} was changed by someone else. Reload it and apply your changes again.");
                process.RowVersion++;
                _items[idx] = process;
            }
            else _items.Add(process);
            foreach (var p in process.Pallets.Where(p => p.Id == 0)) p.Id = _nextPalletId++;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Delete(int processId)
    {
        lock (_gate) _items.RemoveAll(p => p.Id == processId);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public int NextFolio(int valueStreamId)
    {
        // 900000+ is reserved for Excel rows without a usable '#'
        lock (_gate) return _items.Where(p => p.ValueStreamId == valueStreamId && p.FolioNumber < 900000).Select(p => p.FolioNumber).DefaultIfEmpty(0).Max() + 1;
    }

    public void Refresh() => Changed?.Invoke(this, EventArgs.Empty);

    public LegacyAppendResult AppendLegacy(IEnumerable<IbProcess> processes)
    {
        int added = 0, existing = 0;
        lock (_gate)
            foreach (var p in processes)
            {
                if (p.LegacySource is not null && _items.Any(i => i.LegacySource == p.LegacySource)) { existing++; continue; }
                Assign(p);
                _items.Add(p);
                added++;
            }
        Changed?.Invoke(this, EventArgs.Empty);
        return new LegacyAppendResult(added, existing, Array.Empty<string>());
    }

    /// <summary>Tests and samples: replaces everything.</summary>
    public void ReplaceAll(IEnumerable<IbProcess> processes)
    {
        lock (_gate)
        {
            _items.Clear();
            _nextId = 1; _nextPalletId = 1;
            foreach (var p in processes) { Assign(p); _items.Add(p); }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Assign(IbProcess p)
    {
        p.Id = _nextId++;
        foreach (var pallet in p.Pallets) pallet.Id = _nextPalletId++;
    }
}

public sealed class InMemoryAuditTrail : IAuditTrail
{
    private readonly List<AuditEntry> _entries = new();
    private readonly object _gate = new();
    public void Record(IEnumerable<AuditEntry> entries) { lock (_gate) _entries.AddRange(entries); }
    public IReadOnlyList<AuditEntry> For(int processId) { lock (_gate) return _entries.Where(e => e.ProcessId == processId).OrderByDescending(e => e.ChangedAtUtc).ToList(); }
    public IReadOnlyList<AuditEntry> All() { lock (_gate) return _entries.OrderByDescending(e => e.ChangedAtUtc).ToList(); }
    /// <summary>Demo only (memory). The Oracle audit log is append-only.</summary>
    public void Clear() { lock (_gate) _entries.Clear(); }
}

/// <summary>Acknowledged alerts, in memory.</summary>
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

/// <summary>Comments of the IB processes in memory (tests).</summary>
public sealed class InMemoryProcessComments : IProcessComments
{
    private readonly List<ProcessComment> _items = new();
    private readonly object _gate = new();

    public IReadOnlyList<ProcessComment> For(int processId)
    {
        lock (_gate) return _items.Where(c => c.ProcessId == processId).OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.Id).ToList();
    }

    public ProcessComment Add(int processId, string text, string ntUser, string? departmentCode)
    {
        lock (_gate)
        {
            var c = new ProcessComment(_items.Count + 1, processId, text, ntUser, departmentCode, DateTime.UtcNow);
            _items.Add(c);
            return c;
        }
    }
}
