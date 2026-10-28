using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DXSuite.Host.Setup;

/// <summary>
/// Everything the wizard collects, serialized to a JSON file the headless steps consume,
/// so the UI and the install-time custom actions share one contract. Only values the
/// customer must override end up in appsettings.Production.json; the rest inherit from the
/// shipped appsettings.json.
/// </summary>
public sealed class SetupConfig
{
    public SiteConfig Site { get; set; } = new();
    public DatabaseConfig Database { get; set; } = new();
    public AuthConfig Auth { get; set; } = new();
    public FilePathsConfig FilePaths { get; set; } = new();
    public CustomerConfig Customer { get; set; } = new();

    /// <summary>Runtime module gates written to <c>Modules:{name}:Enabled</c>.</summary>
    public Dictionary<string, bool> Modules { get; set; } = new();

    /// <summary>Feature flags written to <c>Features:{name}</c>.</summary>
    public Dictionary<string, bool> Features { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static SetupConfig FromJson(string json) =>
        JsonSerializer.Deserialize<SetupConfig>(json, JsonOptions)
        ?? throw new InvalidDataException("Setup config JSON was empty or invalid.");

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, ToJson());
    }

    public static SetupConfig Load(string path) => FromJson(File.ReadAllText(path));

    /// <summary>The standard temp location the MSI and the wizard agree on.</summary>
    public static string DefaultPath =>
        Path.Combine(Path.GetTempPath(), "dxsuite-setup.json");
}

public sealed class SiteConfig
{
    /// <summary>Where the published app is laid down (also the IIS physical path).</summary>
    public string InstallDir { get; set; } = @"C:\inetpub\DXSuite";
    public string Name { get; set; } = "DXSuite";
    public string AppPool { get; set; } = "DXSuite";

    /// <summary>Empty host header binds to all unassigned addresses on the port.</summary>
    public string Host { get; set; } = "";
    public int Port { get; set; } = 80;
}

/// <summary>
/// SQL Server settings. On-prem provisions only the Director database. Two credential sets:
/// <see cref="Admin"/> runs the bootstrap + EF migrations (needs CREATE DATABASE on the
/// server and db_owner on the target — Migrate runs DDL), <see cref="App"/> is what the app
/// connects with at runtime (only db_datareader / db_datawriter) and goes into the
/// connection strings.
/// </summary>
public sealed class DatabaseConfig
{
    public string Server { get; set; } = "";

    /// <summary>The Director database name. Bootstrap/Migrate/seed all target this.</summary>
    public string DirectorDatabase { get; set; } = "DXSuite_Director";

    public bool TrustServerCertificate { get; set; } = true;

    public SqlCredential Admin { get; set; } = new() { IntegratedSecurity = true };
    public SqlCredential App { get; set; } = new() { IntegratedSecurity = false };

    /// <summary>Run the idempotent seed (db/scripts/director-seed.sql) after migrating.</summary>
    public bool RunSeed { get; set; } = true;

    /// <summary>
    /// Set by the wizard only when an existing untracked DXSuite schema was found and the
    /// operator deliberately confirmed it matches the baseline. Gates baseline stamping.
    /// </summary>
    public bool OperatorConfirmedExistingBaseline { get; set; }

    /// <summary>
    /// Connection strings the app needs beyond Director. These databases are NOT provisioned
    /// here (CoreData/Aviation are an external system; the others are unused on-prem) — the
    /// values are only written into appsettings so the app can reach them. Logical name →
    /// physical database name; same Server + App credentials are assumed unless overridden.
    /// </summary>
    public Dictionary<string, string> ExternalDatabases { get; set; } = new();
}

public sealed class SqlCredential
{
    public bool IntegratedSecurity { get; set; }
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

/// <summary>
/// Authentication. <see cref="Type"/> selects the default scheme; the matching provider
/// block is enabled and its secrets written. Unselected providers are disabled.
/// </summary>
public sealed class AuthConfig
{
    /// <summary>One of: Local, Azure (Microsoft Entra), Google, Okta, Ldap.</summary>
    public string Type { get; set; } = "Local";
    public bool LocalAccountsEnabled { get; set; } = true;

    public MicrosoftAuth Microsoft { get; set; } = new();
    public OidcAuth Google { get; set; } = new();
    public OidcAuth Okta { get; set; } = new();
    public LdapAuth Ldap { get; set; } = new();
}

public sealed class MicrosoftAuth
{
    public string TenantId { get; set; } = "common";
    public string Domain { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
}

public sealed class OidcAuth
{
    public string Authority { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
}

public sealed class LdapAuth
{
    public string Server { get; set; } = "";
    public int Port { get; set; } = 389;
    public string BaseDn { get; set; } = "";
    public string ServiceAccountDn { get; set; } = "";
    public string ServiceAccountPassword { get; set; } = "";
    public string EmailDomainSuffix { get; set; } = "";
}

public sealed class FilePathsConfig
{
    /// <summary>UNC share or local folder that holds media assets across the deployment.</summary>
    public string BaseUNC { get; set; } = "";
    /// <summary>Externally reachable base URL the app advertises to devices/callbacks.</summary>
    public string SiteUrl { get; set; } = "";
}

public sealed class CustomerConfig
{
    public string Code { get; set; } = "";
    public string Tenant { get; set; } = "default";
}
