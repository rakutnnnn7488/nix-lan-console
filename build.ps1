param(
    [string]$OutputDir = (Join-Path $PSScriptRoot 'build'),
    [string]$GameAssetsDir = ''
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:winexe "/out:$(Join-Path $OutputDir 'ControlConsole.exe')" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Management.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:System.Configuration.dll (Join-Path $PSScriptRoot 'src\ControlConsole.cs') (Join-Path $PSScriptRoot 'src\NativeConsoleWindow.cs') (Join-Path $PSScriptRoot 'src\NativeDialogs.cs')
if ($LASTEXITCODE -ne 0) { throw "C# compilation failed:$LASTEXITCODE" }
& $csc /nologo /target:winexe "/out:$(Join-Path $OutputDir 'Updater.exe')" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll (Join-Path $PSScriptRoot 'src\Updater.cs')
if ($LASTEXITCODE -ne 0) { throw "Updater compilation failed:$LASTEXITCODE" }
Copy-Item (Join-Path $PSScriptRoot 'ApplyUpdate.ps1'),(Join-Path $PSScriptRoot 'version.txt'),(Join-Path $PSScriptRoot 'OpenHostTcpPort.ps1') -Destination $OutputDir -Force
if ($GameAssetsDir) {
    $assetNames = @('bg_stream_native.png','sidebar_noon.png','sidebar_evening.png','sidebar_night.png','window_active.png','window_inactive.png','button_start.png','start_pressed.png','jine_bg.png','zpix.ttf','icon_desktop_youtube.png','icon_desktop_asobu.png','icon_desktop_neru.png','icon_desktop_okusuri.png','icon_desktop_internet.png','icon_desktop_odekake.png','icon_status_follower.png','icon_status_stress.png','icon_status_love.png','icon_status_yami.png')
    if (Test-Path -LiteralPath (Join-Path $GameAssetsDir 'desktop_wallpaper.png') -PathType Leaf) { $assetNames += 'desktop_wallpaper.png' }
    $assetTarget = Join-Path $OutputDir 'game-assets'
    New-Item -ItemType Directory -Path $assetTarget -Force | Out-Null
    foreach ($name in $assetNames) {
        $source = Join-Path $GameAssetsDir $name
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing local game UI asset:$source" }
        $destination = Join-Path $assetTarget $name
        if ([IO.Path]::GetFullPath($source) -ne [IO.Path]::GetFullPath($destination)) { Copy-Item -LiteralPath $source -Destination $destination -Force }
    }
    $roleSource = Join-Path $GameAssetsDir 'native-role'
    if (Test-Path -LiteralPath $roleSource -PathType Container) {
        Copy-Item -LiteralPath $roleSource -Destination $assetTarget -Recurse -Force
    }
}
