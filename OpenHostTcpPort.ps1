$ErrorActionPreference = 'Stop'
$ruleName = 'NixLanConsole TCP 46702 (Private)'
$existing = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
if ($existing) { Remove-NetFirewallRule -DisplayName $ruleName }
New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP -LocalPort 46702 -Action Allow -Profile Private | Out-Null
Write-Host 'Nix LAN Console host TCP port 46702 is open on the Private profile.'
