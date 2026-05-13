# Run Flutter app on connected device with API URL using this machine's LAN IP (same as frontend when opened from another device)
# Prefer 192.168.x.x (typical WiFi) over 10.x.x.x (may be VPN/WSL)
$addrs = Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.InterfaceAlias -notmatch 'Loopback' -and $_.IPAddress -match '^\d+\.\d+\.\d+\.\d+$' }
$ip = ($addrs | Where-Object { $_.IPAddress -match '^192\.168\.' } | Select-Object -First 1).IPAddress
if (-not $ip) { $ip = ($addrs | Select-Object -First 1).IPAddress }
if (-not $ip) { $ip = '192.168.10.161' }
$apiUrl = "http://${ip}:5020/api"
Write-Host "Using API: $apiUrl"
& "C:\flutter\bin\flutter.bat" run --dart-define=API_URL=$apiUrl @args
