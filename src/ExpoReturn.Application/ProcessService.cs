using System.Globalization;
using System.Reflection;
using System.Text.Json;
using ExpoReturn.Domain;

namespace ExpoReturn.Application;

public sealed record SaveResult(bool Success, IReadOnlyList<ValidationIssue> Issues, IReadOnlyList<string> DeniedFields, IbProcess? Saved,
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

/// <summary>Validation and status of a process before saving (preview in the editor).</summary>
public sealed record ProcessEvaluation(IReadOnlyList<ValidationIssue> Issues, IReadOnlyDictionary<PalletReturn, PalletEvaluation> Pallets,
                                       WorkflowStatus Status)
{
    public bool HasErrors => Issues.Any(i => i.Severity == Severity.Error);
}

/// <summary>A PO of the process that is already in other IB processes.</summary>
public sealed record PoInOtherProcess(string PoNumber, IReadOnlyList<string> Folios);

/// <summary>
/// IB PROCESS use cases: create, save (permissions → no-emptying → validation → recalculation → audit) and delete.
/// A field that already has a value can be changed but never left empty, for everyone.
/// </summary>
public sealed class ProcessService
{
    private readonly IProcessRepository _repo;
    private readonly IAuditTrail _audit;
    private readonly IClock _clock;
    public CalcParameters Parameters { get; }

    public ProcessService(IProcessRepository repo, IAuditTrail audit, IClock clock, CalcParameters? parameters = null)
    {
        _repo = repo; _audit = audit; _clock = clock;
        Parameters = parameters ?? CalcParameters.Default;
    }

    // ------------------------------------------------------------------ evaluation

    public ProcessEvaluation Evaluate(IbProcess p) => Evaluate(p, p.Id == 0 ? null : _repo.Get(p.Id));

    /// <summary>
    /// Records imported from the Excel 2.0 are validated like any other, but an error the saved record already had
    /// (Excel data) is shown as a warning "to homogenize" and does not block; only errors the edit introduces block.
    /// </summary>
    public ProcessEvaluation Evaluate(IbProcess p, IbProcess? saved)
    {
        var today = _clock.Today;
        var pallets = p.Pallets.ToDictionary(x => x, x => PalletEvaluator.Evaluate(x, Parameters, today));
        var status = IbProcess.StatusOf(pallets.Values.Select(e => e.Status).ToList());
        var issues = PalletValidator.ValidateProcess(p, today);
        if (p.IsLegacy)
        {
            var before = saved is null ? new HashSet<(string, string, int?)>()
                : PalletValidator.ValidateProcess(saved, today).Where(i => i.Severity == Severity.Error).Select(i => (i.Code, i.Field, i.Pallet)).ToHashSet();
            issues = issues.Select(i => i.Severity == Severity.Error && before.Contains((i.Code, i.Field, i.Pallet))
                    ? i with { Severity = Severity.Warning, Message = "Excel data to homogenize: " + i.Message, FromExcel = true }
                    : i with { FromExcel = i.Severity != Severity.Error })
                .ToList();
        }
        return new ProcessEvaluation(issues, pallets, status);
    }

    // ------------------------------------------------------------------ create

    /// <summary>
    /// New IB PROCESS with one empty pallet. Whoever creates it is QMM Responsible if their department is of the QMM
    /// area; otherwise they are the requestor, with their department as requesting department. Both stay editable.
    /// </summary>
    public IbProcess NewDraft(int valueStreamId, UserContext user)
    {
        var p = new IbProcess { ValueStreamId = valueStreamId, FolioNumber = 0, CreatedBy = user.NtUser };
        var dept = user.Grants.Where(g => g.ValueStreamId == valueStreamId && g.Function.CanCapture()).Select(g => g.DepartmentCode).FirstOrDefault()
                   ?? user.DepartmentCode;
        if (!string.IsNullOrWhiteSpace(dept))
        {
            if (RoleInfo.AreaOfDepartment(dept) == RoleCode.Qmm)
            {
                p.QmmResponsible = user.DisplayName;
                p.QmmResponsibleNtUser = user.NtUser;
            }
            else
            {
                p.RequestorName = user.DisplayName;
                p.RequestorNtUser = user.NtUser;
                p.RequestingDepartment = dept;
            }
        }
        p.NewPallet(user.NtUser);
        return p;
    }

    /// <summary>POs of the process already used in OTHER processes (the screen asks before saving).</summary>
    public IReadOnlyList<PoInOtherProcess> PoInOtherProcesses(IbProcess p)
    {
        var mine = p.Pallets.Select(x => x.ReturnPONumber?.Trim()).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        if (mine.Count == 0) return Array.Empty<PoInOtherProcess>();
        var used = _repo.FoliosUsingPo(mine!, p.Id);
        return mine.Select(po => new PoInOtherProcess(po!, used.TryGetValue(po!, out var f) ? f : Array.Empty<string>()))
            .Where(x => x.Folios.Count > 0).ToList();
    }

    // ------------------------------------------------------------------ save

    private sealed record Change(int? Line, PalletReturn? Pallet, string Field, string? Old, string? New);

    public SaveResult Save(IbProcess edited, UserContext user, string? reason = null)
    {
        var original = edited.Id == 0 ? null : _repo.Get(edited.Id);
        if (original is null && !FieldPermissions.CanCreate(user, edited.ValueStreamId))
            return new SaveResult(false, Array.Empty<ValidationIssue>(), new[] { $"create IB processes in VS{edited.ValueStreamId}" }, null);

        var context = original ?? edited;
        var changes = new List<Change>();
        changes.AddRange(DiffHeader(original, edited).Select(c => new Change(null, null, c.Field, c.Old, c.New)));
        foreach (var pallet in edited.Pallets)
        {
            var before = pallet.Id == 0 ? null : original?.Pallets.FirstOrDefault(x => x.Id == pallet.Id);
            changes.AddRange(DiffPallet(before, pallet).Select(c => new Change(pallet.LineNo, pallet, c.Field, c.Old, c.New)));
        }
        var removed = original?.Pallets.Where(o => edited.Pallets.All(x => x.Id != o.Id)).ToList() ?? new List<PalletReturn>();

        var cleared = changes.Where(c => c.Old is not null && c.New is null).Select(c => Label(c)).Distinct().ToList();
        if (cleared.Count > 0) return new SaveResult(false, Array.Empty<ValidationIssue>(), Array.Empty<string>(), null, cleared);

        var denied = changes.Where(c => !FieldPermissions.CanEdit(user, context,
                c.Pallet is null ? null : context.Pallets.FirstOrDefault(x => x.Id == c.Pallet.Id && x.Id != 0) ?? c.Pallet, c.Field))
            .Select(Label).Distinct().ToList();
        denied.AddRange(removed.Where(r => !FieldPermissions.CanRemovePallet(user, context, r)).Select(r => $"delete pallet {r.LineNo}"));
        if (denied.Count > 0) return new SaveResult(false, Array.Empty<ValidationIssue>(), denied, null);

        foreach (var p in edited.Pallets)
        {
            if (!string.IsNullOrWhiteSpace(p.ReturnedMaterial)) p.ReturnedMaterial = Domain.Rules.Identifiers.NormalizeMaterial(p.ReturnedMaterial);
            if (!string.IsNullOrWhiteSpace(p.NewMaterial)) p.NewMaterial = Domain.Rules.Identifiers.NormalizeMaterial(p.NewMaterial);
        }

        var evaluation = Evaluate(edited, original);
        if (evaluation.HasErrors) return new SaveResult(false, evaluation.Issues, Array.Empty<string>(), null);

        var isNew = edited.Id == 0;
        var folioBefore = edited.FolioNumber;
        var statusBefore = original?.Pallets.ToDictionary(p => p.Id, p => p.Status) ?? new Dictionary<int, WorkflowStatus>();
        if (edited.FolioNumber == 0) edited.FolioNumber = _repo.NextFolio(edited.ValueStreamId);
        PalletEvaluator.Apply(edited, Parameters, _clock.Today);
        var now = _clock.UtcNow;
        if (isNew) { edited.CreatedBy = string.IsNullOrWhiteSpace(edited.CreatedBy) ? user.NtUser : edited.CreatedBy; edited.CreatedAtUtc = now; }
        else { edited.ModifiedBy = user.NtUser; edited.ModifiedAtUtc = now; }
        foreach (var p in edited.Pallets)
        {
            if (p.Id == 0) { p.CreatedBy = user.NtUser; p.CreatedAtUtc = now; }
            else if (changes.Any(c => ReferenceEquals(c.Pallet, p)) || statusBefore.GetValueOrDefault(p.Id) != p.Status) { p.ModifiedBy = user.NtUser; p.ModifiedAtUtc = now; }
        }
        try { _repo.Save(edited); }
        catch (Exception ex) when (ex is ConcurrencyException or DataRuleException)
        {
            if (isNew) { edited.Id = 0; edited.FolioNumber = folioBefore; }
            return new SaveResult(false, evaluation.Issues, Array.Empty<string>(), null, null, ex.Message);
        }

        var entries = new List<AuditEntry>();
        foreach (var c in changes)
            entries.Add(new AuditEntry(c.Pallet?.Id ?? 0, c.Pallet?.FolioCode ?? edited.FolioCode, c.Field, c.Old, c.New, user.NtUser, now, reason, edited.Id));
        foreach (var r in removed)
            entries.Add(new AuditEntry(r.Id, r.FolioCode, "Pallet", Describe(r), null, user.NtUser, now, reason ?? "Pallet removed", edited.Id,
                                       Deleted: true, Snapshot: Snapshot(r)));
        foreach (var p in edited.Pallets.Where(p => statusBefore.GetValueOrDefault(p.Id) != p.Status))
            entries.Add(new AuditEntry(p.Id, p.FolioCode, "Status", statusBefore.TryGetValue(p.Id, out var s) ? s.DisplayName() : null,
                                       p.Status.DisplayName(), "SYSTEM", now, "Automatic recalculation", edited.Id));
        _audit.Record(entries);
        return new SaveResult(true, evaluation.Issues, Array.Empty<string>(), edited);
    }

    // ------------------------------------------------------------------ delete

    /// <summary>Deletes the process with its pallets. The audit log keeps a full copy (who, when, why, what).</summary>
    public SaveResult Delete(IbProcess process, UserContext user, string reason)
    {
        if (!FieldPermissions.CanDelete(user, process))
            return new SaveResult(false, Array.Empty<ValidationIssue>(), new[] { $"delete {process.FolioCode}" }, null);
        if (string.IsNullOrWhiteSpace(reason))
            return new SaveResult(false, Array.Empty<ValidationIssue>(), Array.Empty<string>(), null, null, "Enter the reason for deleting.");
        try { _repo.Delete(process.Id); }
        catch (Exception ex) when (ex is ConcurrencyException or DataRuleException)
        {
            return new SaveResult(false, Array.Empty<ValidationIssue>(), Array.Empty<string>(), null, null, ex.Message);
        }
        _audit.Record(new[] { new AuditEntry(0, process.FolioCode, "IB PROCESS", $"{process.FolioCode} · {process.PalletCount} pallet(s)", null,
            user.NtUser, _clock.UtcNow, reason.Trim(), process.Id, Deleted: true, Snapshot: Snapshot(process)) });
        return new SaveResult(true, Array.Empty<ValidationIssue>(), Array.Empty<string>(), null);
    }

    // ------------------------------------------------------------------ helpers

    private static string Label(Change c) => c.Line is { } n ? $"{c.Field} (pallet {n})" : c.Field;

    private static readonly PropertyInfo[] HeaderProps = Fields.Process.Concat(Fields.Linked.Keys)
        .Select(f => typeof(IbProcess).GetProperty(f)!).ToArray();
    private static readonly PropertyInfo[] PalletProps = Fields.Pallet.Select(f => typeof(PalletReturn).GetProperty(f)!).ToArray();

    public static IEnumerable<(string Field, string? Old, string? New)> DiffHeader(IbProcess? before, IbProcess after) => Diff(HeaderProps, before, after);
    public static IEnumerable<(string Field, string? Old, string? New)> DiffPallet(PalletReturn? before, PalletReturn after) => Diff(PalletProps, before, after);

    /// <summary>Pallet-only edit (still used by imports): every field of the pallet and of its process.</summary>
    public static IEnumerable<(string Field, string? Old, string? New)> Diff(PalletReturn? before, PalletReturn after) =>
        DiffHeader(before?.Process, after.Process).Concat(DiffPallet(before, after));

    private static IEnumerable<(string Field, string? Old, string? New)> Diff(PropertyInfo[] props, object? before, object after)
    {
        foreach (var prop in props)
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

    private static string Describe(PalletReturn p) =>
        $"{p.FolioCode} · {p.ReturnedMaterial} · {p.QtyImported} pcs · {p.OriginalHU ?? p.OriginalBatch} · PO {p.ReturnPONumber}";

    private static Dictionary<string, string?> Values(object o, IEnumerable<PropertyInfo> props) =>
        props.ToDictionary(p => p.Name, p => Format(p.GetValue(o)));

    /// <summary>JSON copy of what is deleted (audit log SNAPSHOT).</summary>
    public static string Snapshot(PalletReturn p) => JsonSerializer.Serialize(
        new { p.FolioCode, Fields = Values(p, PalletProps), p.StatusName, Balance = p.Financials.BalanceGeneralUsd });

    public static string Snapshot(IbProcess process) => JsonSerializer.Serialize(new
    {
        process.FolioCode, process.CreatedBy, Fields = Values(process, HeaderProps),
        Pallets = process.Pallets.Select(p => new { p.FolioCode, Fields = Values(p, PalletProps), p.StatusName }),
    });
}
