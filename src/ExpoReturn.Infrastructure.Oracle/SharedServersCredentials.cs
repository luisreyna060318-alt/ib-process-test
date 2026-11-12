using System.Reflection;

namespace ExpoReturn.Infrastructure.Oracle;

/// <summary>
/// Connection settings, fixed in the code (there is no configuration file next to the executable).
/// </summary>
public sealed class OracleSettings
{
    /// <summary>Master key of SharedServersAccess: the connection is automatic, nobody types it.</summary>
    public const string MasterKey = "1234";

    /// <summary>Server title in SharedServersAccess; it is also the Data Source (TNS alias).</summary>
    public string Server { get; set; } = "IFACTJUP";
    /// <summary>
    /// Only used when the DLL is not embedded in the application (build without lib/SharedServersAccess.dll):
    /// path of the file; relative = next to the executable.
    /// </summary>
    public string SharedServersDll { get; set; } = "SharedServersAccess.dll";
    /// <summary>Optional servers.ini path for DiccionarioService(path); empty = the DLL's own default.</summary>
    public string? SharedServersIni { get; set; }
    /// <summary>Folder with tnsnames.ora / sqlnet.ora; empty = TNS_ADMIN, then %ORACLE_HOME%\network\admin.</summary>
    public string? TnsAdmin { get; set; }
    /// <summary>Schema that owns the IB_PROCESS_ tables when the account is not the owner (empty = own schema).</summary>
    public string? Schema { get; set; }
    /// <summary>Rows shown in the Audit log screen (most recent first).</summary>
    public int AuditRowsShown { get; set; } = 5000;
}

public sealed record OracleLogin(string User, string Password, string DataSource);

/// <summary>Error of the credentials step (wrong key, server not found, access expired, DLL missing…).</summary>
public sealed class CredentialsException(string message, int? code = null, Exception? inner = null) : Exception(message, inner)
{
    public int? Code { get; } = code;
}

/// <summary>
/// Reads the Oracle account from SharedServersAccess.dll the same way the good-receipt worker does:
/// AccessControl.InicializarClaveMaestra(key) → new DiccionarioService().ObtenerCredencialesDeServidor(SERVER) →
/// Conexion / ConexionFormato (Data Source=…;User ID=…;Password=…) with {userX}/{passwordX} replaced by Usuario / Password.
/// The DLL is a .NET 2.0 assembly loaded at run time (reflection). It is embedded in the application and loaded from
/// memory, so the user only needs the executable; a copy next to the executable is used only if it was not embedded.
/// </summary>
public sealed class SharedServersCredentials
{
    private readonly OracleSettings _settings;
    private Assembly? _assembly;
    private bool _keyReady;

    public SharedServersCredentials(OracleSettings settings) => _settings = settings;

    public string DllPath => Path.IsPathRooted(_settings.SharedServersDll)
        ? _settings.SharedServersDll
        : Path.Combine(AppContext.BaseDirectory, _settings.SharedServersDll);

    /// <summary>True when SharedServersAccess.dll travels inside the application.</summary>
    public static bool IsEmbedded => typeof(SharedServersCredentials).Assembly.GetManifestResourceInfo(EmbeddedName) is not null;

    private const string EmbeddedName = "SharedServersAccess.dll";

    /// <summary>Loads the DLL (embedded first, else the file) without running any of its code.</summary>
    public static Assembly LoadAssembly(string fallbackPath)
    {
        using (var stream = typeof(SharedServersCredentials).Assembly.GetManifestResourceStream(EmbeddedName))
            if (stream is not null)
            {
                var bytes = new byte[stream.Length];
                stream.ReadExactly(bytes);
                // Already loaded in this process (a reconnection): reuse it, a second copy would have other types.
                return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "SharedServersAccess")
                       ?? Assembly.Load(bytes);
            }
        if (!File.Exists(fallbackPath))
            throw new CredentialsException($"SharedServersAccess.dll is not inside the application and was not found at {fallbackPath}.");
        return Assembly.LoadFrom(fallbackPath);
    }

    /// <summary>Initializes the master key and checks it decrypts the server entry. Call once at start-up.</summary>
    public void Initialize(string masterKey)
    {
        if (string.IsNullOrEmpty(masterKey)) throw new CredentialsException("The master key is empty.");
        var access = Type("SharedServersAccess.AccessControl");
        Invoke(() => access.GetMethod("InicializarClaveMaestra", new[] { typeof(string) })!.Invoke(null, new object[] { masterKey }));
        _keyReady = true;
        try { GetLogin(); }
        catch { _keyReady = false; throw; }
    }

    /// <summary>Asks the DLL for the account every time a connection pool is created (credentials may be renewed).</summary>
    public OracleLogin GetLogin()
    {
        if (!_keyReady) throw new CredentialsException("The SharedServersAccess master key was not initialized.");
        var serviceType = Type("SharedServersAccess.DiccionarioService");
        var service = Invoke(() => string.IsNullOrWhiteSpace(_settings.SharedServersIni)
            ? Activator.CreateInstance(serviceType)!
            : Activator.CreateInstance(serviceType, Environment.ExpandEnvironmentVariables(_settings.SharedServersIni))!);
        var method = serviceType.GetMethod("ObtenerCredencialesDeServidor", new[] { typeof(string), typeof(bool) })
                     ?? throw new CredentialsException("SharedServersAccess.dll: ObtenerCredencialesDeServidor(string, bool) not found.");
        var cred = Invoke(() => method.Invoke(service, new object[] { _settings.Server, false }))
                   ?? throw new CredentialsException($"SharedServersAccess returned nothing for '{_settings.Server}'.");
        string Prop(string name) => cred.GetType().GetProperty(name)?.GetValue(cred) as string ?? "";
        return Parse(_settings.Server, Prop("Conexion"), Prop("ConexionFormato"), Prop("Usuario"), Prop("Password"));
    }

    /// <summary>Same rules as worker_recibos_sap.py: the Data Source must be the server title, otherwise the key is wrong.</summary>
    public static OracleLogin Parse(string server, string? conexion, string? conexionFormato, string? usuario, string? password)
    {
        var text = !string.IsNullOrEmpty(conexion) ? conexion : conexionFormato ?? "";
        var parts = ConnectionStringParts(text);
        var dsn = parts.GetValueOrDefault("data source");
        if (string.IsNullOrWhiteSpace(dsn) || !string.Equals(dsn.Trim(), server, StringComparison.OrdinalIgnoreCase))
            throw new CredentialsException($"The connection string of '{server}' is not valid (Data Source: " +
                                           $"{(string.IsNullOrEmpty(dsn) ? "empty" : dsn.Length > 30 ? dsn[..30] : dsn)}). Check the master key.");
        string Value(string key, string? property, string marker)
        {
            var inText = parts.GetValueOrDefault(key, "");
            return inText.Length > 0 && !inText.Equals(marker, StringComparison.OrdinalIgnoreCase) ? inText : property ?? "";
        }
        var user = Value("user id", usuario, "{userX}");
        var pwd = Value("password", password, "{passwordX}");
        if (user.Length == 0 || pwd.Length == 0 || user.Contains("{userX}", StringComparison.OrdinalIgnoreCase)
            || pwd.Contains("{passwordX}", StringComparison.OrdinalIgnoreCase))
            throw new CredentialsException($"SharedServersAccess did not return user / password for '{server}'.");
        return new OracleLogin(user, pwd, dsn.Trim());
    }

    /// <summary>Splits "Key=Value;Key2='a;b'" (keys lower case; quotes allowed around values).</summary>
    public static Dictionary<string, string> ConnectionStringParts(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string key = "", value = "";
        char? quote = null;
        var inValue = false;
        void Flush()
        {
            if (key.Trim().Length > 0) result[key.Trim().ToLowerInvariant()] = value.Trim();
            key = ""; value = ""; inValue = false;
        }
        foreach (var c in text)
        {
            if (inValue)
            {
                if (quote is not null) { if (c == quote) quote = null; else value += c; }
                else if ((c == '"' || c == '\'') && value.Trim().Length == 0) quote = c;
                else if (c == ';') Flush();
                else value += c;
            }
            else if (c == '=') inValue = true;
            else if (c != ';') key += c;
        }
        Flush();
        return result;
    }

    private Type Type(string name)
    {
        if (_assembly is null)
        {
            try { _assembly = LoadAssembly(DllPath); }
            catch (CredentialsException) { throw; }
            catch (Exception ex) { throw new CredentialsException($"SharedServersAccess.dll could not be loaded: {ex.Message}", null, ex); }
        }
        return _assembly.GetType(name, throwOnError: false) ?? throw new CredentialsException($"SharedServersAccess.dll: type {name} not found.");
    }

    /// <summary>Unwraps reflection errors; SharedServersAccess.CustomException carries CodigoError.</summary>
    private static T Invoke<T>(Func<T> call)
    {
        try { return call(); }
        catch (TargetInvocationException tie) when (tie.InnerException is { } inner)
        {
            var code = inner.GetType().GetProperty("CodigoError")?.GetValue(inner) as int?;
            throw new CredentialsException(inner.Message.Trim(), code, inner);
        }
    }

    private static void Invoke(Action call) => Invoke(() => { call(); return 0; });
}
