[CmdletBinding()]
param([string]$Archive)
$ErrorActionPreference = 'Stop'
if (!$Archive) { $Archive = Join-Path $PSScriptRoot '..\dist\TavernHubSource.zip' }
$Archive = [IO.Path]::GetFullPath($Archive)
if (!(Test-Path -LiteralPath $Archive -PathType Leaf)) { throw 'Run Package-Source.ps1 first.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($Archive)
$checks = 0
function Assert-Source([bool]$Condition,[string]$Message) {
    if (!$Condition) { throw ('FAIL '+$Message) }
    $script:checks++
}
function Get-EntryHash($Entry) {
    $stream = $Entry.Open()
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','').ToLowerInvariant() }
    finally { $sha.Dispose(); $stream.Dispose() }
}
function Read-EntryText($Entry) {
    $reader = New-Object IO.StreamReader($Entry.Open(),[Text.Encoding]::UTF8)
    try { return $reader.ReadToEnd() }
    finally { $reader.Dispose() }
}
try {
    $entries = @{}
    foreach ($entry in $zip.Entries) {
        $name = $entry.FullName.Replace('\','/')
        if ($name.EndsWith('/')) { continue }
        Assert-Source (!$entries.ContainsKey($name)) ('No duplicate ZIP path: '+$name)
        Assert-Source ($name -notmatch '(^|/)\.\.(/|$)|^[A-Za-z]:|^[/\\]') ('Contained relative ZIP path: '+$name)
        Assert-Source ($name -notmatch '(^|/)(research|UserData|artifacts|__pycache__|build|buildtrees|installed|packages|registry-cache|binary-cache|rename-cache|dist)(/|$)') ('Excluded private/generated directory: '+$name)
        Assert-Source ($name -notmatch '\.(exe|dll|pyc|log)$') ('No compiled binary or log: '+$name)
        Assert-Source ($name -notmatch '(^|/)(TavernMesh\.json|TavernSocial\.json|TavernFriendCode\.txt|tavern_launcher\.json|social-data\.json)') ('No saved identity or profile: '+$name)
        $entries[$name] = $entry
        if ($name -match '\.(cs|py|ps1|md|json|cmake|patch|txt)$') {
            $content = Read-EntryText $entry
            Assert-Source ($content -notmatch '(?i)[A-Za-z]:[\\/]Users[\\/]') ('No personal absolute path: '+$name)
        }
    }
    foreach ($required in @('README.md','SOURCE-SHA256SUMS.txt','setup-v1.8.4/Build-All.ps1','setup-v1.8.4/Package-Source.ps1','setup-v1.8.4/src/Setup.cs','setup-v1.8.4/payload/Setup-Worker.ps1','setup-v1.8.4/payload/Setup-ServerCards.ps1','setup-v1.8.4/payload/addons/tavern_native_menu/client.py','setup-v1.8.4/mod-assets/tavern-badge-hires.png','setup-v1.8.4/mesh-native/Build-Native.ps1','setup-v1.8.4/mesh-native/Package-Native.ps1','setup-v1.8.4/mesh-native/bootstrap-nodes.json','setup-v1.8.4/mesh-native/licenses/TOXCORE-LICENSE.txt','setup-v1.8.4/mesh-native/licenses/LIBSODIUM-LICENSE.txt','setup-v1.8.4/mesh-native/licenses/PTHREADS-LICENSE.txt','setup-v1.8.4/mesh-native/ports/libsodium/portfile.cmake','setup-v1.8.4/mesh-native/ports/pthreads/portfile.cmake')) {
        Assert-Source $entries.ContainsKey($required) ('Required readable/build input: '+$required)
    }
    $projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    foreach ($relative in @('mod-src','src','mesh-peer/src','mesh-server/src','social-server/src')) {
        foreach ($file in @(Get-ChildItem -LiteralPath (Join-Path $projectRoot $relative) -Filter '*.cs' -File -Recurse)) {
            $name = 'setup-v1.8.4/'+$file.FullName.Substring($projectRoot.Length+1).Replace('\','/')
            Assert-Source $entries.ContainsKey($name) ('All project source files included: '+$name)
            Assert-Source ((Get-EntryHash $entries[$name]) -eq (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()) ('Exact unmodified project source: '+$name)
        }
    }
    $manifest = @{}
    foreach ($line in (Read-EntryText $entries['SOURCE-SHA256SUMS.txt']) -split '\r?\n') {
        if (!$line) { continue }
        Assert-Source ($line -match '^([a-f0-9]{64})  (.+)$') 'Valid source checksum line'
        $hash = $Matches[1]; $name = $Matches[2]
        Assert-Source (!$manifest.ContainsKey($name)) ('Unique checksum input: '+$name)
        Assert-Source $entries.ContainsKey($name) ('Manifest file exists: '+$name)
        Assert-Source ((Get-EntryHash $entries[$name]) -eq $hash) ('Manifest checksum matches: '+$name)
        $manifest[$name] = $hash
    }
    Assert-Source ($manifest.Count -eq $entries.Count-1) 'Checksum manifest covers every other archive file'
    foreach ($source in @(
        @{Name='c-toxcore-v0.2.23.tar.gz';Hash='15cdd006ed7793dfc657e340ef9f218f6637d2fe5b130704d39b961389bb6cd6'},
        @{Name='jedisct1-libsodium-1.0.22-RELEASE.tar.gz';Hash='5838bb0c3da6148c24ebe531d1ed1297de9a87aea77d426bcd99f289e681631c'},
        @{Name='pthreads4w-code-v3.0.0.zip';Hash='b81136effb7185c77601fe2e0e6ac19bd996912e4814cebdd3010b0fac9e259b'}
    )) {
        $name = 'setup-v1.8.4/mesh-native/downloads/'+$source.Name
        Assert-Source ($entries.ContainsKey($name) -and (Get-EntryHash $entries[$name]) -eq $source.Hash) ('Pinned corresponding-source archive: '+$source.Name)
    }
    Write-Output ('Passed '+$checks+' source package checks across '+$entries.Count+' files.')
} finally { $zip.Dispose() }
