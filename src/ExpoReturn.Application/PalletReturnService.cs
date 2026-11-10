using System.Globalization;
using System.Reflection;
using ExpoReturn.Domain;

namespace ExpoReturn.Application;

public sealed record SaveResult(bool Success, IReadOnlyList<ValidationIssue> Issues, IReadOnlyList<string> DeniedFields, PalletReturn? Saved,
                                IReadOnlyList<string>? ClearedFields = null, string? StoreError = null)
{
    public IReadOnlyList<string> Cleared => ClearedFields ?? Array.Empty<string>();
    public string Summary => Success
        ? "Saved."
        : StoreError is not null
            ? StoreError
            : Cleared.Count > 0
                ? $"A field that already has a value cannot be left empty: {string.Join(", ", Cleared)}"
                : DeniedFields.Count > 0
                    ? $"No permission to change: {string.Join(", ", DeniedFields)}"
                    : $"Not saved: {Issues.Count(i => i.Severity == Severity.Error)} validation error(s).";
}

/// <summary>
/// Record use cases: create and save (permissions + validation + recalculation + audit).
/// Data rules for everyone, the Owner included: a record is never deleted, and a field that already has a value can be
/// changed but never left empty.
/// </summary>
public sealed class PalletReturnService
{
    private readonly IPalletReturnRepository _repo;
    private readonly IAuditTrail _audit;
    private readonly IClock _clock;
    public CalcParameters Parameters { get; }

    public PalletReturnService(IPalletReturnRepository repo, IAuditTrail audit, IClock clock, CalcParameters? parameters = null)
    {
        _repo = repo; _audit = audit; _clock = clock;
        Parameters = parameters ?? CalcParameters.Default;
    }

    public PalletEvaluation Evaluate(PalletReturn p) => PalletEvaluator.Evaluate(p, Parameters, _clock.Today);

    public PalletReturn NewDraft(int valueStreamId, UserContext user) => new()
    {
        ValueStreamId = valueStreamId,
        FolioNumber = 0, // assigned on first save
        CreatedBy = user.NtUser,
        QmmResponsible = user.CanEditArea(RoleCode.Qmm, valueStreamId) && !user.IsOwner ? user.DisplayName : null,
    };

    public SaveResult Save(PalletReturn edited, UserContext user, string? reason = null)
    {
        var original = edited.Id == 0 ? null : _repo.Get(edited.Id);
        var changes = Diff(original, edited).ToList();

        var cleared = changes.Where(c => c.Old is not null && c.New is null).Select(c => c.Field).Distinct().ToList();
        if (cleared.Count > 0) return new SaveResult(false, Array.Empty<ValidationIssue>(), Array.Empty<string>(), null, cleared);

        var denied = changes.Where(c => !CanEditForSave(user, original ?? edited, c.Field)).Select(c => c.Field).Distinct().ToList();
        if (denied.Count > 0) return new SaveResult(false, Array.Empty<ValidationIssue>(), denied, null);

        if (!string.IsNullOrWhiteSpace(edited.ReturnedMaterial))
            edited.ReturnedMaterial = Domain.Rules.Identifiers.NormalizeMaterial(edited.ReturnedMaterial);
        if (!string.IsNullOrWhiteSpace(edited.NewMaterial))
            edited.NewMaterial = Domain.Rules.Identifiers.NormalizeMaterial(edited.NewMaterial);

        var evaluation = PalletEvaluator.Evaluate(edited, Parameters, _clock.Today);
        if (evaluation.HasErrors) return new SaveResult(false, evaluation.Issues, Array.Empty<string>(), null);

        var isNew = edited.Id == 0;
        var folioBefore = edited.FolioNumber;
        if (edited.FolioNumber == 0) edited.FolioNumber = _repo.NextFolio(edited.ValueStreamId);
        var statusBefore = original?.Status;
        PalletEvaluator.Apply(edited, Parameters, _clock.Today);
        if (isNew) { edited.CreatedBy = string.IsNullOrWhiteSpace(edited.CreatedBy) ? user.NtUser : edited.CreatedBy; edited.CreatedAtUtc = _clock.UtcNow; }
        else { edited.ModifiedBy = user.NtUser; edited.ModifiedAtUtc = _clock.UtcNow; }
        try { _repo.Save(edited); }
        catch (Exception ex) when (ex is ConcurrencyException or DataRuleException)
        {
            if (isNew) { edited.Id = 0; edited.FolioNumber = folioBefore; }
            return new SaveResult(false, evaluation.Issues, Array.Empty<string>(), null, null, ex.Message);
        }

        var now = _clock.UtcNow;
        var entries = changes.Select(c => new AuditEntry(edited.Id, edited.FolioCode, c.Field, c.Old, c.New, user.NtUser, now, reason)).ToList();
        if (statusBefore != edited.Status)
            entries.Add(new AuditEntry(edited.Id, edited.FolioCode, "Status", statusBefore?.DisplayName(), edited.Status.DisplayName(), "SYSTEM", now, "Automatic recalculation"));
        _audit.Record(entries);
        return new SaveResult(true, evaluation.Issues, Array.Empty<string>(), edited);
    }

    private static bool CanEditForSave(UserContext user, PalletReturn reference, string field)
    {
        if (user.IsOwner) return true;
        // On create (Id = 0) the status is not closed yet: only area + VS are checked.
        return FieldPermissions.CanEdit(user, reference, field);
    }

    private static readonly PropertyInfo[] Tracked = Fields.All
        .Select(f => typeof(PalletReturn).GetProperty(f)!)
        .ToArray();

    public static IEnumerable<(string Field, string? Old, string? New)> Diff(PalletReturn? before, PalletReturn after)
    {
        foreach (var prop in Tracked)
        {
            var o = before is null ? null : Format(prop.GetValue(before));
            var n = Format(prop.GetValue(after));
            if (o != n && !(before is null && n is null)) yield return (prop.Name, o, n);
        }
    }

    private static string? Format(object? v) => v switch
    {
        null => null,
        string s when string.IsNullOrWhiteSpace(s) => null,
        DateTime d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        bool b => b ? "Yes" : "No",
        _ => Convert.ToString(v, CultureInfo.InvariantCulture),
    };
}
