param(
    [string]$OutputDir = (Join-Path $PSScriptRoot 'build'),
    [string]$GameAssetsDir = ''
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:winexe "/out:$(Join-Path $OutputDir 'ControlConsole.exe')" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Management.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:System.Configuration.dll (Join-Path $PSScriptRoot 'src\ControlConsole.cs')
if ($LASTEXITCODE -ne 0) { throw "C# 编译失败：$LASTEXITCODE" }
Copy-Item (Join-Path $PSScriptRoot 'ApplyUpdate.ps1'),(Join-Path $PSScriptRoot 'console-game-faithful.html'),(Join-Path $PSScriptRoot 'version.txt') -Destination $OutputDir -Force
if ($GameAssetsDir) {
    $assetNames = @('bg_stream_native.png','sidebar_noon.png','sidebar_evening.png','sidebar_night.png','button_start.png','start_pressed.png')
    $assetTarget = Join-Path $OutputDir 'game-assets'
    New-Item -ItemType Directory -Path $assetTarget -Force | Out-Null
    foreach ($name in $assetNames) {
        $source = Join-Path $GameAssetsDir $name
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "缺少本地原生界面素材：$source" }
        $destination = Join-Path $assetTarget $name
        if ([IO.Path]::GetFullPath($source) -ne [IO.Path]::GetFullPath($destination)) { Copy-Item -LiteralPath $source -Destination $destination -Force }
    }
}
