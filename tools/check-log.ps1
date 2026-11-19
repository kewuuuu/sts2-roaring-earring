[CmdletBinding()]
param(
    [string]$LogPath = (Join-Path $env:APPDATA 'SlayTheSpire2\logs\godot.log')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $LogPath)) {
    Write-Error ("Log not found: " + $LogPath)
    exit 1
}

$matches = Select-String -LiteralPath $LogPath -Pattern '\[sts2-roaring-earring\]'
if (-not $matches) {
    Write-Host ("No sts2-roaring-earring lines in " + $LogPath)
    exit 1
}

$matches | ForEach-Object { $_.Line }
