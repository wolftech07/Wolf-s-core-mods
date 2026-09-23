param([switch]$UseDownloadedSnapshot)
$ErrorActionPreference = 'Stop'
$original = Join-Path $PSScriptRoot 'downloads\bootstrap-nodes-original.json'
if (!$UseDownloadedSnapshot) {
    New-Item -ItemType Directory -Path (Split-Path $original -Parent) -Force | Out-Null
    Invoke-WebRequest -UseBasicParsing -Uri 'https://nodes.tox.chat/json' -OutFile $original -TimeoutSec 30
}
$snapshot = Get-Content -LiteralPath $original -Raw | ConvertFrom-Json
function Test-PublicHost([string]$HostName) {
    if (!$HostName -or $HostName.Length -gt 253) { return $false }
    $address = $null
    if ([Net.IPAddress]::TryParse($HostName, [ref]$address)) {
        if ([Net.IPAddress]::IsLoopback($address)) { return $false }
        if ($address.AddressFamily -eq [Net.Sockets.AddressFamily]::InterNetworkV6) {
            return !$address.IsIPv6LinkLocal -and !$address.IsIPv6Multicast -and !$address.IsIPv6SiteLocal -and ($address.GetAddressBytes()[0] -band 254) -ne 252 -and $address.ToString() -ne '::'
        }
        $bytes = $address.GetAddressBytes()
        return $bytes[0] -notin @(0,10,127) -and $bytes[0] -lt 224 -and !($bytes[0] -eq 169 -and $bytes[1] -eq 254) -and !($bytes[0] -eq 172 -and $bytes[1] -ge 16 -and $bytes[1] -le 31) -and !($bytes[0] -eq 192 -and $bytes[1] -eq 168) -and !($bytes[0] -eq 100 -and $bytes[1] -ge 64 -and $bytes[1] -le 127)
    }
    return $HostName -match '^(?=.{1,253}$)[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?)+$' -and $HostName -notmatch '\.(local|localhost|internal|invalid|test)$'
}
$healthy = @($snapshot.nodes | Where-Object { $_.status_udp -and $_.status_tcp -and $_.public_key -match '^[A-Fa-f0-9]{64}$' -and $_.port -ge 1 -and $_.port -le 65535 -and (Test-PublicHost $_.ipv4) -and @($_.tcp_ports | Where-Object { $_ -ge 1 -and $_ -le 65535 }).Count -gt 0 })
$chosen = @()
$countries = @{}
$keys = @{}
foreach ($node in $healthy) {
    if ($countries.ContainsKey([string]$node.location) -or $keys.ContainsKey($node.public_key)) { continue }
    $chosen += $node
    $countries[[string]$node.location] = $true
    $keys[$node.public_key] = $true
    if ($chosen.Count -eq 12) { break }
}
foreach ($node in $healthy) {
    if ($chosen.Count -eq 12) { break }
    if ($keys.ContainsKey($node.public_key)) { continue }
    $chosen += $node
    $keys[$node.public_key] = $true
}
if ($chosen.Count -lt 3) { throw 'The official snapshot did not contain enough validated healthy bootstrap nodes.' }
$result = @($chosen | ForEach-Object { [ordered]@{ host=$_.ipv4; port=[int]$_.port; public_key=$_.public_key.ToUpperInvariant(); tcp_ports=@($_.tcp_ports | Where-Object { $_ -ge 1 -and $_ -le 65535 } | Select-Object -Unique -First 6 | ForEach-Object { [int]$_ }) } })
New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot 'dist') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'dist\bootstrap-nodes.json'), (ConvertTo-Json -InputObject $result -Depth 5), (New-Object Text.UTF8Encoding($false)))
$metadata = [ordered]@{ source='https://nodes.tox.chat/json'; retrieved_utc=[DateTime]::UtcNow.ToString('o'); source_sha256=(Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash.ToLowerInvariant(); last_scan=$snapshot.last_scan; last_refresh=$snapshot.last_refresh; selected_count=$result.Count; note='Filtered to currently listed healthy UDP/TCP nodes, valid public hosts/keys/ports, diverse locations. This is a discovery snapshot, not an uptime guarantee.' }
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'seed-provenance.json'), (ConvertTo-Json -InputObject $metadata -Depth 4), (New-Object Text.UTF8Encoding($false)))
Write-Host "Validated and bundled $($result.Count) bootstrap nodes."
