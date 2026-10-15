param(
    [Parameter(Mandatory=$true)][string]$TargetDir,
    [Parameter(Mandatory=$true)][string]$Archive,
    [Parameter(Mandatory=$true)][int]$ParentPid,
    [Parameter(Mandatory=$true)][string]$RestartRole
)
$ErrorActionPreference = 'Stop'
$target = [IO.Path]::GetFullPath($TargetDir).TrimEnd('\')
$archivePath = [IO.Path]::GetFullPath($Archive)
$updateRoot = Join-Path $env:LOCALAPPDATA 'NixLanConsole\updates'
$stage = Join-Path $updateRoot ('stage-' + [guid]::NewGuid().ToString('N'))
$backup = $target + '.backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
$log = Join-Path $env:LOCALAPPDATA 'NixLanConsole\update.log'
$copiedBackup = $false
try {
    try { Wait-Process -Id $ParentPid -Timeout 90 -ErrorAction Stop } catch { }
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    Expand-Archive -LiteralPath $archivePath -DestinationPath $stage -Force
    $newExe = Join-Path $stage 'ControlConsole.exe'
    if (-not (Test-Path -LiteralPath $newExe -PathType Leaf)) { throw '更新包内没有 ControlConsole.exe。' }
    Copy-Item -LiteralPath $target -Destination $backup -Recurse -Force
    $copiedBackup = $true
    Get-ChildItem -LiteralPath $stage -Force | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $target -Recurse -Force }
    $exe = Join-Path $target 'ControlConsole.exe'
    $p = Start-Process -FilePath $exe -ArgumentList $RestartRole -PassThru
    Start-Sleep -Seconds 6
    $p.Refresh()
    if ($p.HasExited) { throw '新版程序启动后立即退出。已回滚到旧版。' }
    Add-Content -LiteralPath $log -Encoding UTF8 -Value ((Get-Date -Format s) + ' 自动更新成功。备份：' + $backup)
}
catch {
    Add-Content -LiteralPath $log -Encoding UTF8 -Value ((Get-Date -Format s) + ' 自动更新失败：' + $_.Exception.Message)
    if ($copiedBackup -and (Test-Path -LiteralPath $backup)) {
        Get-ChildItem -LiteralPath $backup -Force | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $target -Recurse -Force }
        try { Start-Process -FilePath (Join-Path $target 'ControlConsole.exe') -ArgumentList $RestartRole } catch { }
    }
}
finally {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
}

