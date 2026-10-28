using Microsoft.AspNetCore.Hosting;

namespace DXSuite.Shared.Storage;

/// <summary>
/// Resolves where runtime-uploaded branding images live and how they are served.
///
/// Branding images are mutable at runtime (uploaded via Settings → Branding) and live under
/// wwwroot/images/branding, served by the standard static-file middleware. The shipped defaults are
/// NOT committed there — they live in wwwroot/images/branding-defaults and are seeded into the
/// branding folder on startup. Keeping the branding folder empty at build time (and excluded from
/// the project's static web assets) keeps it out of the compiled asset manifest. That matters because
/// in a published build MapStaticAssets serves manifest assets with a build-time-frozen Content-Length;
/// overwriting such a file at runtime truncates the response and the browser renders a broken image.
/// Runtime files that are absent from the manifest are served live, so uploads resolve correctly.
/// </summary>
public static class BrandingStorage
{
    /// <summary>URL prefix the branding files are served under. Referenced by MainLayout/App markup.</summary>
    public const string RequestPath = "/images/branding";

    /// <summary>The known branding slots. Defaults ship under wwwroot/images/branding-defaults.</summary>
    public static readonly IReadOnlyList<string> KnownFiles =
        ["header.png", "login.png", "login-bg.png", "favicon.png"];

    /// <summary>Absolute path to the writable branding directory under the web root.</summary>
    public static string ResolveDirectory(IWebHostEnvironment env)
        => Path.Combine(env.WebRootPath, "images", "branding");

    /// <summary>
    /// Ensures the branding directory exists and is seeded with the shipped defaults.
    /// Only copies files that are missing, so customer uploads are never overwritten on restart.
    /// </summary>
    public static void EnsureSeeded(IWebHostEnvironment env)
    {
        var target = ResolveDirectory(env);
        Directory.CreateDirectory(target);

        var defaultsDir = Path.Combine(env.WebRootPath, "images", "branding-defaults");
        foreach (var file in KnownFiles)
        {
            var destination = Path.Combine(target, file);
            if (File.Exists(destination)) continue;

            var source = Path.Combine(defaultsDir, file);
            if (File.Exists(source))
                File.Copy(source, destination);
        }
    }
}
