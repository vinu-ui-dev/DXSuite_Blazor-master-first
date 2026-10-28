<#
.SYNOPSIS
Takes an IIS-hosted DXSuite site offline by placing app_offline.htm in its root.

.DESCRIPTION
While app_offline.htm exists in the site root, ANCM stops the app and serves that
file for every request with HTTP 503. Prefer this over stopping the app pool:
browsers and video-wall shells that reload during the outage get the auto-retrying
maintenance page instead of a raw IIS 503 they never recover from.

Run Bring-SiteOnline.ps1 to remove the file and restart the app.

.PARAMETER SiteRoot
Deployed site root (the folder containing web.config and the app binaries).

.EXAMPLE
.\Take-SiteOffline.ps1 -SiteRoot 'C:\inetpub\dxsuite'
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$SiteRoot
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path (Join-Path $SiteRoot 'web.config'))) {
    throw "No web.config in '$SiteRoot' - this does not look like a deployed site root."
}

# Published template beside the binaries; fall back to the repo copy next to this
# script for deployments that predate the publish wiring.
$template = Join-Path $SiteRoot 'app_offline.template.htm'
if (-not (Test-Path $template)) {
    $template = Join-Path $PSScriptRoot 'app_offline.template.htm'
}
if (-not (Test-Path $template)) {
    throw 'app_offline.template.htm not found in the site root or next to this script.'
}

Copy-Item $template (Join-Path $SiteRoot 'app_offline.htm') -Force
Write-Host "Site offline: '$SiteRoot' is now serving the maintenance page."
