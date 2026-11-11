using System.Data;
using System.Text.RegularExpressions;
using ExpoReturn.Application;
using Oracle.ManagedDataAccess.Client;

namespace ExpoReturn.Infrastructure.Oracle;

/// <summary>
/// Opens connections to the IB_PROCESS_ schema with the account SharedServersAccess returns. The Data Source is a
/// TNS alias resolved with tnsnames.ora (like the Python worker: IFACTJUP → IFACTJUP.&lt;NAMES.DEFAULT_DOMAIN&gt;).
/// Oracle errors raised by the integrity triggers (ORA-20010..20012) become <see cref="DataRuleException"/>.
/// </summary>
public sealed partial class OracleDb
{
    private readonly OracleSettings _settings;
    private readonly SharedServersCredentials _credentials;
    private readonly object _gate = new();
    private string? _connectionString;

    public OracleDb(OracleSettings settings, SharedServersCredentials credentials)
    {
        _settings = settings;
        _credentials = credentials;
        if (TnsFolder() is { } dir) OracleConfiguration.TnsAdmin = dir;
    }

    public OracleSettings Settings => _settings;
    public string Server => _settings.Server;
    public string? UserName { get; private set; }

    /// <summary>Table name with the schema prefix when configured.</summary>
    public string T(string table) => string.IsNullOrWhiteSpace(_settings.Schema) ? table : $"{_settings.Schema.Trim().ToUpperInvariant()}.{table}";

    public string? TnsFolder()
    {
        if (!string.IsNullOrWhiteSpace(_settings.TnsAdmin)) return Environment.ExpandEnvironmentVariables(_settings.TnsAdmin);
        if (Environment.GetEnvironmentVariable("TNS_ADMIN") is { Length: > 0 } t) return t;
        if (Environment.GetEnvironmentVariable("ORACLE_HOME") is { Length: > 0 } h && Directory.Exists(Path.Combine(h, "network", "admin")))
            return Path.Combine(h, "network", "admin");
        return null;
    }

    /// <summary>Completes the alias as the Oracle client does: NAMES.DEFAULT_DOMAIN of sqlnet.ora, else the only "alias.domain" entry.</summary>
    public string TnsName(string alias)
    {
        var dir = TnsFolder();
        if (dir is null || alias.IndexOfAny("./:()".ToCharArray()) >= 0) return alias;
        string Read(string file) { try { return File.ReadAllText(Path.Combine(dir, file)); } catch { return ""; } }
        var domain = DefaultDomainRx().Match(Read("sqlnet.ora"));
        if (domain.Success) return $"{alias}.{domain.Groups[1].Value}";
        var entries = Regex.Matches(Read("tnsnames.ora"), $@"^\s*({Regex.Escape(alias)}\.[\w.]+)\s*=", RegexOptions.IgnoreCase | RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return entries.Count == 1 ? entries[0] : alias;
    }

    private string ConnectionString(bool renew)
    {
        lock (_gate)
        {
            if (_connectionString is null || renew)
            {
                var login = _credentials.GetLogin();
                UserName = login.User;
                _connectionString = new OracleConnectionStringBuilder
                {
                    UserID = login.User, Password = login.Password, DataSource = TnsName(login.DataSource),
                    ConnectionTimeout = 20,
                }.ConnectionString;
            }
            return _connectionString;
        }
    }

    public OracleConnection Open()
    {
        try { return OpenWith(ConnectionString(false)); }
        catch (OracleException ex) when (ex.Number is 1017 or 28000 or 28001)   // credentials renewed / expired: ask the DLL again
        {
            return OpenWith(ConnectionString(true));
        }
    }

    private static OracleConnection OpenWith(string cs)
    {
        var c = new OracleConnection(cs);
        c.Open();
        return c;
    }

    public OracleCommand Command(OracleConnection c, string sql, OracleTransaction? tx = null)
    {
        var cmd = c.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = sql;
        if (tx is not null) cmd.Transaction = tx;
        return cmd;
    }

    /// <summary>Runs work in one transaction; trigger errors become DataRuleException.</summary>
    public T InTransaction<T>(Func<OracleConnection, OracleTransaction, T> work)
    {
        using var c = Open();
        using var tx = c.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {
            var result = work(c, tx);
            tx.Commit();
            return result;
        }
        catch (OracleException ex)
        {
            tx.Rollback();
            throw Translate(ex);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public void InTransaction(Action<OracleConnection, OracleTransaction> work) => InTransaction((c, t) => { work(c, t); return 0; });

    public List<T> Query<T>(string sql, Func<OracleDataReader, T> map, params (string Name, object? Value)[] args)
    {
        using var c = Open();
        using var cmd = Command(c, sql);
        foreach (var (n, v) in args) cmd.Parameters.Add(n, v ?? DBNull.Value);
        cmd.FetchSize = 1024 * 1024;
        using var r = cmd.ExecuteReader();
        var list = new List<T>();
        while (r.Read()) list.Add(map(r));
        return list;
    }

    public static Exception Translate(OracleException ex) => ex.Number switch
    {
        20010 or 20011 or 20012 => new DataRuleException(CleanMessage(ex.Message)),
        1 => new DataRuleException("The record already exists (unique key): " + CleanMessage(ex.Message)),
        2290 => new DataRuleException("A database rule was not met (check constraint): " + CleanMessage(ex.Message)),
        2291 => new DataRuleException("A referenced value does not exist (foreign key): " + CleanMessage(ex.Message)),
        2292 => new DataRuleException("The record is referenced and cannot be removed."),
        12899 => new DataRuleException("A value is too long for its column: " + CleanMessage(ex.Message)),
        1438 => new DataRuleException("A number is larger than its column allows: " + CleanMessage(ex.Message)),
        1400 or 1407 => new DataRuleException("A required value is missing: " + CleanMessage(ex.Message)),
        _ => ex,
    };

    private static string CleanMessage(string m)
    {
        var first = m.Split('\n')[0].Trim();
        return OraPrefixRx().Replace(first, "");
    }

    /// <summary>Connection check used at start-up: the tables exist and the account can read them.</summary>
    public string Check()
    {
        using var c = Open();
        using var cmd = Command(c, $"SELECT COUNT(*) FROM {T("IB_PROCESS_VALUE_STREAMS")}");
        var vs = Convert.ToInt32(cmd.ExecuteScalar());
        return $"Oracle {Server} · {UserName} · {vs} value streams";
    }

    // ------------------------------------------------------------------ value helpers

    public static object Db(object? v) => v switch
    {
        null => DBNull.Value,
        string s when string.IsNullOrWhiteSpace(s) => DBNull.Value,
        string s => s.Trim(),
        bool b => b ? "Y" : "N",
        DateTime d => d,
        _ => v,
    };

    public static string? Str(OracleDataReader r, string col) { var i = r.GetOrdinal(col); return r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i)); }
    public static int? Int(OracleDataReader r, string col) { var i = r.GetOrdinal(col); return r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i)); }
    public static decimal? Dec(OracleDataReader r, string col) { var i = r.GetOrdinal(col); return r.IsDBNull(i) ? null : Convert.ToDecimal(r.GetValue(i)); }
    public static DateTime? Date(OracleDataReader r, string col) { var i = r.GetOrdinal(col); return r.IsDBNull(i) ? null : r.GetDateTime(i); }
    public static bool Flag(OracleDataReader r, string col) => Str(r, col) == "Y";
    /// <summary>TIMESTAMP columns hold the database server local time (SYSTIMESTAMP).</summary>
    public static DateTime? Utc(OracleDataReader r, string col) => Date(r, col) is { } d ? DateTime.SpecifyKind(d, DateTimeKind.Local).ToUniversalTime() : null;
    public static DateTime Local(DateTime utc) => utc.Kind == DateTimeKind.Utc ? utc.ToLocalTime() : utc;

    [GeneratedRegex(@"^\s*NAMES\.DEFAULT_DOMAIN\s*=\s*([^\s#]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex DefaultDomainRx();
    [GeneratedRegex(@"^ORA-\d+:\s*")]
    private static partial Regex OraPrefixRx();
}
