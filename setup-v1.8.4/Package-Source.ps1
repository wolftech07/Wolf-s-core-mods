[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$stageRoot = Join-Path ([IO.Path]::GetTempPath()) ('TavernHub-source-'+[Guid]::NewGuid().ToString('N'))
$stageProject = Join-Path $stageRoot 'setup-v1.8.4'
$output = Join-Path $projectRoot 'dist'

function Copy-SourceFile([string]$Source, [string]$Relative) {
    if (!(Test-Path -LiteralPath $Source -PathType Leaf)) { throw "Source release input is missing: $Relative." }
    $destination = [IO.Path]::GetFullPath((Join-Path $stageRoot $Relative))
    if (!$destination.StartsWith($stageRoot.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid source archive path.' }
    New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($destination)) | Out-Null
    Copy-Item -LiteralPath $Source -Destination $destination -Force
}

function Copy-SourceTree([string]$Relative, [string[]]$Extensions) {
    $directory = Join-Path $projectRoot $Relative
    if (!(Test-Path -LiteralPath $directory -PathType Container)) { throw "Source directory is missing: $Relative." }
    foreach ($file in @(Get-ChildItem -LiteralPath $directory -Recurse -File | Where-Object { $Extensions -contains $_.Extension.ToLowerInvariant() })) {
        if ($file.FullName -match '[\\/](dist|artifacts|__pycache__|UserData)[\\/]') { continue }
        $name = $file.FullName.Substring($projectRoot.Length+1)
        Copy-SourceFile $file.FullName ('setup-v1.8.4\'+$name)
    }
}

try {
    New-Item -ItemType Directory -Force -Path $stageProject,$output | Out-Null
    # Explicit allowlists keep runtime identities, research, binaries and caches out.
    foreach ($name in @('Build-All.ps1','Build-Mod.ps1','Build-Setup.ps1','Package-Setup.ps1','Package-Source.ps1','README.md','ADVANCED.md','MESH-FRIENDS.md','SOCIAL-TABLET.md','SOURCE-AND-BUILD.md')) {
        Copy-SourceFile (Join-Path $projectRoot $name) ('setup-v1.8.4\'+$name)
    }
    foreach ($name in @('SECURITY.md')) {
        if (Test-Path -LiteralPath (Join-Path $projectRoot $name) -PathType Leaf) {
            Copy-SourceFile (Join-Path $projectRoot $name) ('setup-v1.8.4\'+$name)
        }
    }
    $repositoryReadme = Join-Path (Split-Path -Parent $projectRoot) 'README.md'
    if (Test-Path -LiteralPath $repositoryReadme -PathType Leaf) {
        Copy-SourceFile $repositoryReadme 'PROJECT-README.md'
    }
    Copy-SourceFile (Join-Path $projectRoot 'SOURCE-AND-BUILD.md') 'README.md'
    foreach ($tree in @('mod-src','src','mesh-peer\src','mesh-server\src','social-server\src')) { Copy-SourceTree $tree @('.cs') }
    foreach ($tree in @('tests','mesh-server\tests','social-server\tests')) { Copy-SourceTree $tree @('.cs','.ps1','.py') }
    Copy-SourceTree 'mod-assets' @('.png','.md')
    Copy-SourceTree 'payload\addons' @('.py','.json')
    foreach ($name in @('Setup-Worker.ps1','Setup-ServerCards.ps1','integration-plan.json')) {
        Copy-SourceFile (Join-Path $projectRoot ('payload\'+$name)) ('setup-v1.8.4\payload\'+$name)
    }
    foreach ($entry in @(
        @{Tree='mesh-peer';Names=@('Build-Peer.ps1','README.md','LICENSE.txt','NEWTONSOFT-LICENSE.txt')},
        @{Tree='mesh-server';Names=@('Build-MeshServer.ps1')},
        @{Tree='social-server';Names=@('Build-Social.ps1','SOCIAL-HOSTING.md')},
        @{Tree='mesh-native';Names=@('Build-Native.ps1','Package-Native.ps1','Refresh-Seeds.ps1','Native-Smoke.py','vcpkg.json','README.md','THIRD-PARTY-NOTICES.txt','seed-provenance.json')}
    )) {
        foreach ($name in $entry.Names) {
            $relative = $entry.Tree+'\'+$name
            Copy-SourceFile (Join-Path $projectRoot $relative) ('setup-v1.8.4\'+$relative)
        }
    }
    $nativeRoot = Join-Path $projectRoot 'mesh-native'
    $seed = Join-Path $nativeRoot 'bootstrap-nodes.json'
    if (!(Test-Path -LiteralPath $seed -PathType Leaf)) { $seed = Join-Path $nativeRoot 'dist\bootstrap-nodes.json' }
    Copy-SourceFile $seed 'setup-v1.8.4\mesh-native\bootstrap-nodes.json'
    foreach ($name in @('TOXCORE-LICENSE.txt','LIBSODIUM-LICENSE.txt','PTHREADS-LICENSE.txt')) {
        $license = Join-Path $nativeRoot ('licenses\'+$name)
        if (!(Test-Path -LiteralPath $license -PathType Leaf)) { $license = Join-Path $nativeRoot ('dist\'+$name) }
        Copy-SourceFile $license ('setup-v1.8.4\mesh-native\licenses\'+$name)
    }
    $archives = @(
        @{Name='c-toxcore-v0.2.23.tar.gz';Hash='15cdd006ed7793dfc657e340ef9f218f6637d2fe5b130704d39b961389bb6cd6'},
        @{Name='jedisct1-libsodium-1.0.22-RELEASE.tar.gz';Hash='5838bb0c3da6148c24ebe531d1ed1297de9a87aea77d426bcd99f289e681631c'},
        @{Name='pthreads4w-code-v3.0.0.zip';Hash='b81136effb7185c77601fe2e0e6ac19bd996912e4814cebdd3010b0fac9e259b'}
    )
    foreach ($entry in $archives) {
        $archive = Join-Path $nativeRoot ('downloads\'+$entry.Name)
        if (!(Test-Path -LiteralPath $archive -PathType Leaf)) { $archive = Join-Path $nativeRoot ('source-package\'+$entry.Name) }
        if (!(Test-Path -LiteralPath $archive -PathType Leaf) -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.Hash) {
            throw "Missing or unverified dependency source archive: $($entry.Name)."
        }
        Copy-SourceFile $archive ('setup-v1.8.4\mesh-native\downloads\'+$entry.Name)
    }
    foreach ($name in @('libsodium','pthreads')) {
        $port = Join-Path $nativeRoot ('ports\'+$name)
        if (!(Test-Path -LiteralPath $port -PathType Container)) { $port = Join-Path $nativeRoot ('source-package\'+$name+'-port') }
        if (!(Test-Path -LiteralPath (Join-Path $port 'portfile.cmake') -PathType Leaf)) { throw "Missing corresponding source port: $name." }
        foreach ($file in @(Get-ChildItem -LiteralPath $port -Recurse -File)) {
            $relative = $file.FullName.Substring([IO.Path]::GetFullPath($port).Length+1)
            Copy-SourceFile $file.FullName ('setup-v1.8.4\mesh-native\ports\'+$name+'\'+$relative)
        }
    }
    $sourceFiles = @(Get-ChildItem -LiteralPath $stageRoot -Recurse -File | Sort-Object FullName)
    $hashes = foreach ($file in $sourceFiles) {
        (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$file.FullName.Substring($stageRoot.Length+1).Replace('\','/')
    }
    [IO.File]::WriteAllLines((Join-Path $stageRoot 'SOURCE-SHA256SUMS.txt'),[string[]]$hashes,(New-Object Text.UTF8Encoding($false)))
    $archivePath = Join-Path $output 'TavernHubSource.zip'
    Compress-Archive -LiteralPath @(Get-ChildItem -LiteralPath $stageRoot -Force | ForEach-Object FullName) -DestinationPath $archivePath -CompressionLevel Optimal -Force
    $archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()+'  TavernHubSource.zip'
    [IO.File]::WriteAllText((Join-Path $output 'TavernHubSource-SHA256SUMS.txt'),$archiveHash+[Environment]::NewLine,(New-Object Text.UTF8Encoding($false)))
    Write-Output ('Packaged readable source: '+$archivePath+' ('+$sourceFiles.Count+' files, plus SHA256 manifest).')
} finally {
    $resolvedStage = [IO.Path]::GetFullPath($stageRoot)
    $temporaryPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if (!$resolvedStage.StartsWith($temporaryPrefix,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolvedStage) -notmatch '^TavernHub-source-[0-9a-f]{32}$') { throw 'Unsafe source staging cleanup path.' }
    if (Test-Path -LiteralPath $resolvedStage) { Remove-Item -LiteralPath $resolvedStage -Recurse -Force }
}
