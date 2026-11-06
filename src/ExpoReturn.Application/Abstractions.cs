using ExpoReturn.Domain;

namespace ExpoReturn.Application;

public interface IClock { DateTime Today { get; } DateTime UtcNow { get; } }

public sealed class SystemClock : IClock
{
    public DateTime Today => DateTime.Today;
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>Persistencia de pallets. Borrador: en memoria. Producción: EF Core + SQL Server.</summary>
public interface IPalletReturnRepository
{
    IReadOnlyList<PalletReturn> GetAll();
    PalletReturn? Get(int id);
    void Save(PalletReturn pallet);
    int NextFolio(int valueStreamId);
    void ReplaceAll(IEnumerable<PalletReturn> pallets);
    event EventHandler? Changed;
}

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

/// <summary>
/// Una asignación de la matriz Champions VS: persona × área × VS × Champion/Proxy. Una persona puede tener
/// varias (una por VS). La matriz delimita en qué VS edita cada usuario, en todas las áreas.
/// </summary>
public sealed record Person(string Name, RoleCode Role, int ValueStreamId, string Assignment)
{
    public string Describe => $"{Name} · {Role.ShortName()} · VS{ValueStreamId} · {Assignment}";
    /// <summary>Celdas sin persona asignada en el Excel ("TBD", "N/A"…): no generan usuario.</summary>
    public bool IsPlaceholder => Name.Trim().ToUpperInvariant() is "" or "TBD" or "N/A" or "NA" or "-" or "POR DEFINIR";
}

/// <summary>Atención de una alerta (UX-B09): quién la atendió, cuándo y con qué comentario.</summary>
public sealed record AlertAcknowledgement(int PalletId, AlertType Type, string NtUser, string Comment, DateTime AtUtc);

public interface IAlertAcknowledgementStore
{
    void Acknowledge(AlertAcknowledgement ack);
    AlertAcknowledgement? Find(int palletId, AlertType type);
    void Clear();
}

/// <summary>Matriz Champions/Proxies por VS (hoja "Champions VS" del Excel real).</summary>
public interface IChampionsDirectory
{
    IReadOnlyList<Person> All();
    IReadOnlyList<string> QmmFor(int valueStreamId);
    IReadOnlyList<UserContext> DemoUsers();
}
