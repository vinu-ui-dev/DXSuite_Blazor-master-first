using Microsoft.Data.SqlClient;

namespace DXSuite.Host.Setup;

/// <summary>
/// Builds SQL Server connection strings that match the shape the app already uses (MARS on,
/// pooling, app-name tag, MultiSubnetFailover) so on-prem behaviour matches the development
/// connection strings in appsettings.json. Shared by the wizard (connection tests) and the
/// DB tool (provisioning + appsettings).
/// </summary>
public static class ConnectionStringFactory
{
    /// <summary>App-name tag per logical database, mirroring the shipped appsettings.json.</summary>
    private static readonly Dictionary<string, string> AppTags = new()
    {
        ["Director"] = "DXSuite_Director",
        ["CoreData"] = "DXSuite_CoreData",
        ["Aviation"] = "DXSuite_Aviation",
        ["Sentinel"] = "DXSuite_Sentinel",
        ["Design"] = "DXSuite_Design",
        ["Pulse"] = "DXSuite_Pulse",
    };

    /// <summary>Runtime connection string for a logical database, using the app account.</summary>
    public static string ForDatabase(DatabaseConfig db, string logicalName, string databaseName)
    {
        var b = BaseBuilder(db.Server, db.App, db.TrustServerCertificate);
        b.InitialCatalog = databaseName;
        b.ApplicationName = AppTags.TryGetValue(logicalName, out var tag) ? tag : $"DXSuite_{logicalName}";
        return b.ConnectionString;
    }

    /// <summary>Admin connection used for bootstrap + EF migrations, scoped to a database.</summary>
    public static string ForAdmin(DatabaseConfig db, string databaseName)
    {
        var b = BaseBuilder(db.Server, db.Admin, db.TrustServerCertificate);
        b.InitialCatalog = databaseName;
        b.ApplicationName = "DXSuite_Setup";
        return b.ConnectionString;
    }

    private static SqlConnectionStringBuilder BaseBuilder(string server, SqlCredential cred, bool trustCert)
    {
        var b = new SqlConnectionStringBuilder
        {
            DataSource = server,
            MultipleActiveResultSets = true,
            TrustServerCertificate = trustCert,
            MaxPoolSize = 200,
            ConnectTimeout = 30,
            MultiSubnetFailover = true,
        };

        if (cred.IntegratedSecurity)
        {
            b.IntegratedSecurity = true;
        }
        else
        {
            b.UserID = cred.Username;
            b.Password = cred.Password;
        }

        return b;
    }

    /// <summary>Returns the same connection string retargeted at a different database.</summary>
    public static string Retarget(string connectionString, string database) =>
        new SqlConnectionStringBuilder(connectionString) { InitialCatalog = database }.ConnectionString;
}
