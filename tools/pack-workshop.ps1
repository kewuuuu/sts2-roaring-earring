# Builds the mod and stages the Workshop content folder: dist/workshop/
# Staging only: publishing is a separate, opt-in step (tools/WorkshopUploader --create / --update).
param(
    [string]$Configuration = 'Release',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $root 'sts2-roaring-earring.csproj'
$src = Join-Path $root 'dist\sts2-roaring-earring'
$workshop = Join-Path $root 'dist\workshop'
$modId = 'sts2-roaring-earring'
$dest = Join-Path $workshop $modId

if (-not $NoBuild) {
    dotnet build $csproj -c $Configuration -v minimal
    if ($LASTEXITCODE -ne 0) {
        throw 'build failed'
    }
}

foreach ($name in @("$modId.dll", "$modId.json")) {
    $path = Join-Path $src $name
    if (-not (Test-Path -LiteralPath $path)) {
        throw "missing build output: $path"
    }
}

# Replace the staged folder so a stale dll can never ride along.
if (Test-Path -LiteralPath $dest) {
    Remove-Item -LiteralPath $dest -Recurse -Force
}
New-Item -ItemType Directory -Path $dest -Force | Out-Null
New-Item -ItemType Directory -Path $workshop -Force | Out-Null

foreach ($file in Get-ChildItem -LiteralPath $src -Recurse -File) {
    $relative = $file.FullName.Substring($src.Length).TrimStart([char]'\')
    $target = Join-Path $dest $relative
    $targetDir = Split-Path -Parent $target
    if (-not (Test-Path -LiteralPath $targetDir)) {
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    }

    Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    $length = (Get-Item -LiteralPath $target).Length
    if ($length -ne $file.Length) {
        throw ("copy mismatch: " + $target + " is " + $length + " bytes, source is " + $file.Length)
    }

    Write-Host ("  staged " + $relative + "  " + $length + " bytes")
}

# The manifest is UTF-8 without BOM; Windows PowerShell 5.1 decodes that with the ANSI codepage
# unless the encoding is passed explicitly, which mangles the Chinese name and breaks the JSON.
$manifestPath = Join-Path $dest "$modId.json"
$manifestText = [System.IO.File]::ReadAllText($manifestPath, [System.Text.Encoding]::UTF8)
$manifest = $manifestText | ConvertFrom-Json
if ($manifest.id -ne $modId) {
    throw ("staged manifest id '" + $manifest.id + "' is not '$modId': " + $manifestPath)
}
Write-Host ("  manifest id=" + $manifest.id + " version=" + $manifest.version)

# Icon and preview sit beside the mod folder: the Workshop page needs both, and the uploader
# can point --preview at either copy.
foreach ($name in @('roaring_earring_icon.png', 'roaring_earring_big.png')) {
    $from = Join-Path $root ('assets\icons\' + $name)
    if (-not (Test-Path -LiteralPath $from)) {
        throw "missing art: $from"
    }

    $to = Join-Path $workshop $name
    Copy-Item -LiteralPath $from -Destination $to -Force
    $length = (Get-Item -LiteralPath $to).Length
    if ($length -ne (Get-Item -LiteralPath $from).Length) {
        throw ("copy mismatch: " + $to)
    }

    Write-Host ("  staged " + $name + "  " + $length + " bytes")
}

$all = @(Get-ChildItem -LiteralPath $workshop -Recurse -File)
$total = 0
foreach ($file in $all) {
    $total += $file.Length
}
Write-Host ("content ready: " + $all.Count + " files, " + $total + " bytes")
