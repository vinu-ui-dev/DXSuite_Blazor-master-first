<#
.SYNOPSIS
Moves existing global tenant media into a tenant-scoped media root (DXME-48).

.DESCRIPTION
DXSuite now stores tenant-owned web content under a per-tenant root
(wwwroot\tenants\{subdomain} by default) instead of the shared wwwroot folders.
Run this once per environment to move the existing (single-tenant era) media into
the root of the tenant that owns it. Stop the DXSuite app pool/site first.

Tenant media isolation is OFF by default (single-tenant installs need no action, ever).
Enabling a multi-tenant environment is a two-step cutover:
  1. Run this script once per tenant that owns existing media (or, with no file moves,
     point the legacy tenant at the global web root instead:
       UPDATE tblTenant SET FilePaths_MediaRoot = 'wwwroot' WHERE Subdomain = '<legacy-tenant>';
     in DXSuite_TenantRegistry - choose one approach per tenant, not both).
  2. Set DXSuite:TenantMedia:Enabled=true in that environment's appsettings.

After all tenants are migrated, set DXSuite:TenantMedia:GlobalFallback=false to make
serving strictly tenant-scoped (the fallback exists only to bridge partial migrations).

CONSTRAINT - Pixera video walls: the Pixera pipeline (PixeraService, Pixera content sync,
background zone derivation, wall allocation, BaseUNC share) reads from the GLOBAL web root.
The tenant that owns Pixera walls must use the FilePaths_MediaRoot='wwwroot' pointer, NOT a
moved tenant root. Do not run this script for that tenant.

CONSTRAINT - one owner per file: this moves the WHOLE global folder to a single tenant. The
global layout predates isolation and carries no tenant marker, so if two tenants both have
media sitting in the shared wwwroot there is nothing here to split it by - the first tenant
you run takes everything and the second is left with an empty root. Run with -WhatIfMode
first, and where media is genuinely shared, split it by hand (location image files are named
{LocationId}_{unixSeconds}.ext, so each tenant's Director DB tblLocation tells you which files
are its own) before running this for either tenant.

.EXAMPLE
.\migrate-media-to-tenant-root.ps1 -WebRoot 'C:\inetpub\DXSuite\wwwroot' -TenantSubdomain radiant -WhatIfMode
.\migrate-media-to-tenant-root.ps1 -WebRoot 'C:\inetpub\DXSuite\wwwroot' -TenantSubdomain radiant
#>
param(
    [Parameter(Mandatory = $true)] [string]$WebRoot,
    [Parameter(Mandatory = $true)] [string]$TenantSubdomain,
    [switch]$WhatIfMode
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $WebRoot)) { throw "Web root not found: $WebRoot" }

# Tenant-owned content folders, relative to the web root. Mirrors
# TenantMediaPaths.TenantContentPrefixes in DXSuite.Shared.
$prefixes = @(
    'MediaAssets',
    'TempFiles',
    'images\Location Images',
    'images\Visual Backgrounds',
    'models'
)

$tenantRoot = Join-Path $WebRoot "tenants\$TenantSubdomain"

foreach ($prefix in $prefixes) {
    $source = Join-Path $WebRoot $prefix
    if (-not (Test-Path $source)) {
        Write-Host "SKIP  $prefix (not present)"
        continue
    }

    # robocopy /MOVE empties the source but leaves the named folder behind, so a second run for
    # another tenant finds an existing-but-empty folder and reports a successful move of nothing.
    # Call that out rather than letting the run look like it migrated the tenant.
    $sourceCount = (Get-ChildItem $source -Recurse -File | Measure-Object).Count
    if ($sourceCount -eq 0) {
        Write-Host "SKIP  $prefix (no files to move - already migrated to another tenant?)"
        continue
    }

    $dest = Join-Path $tenantRoot $prefix
    if ((Test-Path $dest) -and (Get-ChildItem $dest -Recurse -File | Measure-Object).Count -gt 0) {
        Write-Warning "$prefix already has files under '$TenantSubdomain'; merging global content into it. Confirm the global folder is not another tenant's media."
    }
    if ($WhatIfMode) {
        Write-Host "WOULD MOVE  $source -> $dest  ($sourceCount files)"
        continue
    }

    Write-Host "MOVE  $source -> $dest"
    # /MOVE relocates files and removes them from the source; /E includes empty subfolders
    # (playlist folders may legitimately be empty). Exit codes 0-7 are success for robocopy.
    robocopy $source $dest /MOVE /E /NFL /NDL /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed for '$prefix' (exit $LASTEXITCODE)" }
}

if (-not $WhatIfMode) {
    Write-Host ""
    Write-Host "Done. Remaining steps:"
    Write-Host " 1. Leave tblTenant.FilePaths_MediaRoot NULL for '$TenantSubdomain' (default root now holds its media)."
    Write-Host " 2. Set DXSuite:TenantMedia:GlobalFallback=false once all tenants are migrated."
    Write-Host " 3. Restart the DXSuite site."
}
