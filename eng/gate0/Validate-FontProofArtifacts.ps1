[CmdletBinding()]
param(
    [string]$ArtifactRoot = (Join-Path $PSScriptRoot 'artifacts/fonts'),
    [string]$ManifestPath = (Join-Path $PSScriptRoot 'font-proof-artifacts.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-NotReparsePoint {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Description)

    $item = Get-Item -LiteralPath $Path -Force
    if ($item.Attributes.HasFlag([System.IO.FileAttributes]::ReparsePoint)) {
        throw "$Description must not be a reparse point: '$($item.FullName)'."
    }
}

function Get-SafeRelativePath {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Description)

    if ([string]::IsNullOrWhiteSpace($Path) -or [System.IO.Path]::IsPathRooted($Path)) {
        throw "$Description must be a non-empty relative path."
    }

    $normalised = $Path.Replace('\', '/')
    if ($normalised.Split('/') | Where-Object { $_ -eq '' -or $_ -eq '.' -or $_ -eq '..' }) {
        throw "$Description contains an unsafe path '$Path'."
    }
    return $normalised
}

function Resolve-ManifestArtifactPath {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$RelativePath,
        [Parameter(Mandatory)][string]$Description
    )

    $safeRelativePath = Get-SafeRelativePath -Path $RelativePath -Description $Description
    $candidate = [System.IO.Path]::GetFullPath((Join-Path $Root $safeRelativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar)))
    $rootPrefix = "$Root$([System.IO.Path]::DirectorySeparatorChar)"
    if (-not $candidate.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Description escapes the artifact root."
    }
    return $candidate
}

if (-not [System.IO.Path]::IsPathRooted($ArtifactRoot) -or -not [System.IO.Path]::IsPathRooted($ManifestPath)) {
    throw 'ArtifactRoot and ManifestPath must be explicit rooted paths. PATH discovery is prohibited.'
}
if (-not (Test-Path -LiteralPath $ArtifactRoot -PathType Container)) {
    throw "ArtifactRoot does not exist: '$ArtifactRoot'."
}
if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw "ManifestPath does not exist: '$ManifestPath'."
}

Assert-NotReparsePoint -Path $ArtifactRoot -Description 'Font proof artifact root'
Assert-NotReparsePoint -Path $ManifestPath -Description 'Font proof artifact manifest'
$resolvedArtifactRoot = (Resolve-Path -LiteralPath $ArtifactRoot).Path.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
$resolvedManifestPath = (Resolve-Path -LiteralPath $ManifestPath).Path

$manifest = Get-Content -LiteralPath $resolvedManifestPath -Raw | ConvertFrom-Json -AsHashtable
if ($manifest.schemaVersion -ne 1 -or $manifest.manifestVersion -ne 1) {
    throw 'Font proof artifact manifest schemaVersion and manifestVersion must both be 1.'
}
if ($manifest.profileId -ne 'P2.BtbnLgplShared.WindowsX64.20260820') {
    throw 'Font proof artifact manifest profileId does not match the approved Gate 0 profile.'
}
if ($manifest.scope.systemFontFallbackProhibited -ne $true -or $manifest.scope.fontDiscoveryMode -ne 'manifest-only-explicit-paths') {
    throw 'System-font fallback and font discovery are prohibited for reproducible Gate 0 proof.'
}
if ($manifest.scope.networkAccessPermitted -ne $false -or $manifest.scope.pathDiscoveryPermitted -ne $false) {
    throw 'Font proof validation must remain offline and must not use PATH discovery.'
}

$expectedFiles = @{}
function Add-ExpectedFile {
    param([Parameter(Mandatory)]$Entry, [Parameter(Mandatory)][string]$Kind)
    $path = Get-SafeRelativePath -Path $Entry.relativePath -Description "$Kind manifest entry"
    if ($expectedFiles.ContainsKey($path)) { throw "Font proof artifact manifest lists '$path' more than once." }
    if ($Entry.byteLength -lt 0 -or $Entry.sha256 -notmatch '^[0-9A-F]{64}$') {
        throw "$Kind manifest entry '$path' must have a non-negative byteLength and uppercase SHA-256."
    }
    $expectedFiles[$path] = [ordered]@{ entry = $Entry; kind = $Kind }
}

if ($null -eq $manifest.sourceArchives -or $manifest.sourceArchives.Count -ne 3) { throw 'Font proof artifact manifest must record exactly three source archives.' }
foreach ($archive in $manifest.sourceArchives) {
    if ([string]::IsNullOrWhiteSpace($archive.id) -or [string]::IsNullOrWhiteSpace($archive.release) -or
        $archive.officialReleaseUrl -notmatch '^https://github\.com/' -or $archive.officialArchiveUrl -notmatch '^https://github\.com/' -or
        $archive.byteLength -le 0 -or $archive.sha256 -notmatch '^[0-9A-F]{64}$') {
        throw 'Every source archive must have an exact release, official GitHub URLs, positive byteLength, and uppercase SHA-256.'
    }
}
if ($null -eq $manifest.licenses -or $manifest.licenses.Count -ne 3 -or $null -eq $manifest.fonts -or $manifest.fonts.Count -ne 3) {
    throw 'Font proof artifact manifest must contain exactly three license entries and three font entries.'
}
$licenseIds = @{}
foreach ($license in $manifest.licenses) {
    if ($license.spdx -ne 'OFL-1.1') { throw "License '$($license.id)' must use OFL-1.1." }
    if ([string]::IsNullOrWhiteSpace($license.sourceArchiveId) -or [string]::IsNullOrWhiteSpace($license.sourceArchiveMemberPath)) {
        throw "License '$($license.id)' must record its exact source archive and archive member path."
    }
    $licenseIds[$license.id] = $true
    Add-ExpectedFile -Entry $license -Kind 'License'
}
$archiveIds = @($manifest.sourceArchives | ForEach-Object { $_.id })
foreach ($license in $manifest.licenses) {
    if ($archiveIds -notcontains $license.sourceArchiveId) { throw "License '$($license.id)' references an unknown source archive." }
    [void](Get-SafeRelativePath -Path $license.sourceArchiveMemberPath -Description "License '$($license.id)' source archive member")
}
foreach ($font in $manifest.fonts) {
    if ($archiveIds -notcontains $font.sourceArchiveId) { throw "Font '$($font.id)' references an unknown source archive." }
    if (-not $licenseIds.ContainsKey($font.licenseId) -or [string]::IsNullOrWhiteSpace($font.sourceArchiveMemberPath) -or [string]::IsNullOrWhiteSpace($font.role) -or [string]::IsNullOrWhiteSpace($font.locale)) {
        throw "Font '$($font.id)' must map to a recorded OFL-1.1 license, exact source archive member, role, and locale."
    }
    [void](Get-SafeRelativePath -Path $font.sourceArchiveMemberPath -Description "Font '$($font.id)' source archive member")
    Add-ExpectedFile -Entry $font -Kind 'Font'
}
foreach ($documentation in @($manifest.documentation)) { Add-ExpectedFile -Entry $documentation -Kind 'Documentation' }

$actualFiles = @{}
foreach ($directory in Get-ChildItem -LiteralPath $resolvedArtifactRoot -Directory -Recurse -Force) {
    Assert-NotReparsePoint -Path $directory.FullName -Description 'Font proof artifact directory'
}
foreach ($file in Get-ChildItem -LiteralPath $resolvedArtifactRoot -File -Recurse -Force) {
    Assert-NotReparsePoint -Path $file.FullName -Description 'Font proof artifact file'
    $relative = [System.IO.Path]::GetRelativePath($resolvedArtifactRoot, $file.FullName).Replace('\', '/')
    $actualFiles[(Get-SafeRelativePath -Path $relative -Description 'Discovered artifact file')] = $file
}

$missing = @($expectedFiles.Keys | Where-Object { -not $actualFiles.ContainsKey($_) })
$additional = @($actualFiles.Keys | Where-Object { -not $expectedFiles.ContainsKey($_) })
if ($missing.Count -gt 0) { throw "Font proof artifact set is missing required file(s): $($missing -join ', ')." }
if ($additional.Count -gt 0) { throw "Font proof artifact set contains additional file(s): $($additional -join ', ')." }

foreach ($path in $expectedFiles.Keys) {
    $entry = $expectedFiles[$path].entry
    $file = $actualFiles[$path]
    $resolvedPath = Resolve-ManifestArtifactPath -Root $resolvedArtifactRoot -RelativePath $path -Description 'Font proof artifact'
    if ($file.FullName -ne $resolvedPath) { throw "Font proof artifact '$path' did not resolve to its expected path." }
    if ($file.Length -ne [long]$entry.byteLength) { throw "Font proof artifact '$path' byteLength does not match the manifest." }
    $actualHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($actualHash -ne $entry.sha256) { throw "Font proof artifact '$path' SHA-256 does not match the manifest." }
}

[ordered]@{
    schemaVersion = 1
    artifactSetId = $manifest.artifactSetId
    profileId = $manifest.profileId
    status = 'validated'
    proofOnly = $manifest.scope.proofOnly
    systemFontFallbackProhibited = $manifest.scope.systemFontFallbackProhibited
    networkAccessPermitted = $manifest.scope.networkAccessPermitted
    pathDiscoveryPermitted = $manifest.scope.pathDiscoveryPermitted
    filesValidated = @($expectedFiles.Keys | Sort-Object)
} | ConvertTo-Json -Depth 4
