#requires -Version 5.1
<#
.SYNOPSIS
    Builds the DXSuite help site from docs-src into src/DXSuite.Host/wwwroot/docs.

.DESCRIPTION
    Runs MkDocs (Material theme) to compile the markdown under docs-src into static
    HTML served by the Host at /docs. Output goes straight into wwwroot/docs via the
    site_dir setting in mkdocs.yml, so no manual copy is needed after building.

    Run from anywhere; paths are resolved relative to this script.

.PARAMETER Serve
    Start the live-reload preview server (http://127.0.0.1:8000) instead of a one-shot
    build. Handy while authoring. Does NOT write to wwwroot/docs.

.PARAMETER SkipInstall
    Skip the pip dependency install step (use when the pinned packages are already present).

.EXAMPLE
    ./build-docs.ps1
    One-shot build into wwwroot/docs.

.EXAMPLE
    ./build-docs.ps1 -Serve
    Author with live reload.
#>
param(
    [switch]$Serve,
    [switch]$SkipInstall
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot   # docs-src's parent = repo root (mkdocs.yml lives here)

# Resolve a working Python launcher. Prefer 'python' (on PATH); fall back to the 'py' launcher.
# We probe each candidate because a 'py' launcher can be present but point at an uninstalled
# version, which fails at run time rather than resolution time.
$pythonCmd = $null
foreach ($candidate in @('python', 'py')) {
    if (Get-Command $candidate -ErrorAction SilentlyContinue) {
        & $candidate --version *> $null
        if ($LASTEXITCODE -eq 0) { $pythonCmd = $candidate; break }
    }
}
if (-not $pythonCmd) { throw "No working Python interpreter found on PATH (tried 'python' and 'py')." }

if (-not $SkipInstall) {
    Write-Host "Installing pinned MkDocs dependencies..." -ForegroundColor Cyan
    & $pythonCmd -m pip install -r (Join-Path $PSScriptRoot 'requirements.txt')
    if ($LASTEXITCODE -ne 0) { throw "pip install failed (exit $LASTEXITCODE)" }
}

Push-Location $repoRoot
try {
    if ($Serve) {
        Write-Host "Serving docs at http://127.0.0.1:8000 (Ctrl+C to stop)..." -ForegroundColor Green
        & $pythonCmd -m mkdocs serve
    }
    else {
        Write-Host "Building docs into src/DXSuite.Host/wwwroot/docs..." -ForegroundColor Green
        & $pythonCmd -m mkdocs build
        if ($LASTEXITCODE -ne 0) { throw "mkdocs build failed (exit $LASTEXITCODE)" }
        Write-Host "Done. Commit the regenerated wwwroot/docs alongside your markdown changes." -ForegroundColor Green
    }
}
finally {
    Pop-Location
}
