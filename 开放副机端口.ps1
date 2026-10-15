$ErrorActionPreference = 'Stop'
$ruleName = 'NixLanConsole UDP 46701 (Private)'
$existing = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
if ($existing) { Remove-NetFirewallRule -DisplayName $ruleName }
New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol UDP -LocalPort 46701 -Action Allow -Profile Private | Out-Null
Write-Host '已为专用网络开放副机接收端口 UDP 46701。'

