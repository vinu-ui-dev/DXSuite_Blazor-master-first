<#
.SYNOPSIS
Brings an IIS-hosted DXSuite site back online by removing app_offline.htm.

.DESCRIPTION
Counterpart to Take-SiteOffline.ps1. Removing the file lets ANCM restart the app
on the next request; maintenance pages already open in browsers and wall shells
detect the recovery on their next probe and reload themselves.

.PARAMETER SiteRoot
Deployed site root (the folder containing web.config and the app binaries).

.EXAMPLE
.\Bring-SiteOnline.ps1 -SiteRoot 'C:\inetpub\dxsuite'
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$SiteRoot
)

$ErrorActionPreference = 'Stop'

$live = Join-Path $SiteRoot 'app_offline.htm'
if (Test-Path $live) {
    Remove-Item $live -Force
    Write-Host "Site online: the app restarts on the next request."
} else {
    Write-Host "Nothing to do: no app_offline.htm in '$SiteRoot'."
}
