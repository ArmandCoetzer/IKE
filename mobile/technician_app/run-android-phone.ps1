$ErrorActionPreference = "Stop"

function Get-LanIPv4 {
  # Primary: only adapters that are Up and have IPv4 configured.
  $ips = @()
  try {
    $ips = Get-NetIPConfiguration -ErrorAction Stop |
      Where-Object { $_.NetAdapter.Status -eq 'Up' -and $_.IPv4Address } |
      Sort-Object { if ($_.InterfaceAlias -match 'Wi-Fi|WLAN') { 0 } else { 1 } } |
      ForEach-Object { $_.IPv4Address.IPAddress } |
      Where-Object {
        $_ -and
        $_ -ne '127.0.0.1' -and
        $_ -notlike '169.254.*' -and
        ($_ -like '192.168.*' -or $_ -like '10.*' -or $_ -like '172.16.*' -or $_ -like '172.17.*' -or $_ -like '172.18.*' -or $_ -like '172.19.*' -or $_ -like '172.2?.*' -or $_ -like '172.30.*' -or $_ -like '172.31.*')
      }
  } catch {
    # Fallback for systems where NetTCPIP cmdlets are unavailable/restricted.
    $ips = ipconfig |
      Select-String -Pattern 'IPv4.*?:\s*(\d{1,3}(?:\.\d{1,3}){3})' |
      ForEach-Object { if ($_.Matches.Count -gt 0) { $_.Matches[0].Groups[1].Value } } |
      Where-Object { $_ -and $_ -match '^\d{1,3}(\.\d{1,3}){3}$' -and $_ -ne '127.0.0.1' -and $_ -notlike '169.254.*' }
  }

  $preferred = $ips | Where-Object { $_ -like "192.168.*" } | Select-Object -First 1
  if ($preferred) { return $preferred }

  $preferred = $ips | Where-Object { $_ -like "10.*" } | Select-Object -First 1
  if ($preferred) { return $preferred }

  return ($ips | Select-Object -First 1)
}

$ip = Get-LanIPv4
if (-not $ip) {
  throw "Could not detect a LAN IPv4 address. Connect to Wi-Fi and try again."
}

$api = "http://${ip}:5020/api"
Write-Host "Using API_URL=$api"

$flutterCmd = Get-Command flutter -ErrorAction SilentlyContinue
if ($flutterCmd) {
  flutter pub get
  flutter run --dart-define=API_URL=$api
} else {
  & 'C:\flutter\bin\flutter.bat' pub get
  & 'C:\flutter\bin\flutter.bat' run --dart-define=API_URL=$api
}

