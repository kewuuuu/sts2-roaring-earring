[CmdletBinding()]
param(
    [string]$ModsDir = 'D:\software\steam\steamapps\common\Slay the Spire 2\mods',
    [string]$ModId = 'sts2-roaring-earring',
    [switch]$Yes
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$distDir = Join-Path $projectRoot ('dist\' + $ModId)
$dll = Join-Path $distDir ($ModId + '.dll')
$manifest = Join-Path $distDir ($ModId + '.json')

if (-not (Test-Path -LiteralPath $dll) -or -not (Test-Path -LiteralPath $manifest)) {
    throw "Nothing staged in $distDir - run 'dotnet build -c Release' first."
}

$running = Get-Process -Name 'SlayTheSpire2' -ErrorAction SilentlyContinue
if ($running) {
    throw 'Slay the Spire 2 is running; close it before installing the mod (the dll is locked while it loads).'
}

$targetDir = Join-Path $ModsDir $ModId
Write-Host "Install $ModId"
Write-Host ("  from: " + $distDir)
Write-Host ("  to:   " + $targetDir)
foreach ($file in @($dll, $manifest)) {
    $info = Get-Item -LiteralPath $file
    Write-Host ("  file: " + $info.Name + ' (' + $info.Length + ' bytes)')
}

if (-not $Yes) {
    $answer = Read-Host 'Copy these files into the mods directory? [y/N]'
    if ($answer -notmatch '^(y|yes)$') {
        Write-Host 'Cancelled.'
        return
    }
}

New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
foreach ($file in @($dll, $manifest)) {
    $destination = Join-Path $targetDir (Split-Path -Leaf $file)
    Copy-Item -LiteralPath $file -Destination $destination -Force
    $sourceBytes = (Get-Item -LiteralPath $file).Length
    $copiedBytes = (Get-Item -LiteralPath $destination).Length
    if ($sourceBytes -ne $copiedBytes) {
        throw ("Copy of " + $destination + " is " + $copiedBytes + ' bytes, expected ' + $sourceBytes + '.')
    }
    Write-Host ("  copied " + $destination + ' (' + $copiedBytes + ' bytes)')
}

Write-Host 'Done. Start the game and check the log with tools\check-log.ps1.'
