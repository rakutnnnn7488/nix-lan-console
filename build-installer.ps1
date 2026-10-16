param(
    [Parameter(Mandatory=$true)][string]$PackageDir,
    [Parameter(Mandatory=$true)][string]$OutputExe
)
$ErrorActionPreference = 'Stop'
$PackageDir = [IO.Path]::GetFullPath($PackageDir)
$OutputExe = [IO.Path]::GetFullPath($OutputExe)
$stub = Join-Path $PSScriptRoot 'src\SetupStub.cs'
if (-not (Test-Path -LiteralPath $stub -PathType Leaf)) { throw "Installer source not found: $stub" }
foreach ($required in @('ControlConsole.exe','Updater.exe','ApplyUpdate.ps1','version.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $PackageDir $required) -PathType Leaf)) { throw "Full package is missing: $required" }
}
$roleManifests = Get-ChildItem -LiteralPath (Join-Path $PackageDir 'game-assets\native-role') -Filter '*.json' -File -ErrorAction SilentlyContinue
if (-not $roleManifests) { throw 'Full package is missing the native character animation manifest.' }
$version = (Get-Content -LiteralPath (Join-Path $PackageDir 'version.txt') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Invalid four-part application version.' }
$stage = Join-Path ([IO.Path]::GetTempPath()) ('NixLanConsoleSetup-' + [guid]::NewGuid().ToString('N'))
$payload = Join-Path $stage 'payload.zip'
New-Item -ItemType Directory -Path $stage -Force | Out-Null
try {
    Compress-Archive -Path (Join-Path $PackageDir '*') -DestinationPath $payload -CompressionLevel Optimal
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    New-Item -ItemType Directory -Path (Split-Path -Parent $OutputExe) -Force | Out-Null
    & $csc /nologo /target:winexe "/out:$OutputExe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "/resource:$payload,NixLanConsoleSetup.PayloadZip" $stub
    if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed: $LASTEXITCODE" }
    $verify = Start-Process -FilePath $OutputExe -ArgumentList '--verify' -Wait -PassThru
    if ($verify.ExitCode -ne 0) { throw "Embedded full package verification failed: $($verify.ExitCode)" }
    Write-Output $OutputExe
}
finally { if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue } }
