using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DXSuite.Director.Services.LocationBundles;

/// <summary>
/// Deserialized manifest.json from the root of a location bundle zip. The raw JSON is
/// persisted on TblLocationBundleVersion; this type is the validation/consumption view.
/// </summary>
public class LocationBundleManifest
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Entry { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// Declared API usage (e.g. "assets:read", "schedule:write", "storage", "overrides").
    /// Documentation and import-time warnings only — the server enforces access from the
    /// caller's resolved permissions, never from these declarations.
    /// </summary>
    public List<string>? Capabilities { get; set; }

    public LocationBundleSettingsSchema? SettingsSchema { get; set; }

    /// <summary>
    /// Present when the bundle is usable as a panel. Declares the hotspot keys its markup
    /// uses, which the Panels config tab turns into a hotspot-to-display mapping grid.
    /// </summary>
    public LocationBundlePanelSpec? Panel { get; set; }

    public string? MinHostVersion { get; set; }
}

public class LocationBundlePanelSpec
{
    /// <summary>
    /// Stable identifiers for the clickable regions in the bundle's markup, e.g. "screen-1".
    /// Order is the order the config tab presents them and the order screens appear in the
    /// published document.
    /// </summary>
    public List<string> Hotspots { get; set; } = new();
}

public class LocationBundleSettingsSchema
{
    public List<LocationBundleSettingsField> Fields { get; set; } = new();
}

public class LocationBundleSettingsField
{
    public string Key { get; set; } = string.Empty;
    public string? Label { get; set; }

    /// <summary>"text", "number", "boolean", "select", or "json".</summary>
    public string Type { get; set; } = "text";

    public List<string>? Options { get; set; }
    public JsonElement? Default { get; set; }
    public bool Required { get; set; }
}

public static class LocationBundleManifestValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    // id and version become storage path segments and URL segments — charset is strict.
    private static readonly Regex IdPattern = new(@"^[a-z0-9][a-z0-9\-_]{1,63}$", RegexOptions.Compiled);
    private static readonly Regex VersionPattern = new(@"^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z][0-9A-Za-z\.\-]*)?$", RegexOptions.Compiled);

    private static readonly HashSet<string> KnownCapabilities = new(StringComparer.OrdinalIgnoreCase)
    {
        "assets:read", "assets:write", "schedule:read", "schedule:write", "storage", "overrides"
    };

    private static readonly HashSet<string> SettingsFieldTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text", "number", "boolean", "select", "json"
    };

    /// <summary>
    /// Parses and validates manifest JSON. Errors block the import; warnings are shown
    /// to the uploader but do not block.
    /// </summary>
    public static LocationBundleManifest? Validate(string manifestJson, List<string> errors, List<string> warnings)
    {
        LocationBundleManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<LocationBundleManifest>(manifestJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            errors.Add($"manifest.json is not valid JSON: {ex.Message}");
            return null;
        }

        if (manifest == null)
        {
            errors.Add("manifest.json is empty.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(manifest.Id) || !IdPattern.IsMatch(manifest.Id))
            errors.Add("Manifest \"id\" is required and must be 2-64 lowercase letters, digits, hyphens, or underscores, starting with a letter or digit.");

        if (string.IsNullOrWhiteSpace(manifest.Name))
            errors.Add("Manifest \"name\" is required.");

        if (string.IsNullOrWhiteSpace(manifest.Version) || !VersionPattern.IsMatch(manifest.Version) || manifest.Version.Length > 32)
            errors.Add("Manifest \"version\" is required and must be semver (e.g. \"1.2.0\" or \"1.2.0-dev.3\"), max 32 characters.");

        if (string.IsNullOrWhiteSpace(manifest.Entry))
            errors.Add("Manifest \"entry\" is required (relative path of the entry HTML file, e.g. \"index.html\").");
        else if (manifest.Entry.StartsWith('/') || manifest.Entry.Contains(".."))
            errors.Add("Manifest \"entry\" must be a relative path with no \"..\" segments.");

        foreach (var cap in manifest.Capabilities ?? Enumerable.Empty<string>())
        {
            if (!KnownCapabilities.Contains(cap))
                warnings.Add($"Unknown capability \"{cap}\" — this host will ignore it.");
        }

        if (manifest.SettingsSchema != null)
        {
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in manifest.SettingsSchema.Fields)
            {
                if (string.IsNullOrWhiteSpace(field.Key))
                    errors.Add("settingsSchema contains a field with no \"key\".");
                else if (!seenKeys.Add(field.Key))
                    errors.Add($"settingsSchema has duplicate field key \"{field.Key}\".");

                if (!SettingsFieldTypes.Contains(field.Type))
                    errors.Add($"settingsSchema field \"{field.Key}\": unsupported type \"{field.Type}\" (allowed: text, number, boolean, select, json).");

                if (string.Equals(field.Type, "select", StringComparison.OrdinalIgnoreCase) && (field.Options == null || field.Options.Count == 0))
                    errors.Add($"settingsSchema field \"{field.Key}\": type \"select\" requires non-empty \"options\".");
            }
        }

        if (manifest.Panel != null)
        {
            if (manifest.Panel.Hotspots.Count == 0)
                errors.Add("Manifest \"panel\" is present but declares no hotspots.");

            var seenHotspots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var hotspot in manifest.Panel.Hotspots)
            {
                // Hotspot keys are persisted in the panel's mapping and echoed in the published
                // document, so they stay to the same strict charset as ids.
                if (string.IsNullOrWhiteSpace(hotspot) || !IdPattern.IsMatch(hotspot))
                    errors.Add($"Manifest panel hotspot \"{hotspot}\" must be 2-64 lowercase letters, digits, hyphens, or underscores.");
                else if (!seenHotspots.Add(hotspot))
                    errors.Add($"Manifest panel declares duplicate hotspot \"{hotspot}\".");
            }
        }

        return manifest;
    }
}
