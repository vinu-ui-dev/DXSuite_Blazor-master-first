using System.Text.Json;
using System.Text.Json.Nodes;

namespace DXSuite.Host.Setup;

/// <summary>
/// Produces <c>appsettings.Production.json</c> next to the deployed app. ASP.NET Core layers
/// this over the shipped <c>appsettings.json</c> (Production loads by default when
/// ASPNETCORE_ENVIRONMENT is unset under IIS), so only customer-specific keys are written;
/// everything else inherits the shipped defaults.
/// </summary>
public static class AppSettingsWriter
{
    public static string Write(SetupConfig cfg, string installDir)
    {
        var conn = BuildConnectionStrings(cfg.Database);

        var root = new JsonObject
        {
            ["ConnectionStrings"] = Clone(conn),
            ["Authentication"] = BuildAuthentication(cfg.Auth),
            ["FilePaths"] = new JsonObject
            {
                ["BaseUNC"] = cfg.FilePaths.BaseUNC,
                ["SiteUrl"] = cfg.FilePaths.SiteUrl,
            },
            ["CustomerInfo"] = new JsonObject { ["CustomerCode"] = cfg.Customer.Code },
            // On-prem is always a single standalone process.
            ["DXSuite"] = new JsonObject
            {
                ["Role"] = "standalone",
                ["DefaultTenant"] = cfg.Customer.Tenant,
            },
        };

        if (cfg.Modules.Count > 0)
        {
            var modules = new JsonObject();
            foreach (var (name, enabled) in cfg.Modules)
                modules[name] = new JsonObject { ["Enabled"] = enabled };
            root["Modules"] = modules;
        }

        if (cfg.Features.Count > 0)
        {
            var features = new JsonObject();
            foreach (var (name, enabled) in cfg.Features)
                features[name] = enabled;
            root["Features"] = features;
        }

        root["Tenants"] = new JsonObject
        {
            ["Default"] = new JsonObject
            {
                ["TenantId"] = cfg.Customer.Tenant,
                ["CustomerCode"] = cfg.Customer.Code,
                ["IsEnabled"] = true,
                ["ConnectionStrings"] = Clone(conn),
            },
        };

        var path = Path.Combine(installDir, "appsettings.Production.json");
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    private static JsonObject BuildConnectionStrings(DatabaseConfig db)
    {
        var o = new JsonObject
        {
            // The only database the installer provisions and migrates.
            ["Director"] = ConnectionStringFactory.ForDatabase(db, "Director", db.DirectorDatabase),
        };

        // External / unprovisioned databases the app still needs to reach (e.g. CoreData,
        // Aviation). Written with the same server + app account unless the operator changed
        // the names. Not migrated here.
        foreach (var (logical, dbName) in db.ExternalDatabases)
        {
            if (!string.IsNullOrWhiteSpace(dbName))
                o[logical] = ConnectionStringFactory.ForDatabase(db, logical, dbName);
        }

        return o;
    }

    private static JsonObject BuildAuthentication(AuthConfig auth)
    {
        var providers = new JsonObject
        {
            ["Microsoft"] = new JsonObject
            {
                ["Enabled"] = auth.Type == "Azure",
                ["Instance"] = "https://login.microsoftonline.com/",
                ["Domain"] = auth.Microsoft.Domain,
                ["TenantId"] = auth.Microsoft.TenantId,
                ["ClientId"] = auth.Microsoft.ClientId,
                ["ClientSecret"] = auth.Microsoft.ClientSecret,
                ["CallbackPath"] = "/signin-oidc",
            },
            ["Google"] = new JsonObject
            {
                ["Enabled"] = auth.Type == "Google",
                ["Authority"] = string.IsNullOrWhiteSpace(auth.Google.Authority)
                    ? "https://accounts.google.com" : auth.Google.Authority,
                ["ClientId"] = auth.Google.ClientId,
                ["ClientSecret"] = auth.Google.ClientSecret,
                ["CallbackPath"] = "/signin-oidc-google",
            },
            ["Okta"] = new JsonObject
            {
                ["Enabled"] = auth.Type == "Okta",
                ["Authority"] = auth.Okta.Authority,
                ["ClientId"] = auth.Okta.ClientId,
                ["ClientSecret"] = auth.Okta.ClientSecret,
                ["CallbackPath"] = "/signin-oidc-okta",
            },
        };

        return new JsonObject
        {
            ["AuthenticationType"] = auth.Type == "Azure" ? "Azure" : "Local",
            ["Providers"] = providers,
            ["LocalAccounts"] = new JsonObject { ["Enabled"] = auth.LocalAccountsEnabled },
            ["Ldap"] = new JsonObject
            {
                ["Enabled"] = auth.Type == "Ldap",
                ["Server"] = auth.Ldap.Server,
                ["Port"] = auth.Ldap.Port,
                ["BaseDn"] = auth.Ldap.BaseDn,
                ["ServiceAccountDn"] = auth.Ldap.ServiceAccountDn,
                ["ServiceAccountPassword"] = auth.Ldap.ServiceAccountPassword,
                ["EmailDomainSuffix"] = auth.Ldap.EmailDomainSuffix,
            },
        };
    }

    /// <summary>A JsonNode can only live in one parent, so deep-copy for reuse.</summary>
    private static JsonNode Clone(JsonObject source) => JsonNode.Parse(source.ToJsonString())!;
}
