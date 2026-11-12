using ExpoReturn.Application;
using Oracle.ManagedDataAccess.Client;
using static ExpoReturn.Infrastructure.Oracle.OracleDb;

namespace ExpoReturn.Infrastructure.Oracle;

/// <summary>
/// Users directory in IB_PROCESS_USERS, IB_PROCESS_USER_ACCESS, IB_PROCESS_PROFILE_REQUESTS and IB_PROCESS_REQUEST_VS.
/// Nothing is deleted: an access row that ends gets VALID_TO / REMOVED_BY (history), a function change closes the row and
/// inserts a new one, a request goes from PENDING to APPROVED / REJECTED. A filled field is never set to NULL.
/// OWNER rows have no department and no Value Stream (NULL in Oracle, "" / 0 in the application).
/// </summary>
public sealed class OracleUserStore : IUserStoreBackend
{
    private readonly OracleDb _db;
    public OracleUserStore(OracleDb db) => _db = db;
    public string Description => $"Oracle {_db.Server} · IB_PROCESS_USERS";

    private static string FunctionCode(UserFunction f) => f.ToString().ToUpperInvariant();
    private static UserFunction FunctionOf(string? code) => Enum.TryParse<UserFunction>(code, true, out var f) ? f : UserFunction.Viewer;

    public UserStoreSnapshot Load()
    {
        var access = _db.Query($@"SELECT NT_USER, DEPARTMENT_CODE, VALUE_STREAM_ID, FUNCTION_CODE FROM {_db.T("IB_PROCESS_USER_ACCESS")}
                                   WHERE VALID_TO IS NULL ORDER BY ACCESS_ID",
            r => new UserAccess(Str(r, "NT_USER")!, Str(r, "DEPARTMENT_CODE") ?? "", Int(r, "VALUE_STREAM_ID") ?? 0, FunctionOf(Str(r, "FUNCTION_CODE"))));
        var users = _db.Query($@"SELECT NT_USER, DISPLAY_NAME, DEPARTMENT_CODE, CREATED_AT FROM {_db.T("IB_PROCESS_USERS")} WHERE IS_ACTIVE = 'Y'",
            r =>
            {
                var nt = Str(r, "NT_USER")!;
                var vss = access.Where(a => a.NtUser == nt && a.Function != UserFunction.Owner).Select(a => a.ValueStreamId)
                                .Distinct().OrderBy(v => v).ToList();
                return new AppUser(nt, Str(r, "DISPLAY_NAME") ?? nt, Str(r, "DEPARTMENT_CODE"), vss, Utc(r, "CREATED_AT") ?? DateTime.UtcNow);
            });
        var requestVs = _db.Query($"SELECT REQUEST_ID, VALUE_STREAM_ID FROM {_db.T("IB_PROCESS_REQUEST_VS")}",
                r => (Id: Int(r, "REQUEST_ID")!.Value, Vs: Int(r, "VALUE_STREAM_ID")!.Value))
            .ToLookup(x => x.Id, x => x.Vs);
        var requests = _db.Query($@"SELECT * FROM {_db.T("IB_PROCESS_PROFILE_REQUESTS")} ORDER BY REQUESTED_AT", r =>
        {
            var id = Int(r, "REQUEST_ID")!.Value;
            return new ProfileRequest(id, Str(r, "NT_USER")!, Str(r, "DEPARTMENT_CODE")!, requestVs[id].OrderBy(v => v).ToList(),
                Utc(r, "REQUESTED_AT") ?? DateTime.UtcNow,
                Enum.TryParse<RequestStatus>(Str(r, "STATUS"), true, out var s) ? s : RequestStatus.Pending,
                Str(r, "DECIDED_BY"), Utc(r, "DECIDED_AT"), Str(r, "DECISION_COMMENT"));
        });
        return new UserStoreSnapshot(users, access, requests);
    }

    public IReadOnlyDictionary<int, int> Apply(UserStoreChanges changes, UserStoreSnapshot after) => _db.InTransaction((c, tx) =>
    {
        var by = Cut(changes.ChangedBy, 64);
        foreach (var u in changes.NewUsers)
        {
            // MERGE: the same person may have registered from another PC a moment ago.
            using var cmd = _db.Command(c, $@"MERGE INTO {_db.T("IB_PROCESS_USERS")} d USING (SELECT :nt AS NT FROM DUAL) s ON (d.NT_USER = s.NT)
                WHEN NOT MATCHED THEN INSERT (NT_USER, DISPLAY_NAME, DEPARTMENT_CODE) VALUES (:nt, :name, :dept)", tx);
            cmd.Parameters.Add("nt", u.NtUser);
            cmd.Parameters.Add("name", Cut(u.DisplayName, 150));
            cmd.Parameters.Add("dept", Db(u.DepartmentCode));
            cmd.ExecuteNonQuery();
        }
        foreach (var u in changes.UpdatedUsers)
        {
            // NVL: a value already stored is never replaced by an empty one.
            using var cmd = _db.Command(c, $@"UPDATE {_db.T("IB_PROCESS_USERS")}
                   SET DISPLAY_NAME = NVL(:name, DISPLAY_NAME), DEPARTMENT_CODE = NVL(:dept, DEPARTMENT_CODE)
                 WHERE NT_USER = :nt", tx);
            cmd.Parameters.Add("name", Db(Cut(u.DisplayName, 150)));
            cmd.Parameters.Add("dept", Db(u.DepartmentCode));
            cmd.Parameters.Add("nt", u.NtUser);
            cmd.ExecuteNonQuery();
        }
        foreach (var a in changes.EndedAccess)
        {
            using var cmd = _db.Command(c, $@"UPDATE {_db.T("IB_PROCESS_USER_ACCESS")}
                   SET VALID_TO = SYSTIMESTAMP, REMOVED_BY = :changedBy
                 WHERE NT_USER = :nt AND NVL(DEPARTMENT_CODE, '-') = :dept AND NVL(VALUE_STREAM_ID, 0) = :vs
                   AND FUNCTION_CODE = :fn AND VALID_TO IS NULL", tx);
            cmd.Parameters.Add("changedBy", by);
            cmd.Parameters.Add("nt", a.NtUser);
            cmd.Parameters.Add("dept", string.IsNullOrEmpty(a.DepartmentCode) ? "-" : a.DepartmentCode);
            cmd.Parameters.Add("vs", a.ValueStreamId);
            cmd.Parameters.Add("fn", FunctionCode(a.Function));
            cmd.ExecuteNonQuery();
        }
        foreach (var a in changes.AddedAccess)
        {
            EnsureUser(c, tx, a.NtUser);
            var owner = a.Function == UserFunction.Owner;
            using var cmd = _db.Command(c, $@"INSERT INTO {_db.T("IB_PROCESS_USER_ACCESS")} (NT_USER, DEPARTMENT_CODE, VALUE_STREAM_ID, FUNCTION_CODE, ASSIGNED_BY)
                                              VALUES (:nt, :dept, :vs, :fn, :changedBy)", tx);
            cmd.Parameters.Add("nt", a.NtUser);
            cmd.Parameters.Add("dept", owner ? DBNull.Value : Db(a.DepartmentCode));
            cmd.Parameters.Add("vs", owner || a.ValueStreamId == 0 ? DBNull.Value : a.ValueStreamId);
            cmd.Parameters.Add("fn", FunctionCode(a.Function));
            cmd.Parameters.Add("changedBy", by);
            cmd.ExecuteNonQuery();
        }
        foreach (var r in changes.DecidedRequests)
        {
            using var cmd = _db.Command(c, $@"UPDATE {_db.T("IB_PROCESS_PROFILE_REQUESTS")}
                   SET STATUS = :status, DECIDED_BY = :changedBy, DECIDED_AT = SYSTIMESTAMP, DECISION_COMMENT = NVL(:cmt, DECISION_COMMENT)
                 WHERE REQUEST_ID = :id AND STATUS = 'PENDING'", tx);
            cmd.Parameters.Add("status", r.Status.ToString().ToUpperInvariant());
            cmd.Parameters.Add("changedBy", Cut(r.DecidedBy ?? changes.ChangedBy, 64));
            cmd.Parameters.Add("cmt", Db(Cut(r.Comment, 500)));
            cmd.Parameters.Add("id", r.Id);
            cmd.ExecuteNonQuery();
        }
        var ids = new Dictionary<int, int>();
        foreach (var r in changes.NewRequests)
        {
            using var cmd = _db.Command(c, $@"INSERT INTO {_db.T("IB_PROCESS_PROFILE_REQUESTS")} (NT_USER, DEPARTMENT_CODE)
                                              VALUES (:nt, :dept) RETURNING REQUEST_ID INTO :newId", tx);
            cmd.Parameters.Add("nt", r.NtUser);
            cmd.Parameters.Add("dept", r.DepartmentCode);
            var id = cmd.Parameters.Add("newId", OracleDbType.Int32, System.Data.ParameterDirection.Output);
            cmd.ExecuteNonQuery();
            var newId = Convert.ToInt32(id.Value.ToString());
            ids[r.Id] = newId;
            foreach (var vs in r.ValueStreams)
            {
                using var v = _db.Command(c, $"INSERT INTO {_db.T("IB_PROCESS_REQUEST_VS")} (REQUEST_ID, VALUE_STREAM_ID) VALUES (:id, :vs)", tx);
                v.Parameters.Add("id", newId);
                v.Parameters.Add("vs", vs);
                v.ExecuteNonQuery();
            }
        }
        return (IReadOnlyDictionary<int, int>)ids;
    });

    /// <summary>Access can be given to someone who never opened the app: the user row is created with the account as name.</summary>
    private void EnsureUser(OracleConnection c, OracleTransaction tx, string nt)
    {
        using var cmd = _db.Command(c, $@"MERGE INTO {_db.T("IB_PROCESS_USERS")} d USING (SELECT :nt AS NT FROM DUAL) s ON (d.NT_USER = s.NT)
            WHEN NOT MATCHED THEN INSERT (NT_USER, DISPLAY_NAME) VALUES (:nt, :nt)", tx);
        cmd.Parameters.Add("nt", nt);
        cmd.ExecuteNonQuery();
    }

    private static string? Cut(string? s, int max) => OracleAuditTrail.Cut(s, max);
}
