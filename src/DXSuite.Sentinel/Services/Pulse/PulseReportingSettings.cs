using Microsoft.Extensions.Configuration;

namespace DXSuite.Sentinel.Services.Pulse;

/// <summary>
/// Per-tenant switches for publishing Sentinel device health into a customer's Pulse
/// database. Disabled by default so an upgrade never starts writing into a live Pulse
/// unattended.
/// </summary>
public sealed record PulseReportingSettings
{
    public const string SectionName = "Sentinel:PulseReporting";

    public bool Enabled { get; init; }

    /// <summary>Pulse <c>CustomerID</c> every reading and heartbeat is written under.</summary>
    public string CustomerId { get; init; } = "";

    public int CycleSeconds { get; init; } = 60;

    /// <summary>Open issues are re-written at this interval so Pulse's reading pruning never drops them.</summary>
    public int ReassertHours { get; init; } = 24;
}

/// <summary>A tenant's reporting settings and the Pulse connection string they apply to.</summary>
public sealed record ResolvedPulseReporting(
    string Subdomain,
    PulseReportingSettings Settings,
    string? ConnectionString);

/// <summary>
/// Resolves a tenant's Pulse reporting settings key by key from
/// <c>Tenants:&lt;subdomain&gt;:Sentinel:PulseReporting</c>, falling back to the top-level
/// <c>Sentinel:PulseReporting</c>. The connection string is always named <c>Pulse</c> and
/// resolves the same way; it is deliberately not part of the tenant record.
/// </summary>
public sealed class PulseReportingSettingsResolver
{
    private const string ConnectionStringName = "Pulse";

    private readonly IConfiguration _config;

    public PulseReportingSettingsResolver(IConfiguration config)
    {
        _config = config;
    }

    /// <summary>
    /// Throws when reporting is enabled but cannot write correctly: a blank CustomerId would
    /// write readings Pulse never shows, so a misconfigured tenant must fail loudly.
    /// </summary>
    public ResolvedPulseReporting Resolve(string subdomain)
    {
        var tenant = _config.GetSection($"Tenants:{subdomain}:{PulseReportingSettings.SectionName}");
        var global = _config.GetSection(PulseReportingSettings.SectionName);
        var defaults = new PulseReportingSettings();

        var settings = new PulseReportingSettings
        {
            Enabled = Read(tenant, global, nameof(PulseReportingSettings.Enabled), defaults.Enabled),
            CustomerId = ReadString(tenant, global, nameof(PulseReportingSettings.CustomerId)) ?? defaults.CustomerId,
            CycleSeconds = Read(tenant, global, nameof(PulseReportingSettings.CycleSeconds), defaults.CycleSeconds),
            ReassertHours = Read(tenant, global, nameof(PulseReportingSettings.ReassertHours), defaults.ReassertHours),
        };

        var connectionString = ReadString(
            _config.GetSection($"Tenants:{subdomain}:ConnectionStrings"),
            _config.GetSection("ConnectionStrings"),
            ConnectionStringName);

        if (settings.Enabled)
            Validate(subdomain, settings, connectionString);

        return new ResolvedPulseReporting(subdomain, settings, connectionString);
    }

    private static void Validate(string subdomain, PulseReportingSettings settings, string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(settings.CustomerId))
            throw new InvalidOperationException(
                $"Pulse reporting is enabled for tenant '{subdomain}' but {PulseReportingSettings.SectionName}:CustomerId is blank.");
        if (connectionString is null)
            throw new InvalidOperationException(
                $"Pulse reporting is enabled for tenant '{subdomain}' but no '{ConnectionStringName}' connection string resolves.");
        if (settings.CycleSeconds <= 0 || settings.ReassertHours <= 0)
            throw new InvalidOperationException(
                $"Pulse reporting for tenant '{subdomain}' needs CycleSeconds and ReassertHours greater than zero.");
    }

    private static T Read<T>(IConfigurationSection tenant, IConfigurationSection global, string key, T fallback) =>
        tenant[key] is not null ? tenant.GetValue<T>(key)!
        : global[key] is not null ? global.GetValue<T>(key)!
        : fallback;

    // A blank string is treated as unset so a tenant's empty placeholder never masks the top level.
    private static string? ReadString(IConfigurationSection tenant, IConfigurationSection global, string key) =>
        !string.IsNullOrWhiteSpace(tenant[key]) ? tenant[key]
        : !string.IsNullOrWhiteSpace(global[key]) ? global[key]
        : null;
}
