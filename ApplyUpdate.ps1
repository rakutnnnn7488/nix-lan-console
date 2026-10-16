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
$readyFile = Join-Path $env:LOCALAPPDATA 'NixLanConsole\startup-ready.txt'
$failedFile = Join-Path $env:LOCALAPPDATA 'NixLanConsole\failed-update.txt'
$copiedBackup = $false
$newVersion = ''
function Write-UpdateLog([string]$message) {
    Add-Content -LiteralPath $log -Encoding UTF8 -Value ((Get-Date -Format s) + ' ' + $message)
}
try {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $log) | Out-Null
    Write-UpdateLog "开始安装更新：$archivePath"
    $parentProcess = Get-Process -Id $ParentPid -ErrorAction SilentlyContinue
    if ($parentProcess) { Wait-Process -Id $ParentPid -Timeout 90 -ErrorAction Stop }
    if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) { throw '更新压缩包不存在。' }
    if (-not (Test-Path -LiteralPath $target -PathType Container)) { throw '安装目录不存在。' }
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    Expand-Archive -LiteralPath $archivePath -DestinationPath $stage -Force
    $newExe = Join-Path $stage 'ControlConsole.exe'
    if (-not (Test-Path -LiteralPath $newExe -PathType Leaf)) { throw '更新包内没有 ControlConsole.exe。' }
    if (-not (Test-Path -LiteralPath (Join-Path $stage 'ApplyUpdate.ps1') -PathType Leaf)) { throw '更新包内没有 ApplyUpdate.ps1。' }
    if (-not (Test-Path -LiteralPath (Join-Path $stage 'version.txt') -PathType Leaf)) { throw '更新包内没有 version.txt。' }
    $newVersion = (Get-Content -LiteralPath (Join-Path $stage 'version.txt') -Raw).Trim()
    if ($newVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw '更新包版本号无效。' }
    Write-UpdateLog '更新包验证完成，正在备份旧版本。'
    Copy-Item -LiteralPath $target -Destination $backup -Recurse -Force
    $copiedBackup = $true
    Get-ChildItem -LiteralPath $stage -Force | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $target -Recurse -Force }
    $exe = Join-Path $target 'ControlConsole.exe'
    Write-UpdateLog '新版文件已复制，正在重新启动。'
    if (Test-Path -LiteralPath $readyFile) { Remove-Item -LiteralPath $readyFile -Force }
    $p = Start-Process -FilePath $exe -ArgumentList $RestartRole -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    $ready = $false
    while ([DateTime]::UtcNow -lt $deadline) {
        $p.Refresh()
        if ($p.HasExited) { throw '新版程序启动后立即退出。已回滚到旧版。' }
        if (Test-Path -LiteralPath $readyFile) {
            $parts = ((Get-Content -LiteralPath $readyFile -Raw).Trim() -split '\|')
            if ($parts.Count -ge 3 -and $parts[0] -eq [string]$p.Id -and $parts[1] -eq $newVersion -and $parts[2] -eq $RestartRole) { $ready = $true; break }
        }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) { throw '新版程序没有报告启动就绪，已回滚到旧版。' }
    if (Test-Path -LiteralPath $failedFile) { Remove-Item -LiteralPath $failedFile -Force -ErrorAction SilentlyContinue }
    Write-UpdateLog ('自动更新成功。备份：' + $backup)
}
catch {
    Write-UpdateLog ('自动更新失败：' + $_.Exception.Message)
    if ($newVersion) { Set-Content -LiteralPath $failedFile -Encoding UTF8 -Value $newVersion }
    if ($copiedBackup -and (Test-Path -LiteralPath $backup)) {
        if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
        Get-ChildItem -LiteralPath $backup -Force | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $target -Recurse -Force }
        try { Start-Process -FilePath (Join-Path $target 'ControlConsole.exe') -ArgumentList $RestartRole | Out-Null; Write-UpdateLog '已恢复并重启旧版本。' } catch { Write-UpdateLog ('旧版本重启失败：' + $_.Exception.Message) }
    }
}
finally {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
}
