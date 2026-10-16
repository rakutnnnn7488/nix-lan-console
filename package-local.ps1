param(
    [Parameter(Mandatory=$true)][string]$GameAssetsDir,
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\ControlConsole-local-package')
)
$ErrorActionPreference = 'Stop'
$version = (Get-Content (Join-Path $PSScriptRoot 'version.txt') -Raw).Trim()
$packageName = "ControlConsole-Windows-$version"
$packageDir = Join-Path $OutputRoot $packageName
$zip = Join-Path $OutputRoot ($packageName + '-full.zip')
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
if (Test-Path -LiteralPath $packageDir) { throw "Package directory exists: $packageDir" }
if (Test-Path -LiteralPath $zip) { throw "Package ZIP exists: $zip" }
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDir $packageDir -GameAssetsDir $GameAssetsDir
$roleDir = Join-Path $packageDir 'game-assets\native-role'
if (-not (Get-ChildItem -LiteralPath $roleDir -Filter '*.json' -File -ErrorAction SilentlyContinue)) { throw 'Role manifest is missing.' }
Set-Content -LiteralPath (Join-Path $packageDir 'update-source.txt') -Encoding UTF8 -Value 'rakutnnnn7488/nix-lan-console'
Compress-Archive -LiteralPath $packageDir -DestinationPath $zip -CompressionLevel Optimal
Write-Output $packageDir
Write-Output $zip
