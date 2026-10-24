param(
    [string]$OutputDir = (Join-Path $PSScriptRoot 'build'),
    [string]$GameAssetsDir = ''
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$version = (Get-Content (Join-Path $PSScriptRoot 'version.txt') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw "version.txt must contain a four-part version: $version" }
$appDir = Join-Path $OutputDir (Join-Path 'versions' $version)
New-Item -ItemType Directory -Path $appDir -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $appDir '.complete'), $version, (New-Object System.Text.UTF8Encoding($false)))
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

# Versioned application (never replaced while running).
& $csc /nologo /target:winexe "/out:$(Join-Path $appDir 'ControlConsole.exe')" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Management.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:System.Configuration.dll (Join-Path $PSScriptRoot 'src\ControlConsole.cs') (Join-Path $PSScriptRoot 'src\NativeConsoleWindow.cs') (Join-Path $PSScriptRoot 'src\RoleAnimationCatalog.cs') (Join-Path $PSScriptRoot 'src\AssetBundleSync.cs') (Join-Path $PSScriptRoot 'src\NativeDialogs.cs') (Join-Path $PSScriptRoot 'src\FoliaWindowHost.cs') (Join-Path $PSScriptRoot 'src\FoliaLanRelay.cs') (Join-Path $PSScriptRoot 'src\FoliaPixelIcon.cs')
if ($LASTEXITCODE -ne 0) { throw "C# compilation failed:$LASTEXITCODE" }

# Stable launcher at the installation root.
& $csc /nologo /target:winexe "/out:$(Join-Path $OutputDir 'ControlConsole.exe')" /reference:System.Windows.Forms.dll (Join-Path $PSScriptRoot 'src\Launcher.cs')
if ($LASTEXITCODE -ne 0) { throw "Launcher compilation failed:$LASTEXITCODE" }
Copy-Item (Join-Path $OutputDir 'ControlConsole.exe') (Join-Path $appDir 'Launcher.exe') -Force

Copy-Item (Join-Path $PSScriptRoot 'version.txt'),(Join-Path $PSScriptRoot 'OpenHostTcpPort.ps1') -Destination $OutputDir -Force
[IO.File]::WriteAllText((Join-Path $OutputDir 'current.txt'), $version, (New-Object System.Text.UTF8Encoding($false)))

if ($GameAssetsDir) {
    $assetNames = @('bg_stream_native.png','haisin.png','sidebar_noon.png','sidebar_evening.png','sidebar_night.png','window_active.png','window_inactive.png','button_minimize.png','button_maximize.png','button_close.png','button_start.png','start_pressed.png','jine_bg.png','icon_jine_ame.png','tail.png','zpix.ttf','icon_desktop_youtube.png','icon_desktop_asobu.png','icon_desktop_neru.png','icon_desktop_okusuri.png','icon_desktop_internet.png','icon_desktop_odekake.png','icon_status_follower.png','icon_status_stress.png','icon_status_love.png','icon_status_yami.png')
    if (Test-Path -LiteralPath (Join-Path $GameAssetsDir 'desktop_wallpaper.png') -PathType Leaf) { $assetNames += 'desktop_wallpaper.png' }
    if (Test-Path -LiteralPath (Join-Path $GameAssetsDir 'desktop_full_original.png') -PathType Leaf) { $assetNames += 'desktop_full_original.png' }
    if (Test-Path -LiteralPath (Join-Path $GameAssetsDir 'footer_original.png') -PathType Leaf) { $assetNames += 'footer_original.png' }
    foreach ($font in @('PixelMplus10-Regular.ttf','DinkieBitmap-7px.ttf','DinkieBitmap-9px.ttf')) {
        if (Test-Path -LiteralPath (Join-Path $GameAssetsDir $font) -PathType Leaf) { $assetNames += $font }
    }
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
