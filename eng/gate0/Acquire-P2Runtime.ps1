[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [uri] $SourceUri,

    [string] $ManifestPath = (Join-Path $PSScriptRoot 'manifests\p2-btbn-lgplv3-shared-windows-x64-20260820.json'),

    [string] $CacheRoot = (Join-Path ([System.IO.Path]::GetTempPath()) 'ReelForge-Gate0'),

    [string] $ReportPath,

    [switch] $AllowRetentionLimitedUpstream
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$upstreamUri = [uri] $manifest.upstreamUrl
$sourceText = $SourceUri.AbsoluteUri

if ($sourceText -match '(?i)(/latest/|[-_/]latest[-_.?/])') {
    throw 'Mutable latest artifact references are forbidden.'
}

$usesRetentionLimitedUpstream = $sourceText.Equals($upstreamUri.AbsoluteUri, [System.StringComparison]::Ordinal)
if ($usesRetentionLimitedUpstream -and -not $AllowRetentionLimitedUpstream) {
    throw 'The approved upstream daily artifact is retention-limited. Pass -AllowRetentionLimitedUpstream for an explicit local proof, or provide the project-controlled preserved artifact URI.'
}

if ($usesRetentionLimitedUpstream -and $env:CI -eq 'true') {
    throw 'Long-term CI must not depend on the retention-limited BtbN daily URL. Configure the exact project-controlled preservation or re-pin an approved monthly build.'
}

$cacheRootFull = [System.IO.Path]::GetFullPath($CacheRoot)
$profileRoot = Join-Path $cacheRootFull $manifest.archiveSha256
$archivePath = Join-Path $profileRoot $manifest.archiveName
$extractionRoot = Join-Path $profileRoot 'runtime'
$runtimeRoot = Join-Path $extractionRoot $manifest.archiveRoot

New-Item -ItemType Directory -Force -Path $profileRoot | Out-Null

if (-not (Test-Path -LiteralPath $archivePath)) {
    $temporaryArchive = Join-Path $profileRoot ([System.IO.Path]::GetRandomFileName())
    try {
        Invoke-WebRequest -Uri $SourceUri -OutFile $temporaryArchive
        Move-Item -LiteralPath $temporaryArchive -Destination $archivePath
    }
    finally {
        if (Test-Path -LiteralPath $temporaryArchive) {
            Remove-Item -LiteralPath $temporaryArchive -Force
        }
    }
}

$archive = Get-Item -LiteralPath $archivePath
if ($archive.Length -ne [long] $manifest.archiveSize) {
    throw "P2 archive size mismatch. Expected $($manifest.archiveSize); observed $($archive.Length)."
}

$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToUpperInvariant()
if (-not $archiveHash.Equals([string] $manifest.archiveSha256, [System.StringComparison]::Ordinal)) {
    throw "P2 archive hash mismatch. Expected $($manifest.archiveSha256); observed $archiveHash."
}

if (-not (Test-Path -LiteralPath $runtimeRoot)) {
    $temporaryExtraction = Join-Path $profileRoot ('extract-' + [System.Guid]::NewGuid().ToString('N'))
    try {
        Expand-Archive -LiteralPath $archivePath -DestinationPath $temporaryExtraction
        Move-Item -LiteralPath $temporaryExtraction -Destination $extractionRoot
    }
    finally {
        if (Test-Path -LiteralPath $temporaryExtraction) {
            Remove-Item -LiteralPath $temporaryExtraction -Recurse -Force
        }
    }
}

$verifiedFiles = foreach ($runtimeFile in $manifest.runtimeFiles) {
    $relativePath = ([string] $runtimeFile.path).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    $fullPath = [System.IO.Path]::GetFullPath((Join-Path $runtimeRoot $relativePath))
    $runtimePrefix = $runtimeRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($runtimePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Runtime manifest path escapes the verified root: $relativePath"
    }
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "P2 runtime file is missing: $relativePath"
    }
    $observedHash = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToUpperInvariant()
    if (-not $observedHash.Equals([string] $runtimeFile.sha256, [System.StringComparison]::Ordinal)) {
        throw "P2 runtime hash mismatch for $relativePath. Expected $($runtimeFile.sha256); observed $observedHash."
    }
    [ordered]@{ path = $relativePath; sha256 = $observedHash }
}

$ffmpegPath = Join-Path $runtimeRoot 'bin\ffmpeg.exe'
$ffprobePath = Join-Path $runtimeRoot 'bin\ffprobe.exe'
$report = [ordered]@{
    schemaVersion = 1
    profileId = $manifest.profileId
    licensePath = $manifest.licensePath
    proofOnly = $true
    sourceUri = $sourceText
    originalUpstreamUri = $manifest.upstreamUrl
    retentionLimitedUpstream = $usesRetentionLimitedUpstream
    archivePath = $archivePath
    archiveSize = $archive.Length
    archiveSha256 = $archiveHash
    runtimeRoot = $runtimeRoot
    ffmpegPath = $ffmpegPath
    ffprobePath = $ffprobePath
    runtimeFiles = @($verifiedFiles)
    acquiredAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    statement = 'Archive and runtime closure match the reviewed Gate 0 P2 profile; semantic capabilities are not yet proven.'
}

$json = $report | ConvertTo-Json -Depth 6
if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
    $reportFullPath = [System.IO.Path]::GetFullPath($ReportPath)
    $reportDirectory = Split-Path -Parent $reportFullPath
    if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
        New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null
    }
    Set-Content -LiteralPath $reportFullPath -Value $json -Encoding utf8NoBOM
}

Write-Output $json
