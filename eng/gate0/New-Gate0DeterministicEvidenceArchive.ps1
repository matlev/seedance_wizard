[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SourceRoot,
    [Parameter(Mandatory)][string]$ApprovedSourceRoot,
    [Parameter(Mandatory)][string]$ArtifactRoot,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$ArchivePath,
    [Parameter(Mandatory)][string]$ManifestPath,
    [ValidateSet('None', 'AfterArchiveTempWrite', 'AfterArchiveTempValidation', 'AfterManifestTempCreate', 'AfterManifestTempWrite', 'BeforeArchivePromotion', 'BeforeManifestPromotion')][string]$TestFailurePhase = 'None',
    [ValidateSet('None', 'Archive', 'Manifest')][string]$TestRaceFinalPath = 'None',
    [ValidateSet('None', 'Archive', 'Manifest')][string]$TestPrecreateTemporaryPath = 'None'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-RootedPath([string]$Path, [string]$Label) {
    if (-not [IO.Path]::IsPathRooted($Path)) { throw "$Label must be an absolute path." }
    return [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
}

function Test-ContainedPath([string]$Candidate, [string]$Root) {
    return $Candidate.Equals($Root, [StringComparison]::OrdinalIgnoreCase) -or
        $Candidate.StartsWith($Root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-ExistingNonReparseDirectory([string]$Path, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { throw "$Label must be an existing directory." }
    foreach ($item in @((Get-Item -LiteralPath $Path -Force)) + @(Get-ChildItem -LiteralPath $Path -Force -Recurse)) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "$Label contains a reparse point: $($item.FullName)" }
    }
}

function Assert-NonReparseAncestors([string]$Path, [string]$StopAt, [string]$Label) {
    $current = Get-Item -LiteralPath $Path -Force
    while ($true) {
        if (($current.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "$Label has a reparse-point ancestor: $($current.FullName)" }
        if ($current.FullName.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar).Equals($StopAt, [StringComparison]::OrdinalIgnoreCase)) { return }
        if ($null -eq $current.Parent) { throw "$Label escaped its approved ancestor boundary." }
        $current = $current.Parent
    }
}

function Get-Sha256([string]$Path) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $stream = [IO.File]::OpenRead($Path)
        try { return ([Convert]::ToHexString($sha.ComputeHash($stream))) }
        finally { $stream.Dispose() }
    }
    finally { $sha.Dispose() }
}

function Get-StreamSha256([IO.Stream]$Stream) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [Convert]::ToHexString($sha.ComputeHash($Stream)) }
    finally { $sha.Dispose() }
}

function New-UniqueTemporaryPath([string]$FinalPath, [string]$Kind) {
    $parent = [IO.Path]::GetDirectoryName($FinalPath)
    $name = [IO.Path]::GetFileName($FinalPath)
    do { $candidate = Join-Path $parent ('.' + $name + '.tmp-' + [guid]::NewGuid().ToString('N')) } while (Test-Path -LiteralPath $candidate)
    if ($TestPrecreateTemporaryPath -eq $Kind) {
        [IO.File]::WriteAllText($candidate, "raced-temporary-$Kind", [Text.UTF8Encoding]::new($false))
    }
    return $candidate
}

function Invoke-TestHook([string]$Phase, [string]$TargetPath, [string]$HookName) {
    if ($TestFailurePhase -eq $Phase) { throw "Injected deterministic archive test failure at $Phase." }
    if ($HookName -ne 'None' -and $TestRaceFinalPath -eq $HookName) {
        [IO.File]::WriteAllText($TargetPath, "raced-$HookName", [Text.UTF8Encoding]::new($false))
    }
}

$source = Resolve-RootedPath $SourceRoot 'SourceRoot'
$approved = Resolve-RootedPath $ApprovedSourceRoot 'ApprovedSourceRoot'
$artifact = Resolve-RootedPath $ArtifactRoot 'ArtifactRoot'
$output = Resolve-RootedPath $OutputDirectory 'OutputDirectory'
$archive = Resolve-RootedPath $ArchivePath 'ArchivePath'
$manifest = Resolve-RootedPath $ManifestPath 'ManifestPath'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$repositoryParent = [IO.Path]::GetDirectoryName($repository)

if (($TestFailurePhase -ne 'None' -or $TestRaceFinalPath -ne 'None' -or $TestPrecreateTemporaryPath -ne 'None') -and -not (Test-Path -LiteralPath (Join-Path $repository '.gate0-deterministic-archive-test-sentinel') -PathType Leaf)) {
    throw 'Deterministic archive test hooks require an isolated test repository sentinel.'
}

if ([IO.Path]::GetDirectoryName($artifact) -ne $repositoryParent) { throw 'ArtifactRoot must be a sibling of the repository.' }
if ([IO.Path]::GetDirectoryName($approved) -ne $repositoryParent) { throw 'ApprovedSourceRoot must be a sibling of the repository.' }
if ($approved.Equals($repository, [StringComparison]::OrdinalIgnoreCase) -or $approved.Equals($artifact, [StringComparison]::OrdinalIgnoreCase)) { throw 'ApprovedSourceRoot must be distinct from the repository and ArtifactRoot.' }
if (-not (Test-ContainedPath $source $approved)) { throw 'SourceRoot escaped ApprovedSourceRoot.' }
if (-not (Test-ContainedPath $output $approved)) { throw 'OutputDirectory escaped ApprovedSourceRoot.' }
if (Test-ContainedPath $output $source -or Test-ContainedPath $source $output) { throw 'OutputDirectory and SourceRoot must not overlap.' }
if (-not (Test-ContainedPath $archive $output) -or -not (Test-ContainedPath $manifest $output)) { throw 'ArchivePath and ManifestPath must be under OutputDirectory.' }
if ($archive.Equals($manifest, [StringComparison]::OrdinalIgnoreCase)) { throw 'ArchivePath and ManifestPath must be distinct.' }
if ((Test-Path -LiteralPath $archive) -or (Test-Path -LiteralPath $manifest)) { throw 'ArchivePath and ManifestPath must be new paths.' }
$archiveParent = [IO.Path]::GetDirectoryName($archive)
$manifestParent = [IO.Path]::GetDirectoryName($manifest)

Assert-ExistingNonReparseDirectory $source 'SourceRoot'
Assert-ExistingNonReparseDirectory $approved 'ApprovedSourceRoot'
Assert-ExistingNonReparseDirectory $artifact 'ArtifactRoot'
Assert-ExistingNonReparseDirectory $output 'OutputDirectory'
Assert-ExistingNonReparseDirectory $archiveParent 'ArchivePath parent'
Assert-ExistingNonReparseDirectory $manifestParent 'ManifestPath parent'
Assert-NonReparseAncestors $repository $repositoryParent 'Repository'
Assert-NonReparseAncestors $artifact $repositoryParent 'ArtifactRoot'
Assert-NonReparseAncestors $approved $repositoryParent 'ApprovedSourceRoot'
Assert-NonReparseAncestors $output $approved 'OutputDirectory'
Assert-NonReparseAncestors $archiveParent $output 'ArchivePath parent'
Assert-NonReparseAncestors $manifestParent $output 'ManifestPath parent'

$files = @(Get-ChildItem -LiteralPath $source -File -Force -Recurse | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($source, $_.FullName).Replace('\', '/')
    if ([IO.Path]::IsPathRooted($relative) -or $relative.Split('/') -contains '..' -or [string]::IsNullOrWhiteSpace($relative)) { throw 'SourceRoot contains an unsafe relative path.' }
    [pscustomobject]@{ File = $_; RelativePath = $relative }
})
[Array]::Sort([object[]]$files, [System.Comparison[object]]{
    param($left, $right)
    return [StringComparer]::Ordinal.Compare([string]$left.RelativePath, [string]$right.RelativePath)
})
if ($files.Count -eq 0) { throw 'SourceRoot contains no files.' }

$entries = @($files | ForEach-Object {
    [ordered]@{ relativePath = $_.RelativePath; byteSize = [int64]$_.File.Length; sha256 = Get-Sha256 $_.File.FullName }
})

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$timestamp = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
$archiveTemp = New-UniqueTemporaryPath $archive 'Archive'
$manifestTemp = New-UniqueTemporaryPath $manifest 'Manifest'
$ownsArchiveTemp = $false
$ownsManifestTemp = $false
try {
    $fileStream = [IO.File]::Open($archiveTemp, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $ownsArchiveTemp = $true
    try {
        $zip = [IO.Compression.ZipArchive]::new($fileStream, [IO.Compression.ZipArchiveMode]::Create, $false, [Text.UTF8Encoding]::new($false))
        try {
            foreach ($file in $files) {
                $entry = $zip.CreateEntry($file.RelativePath, [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $timestamp
                $input = [IO.File]::OpenRead($file.File.FullName)
                try {
                    $destination = $entry.Open()
                    try { $input.CopyTo($destination) }
                    finally { $destination.Dispose() }
                }
                finally { $input.Dispose() }
            }
        }
        finally { $zip.Dispose() }
    }
    finally { $fileStream.Dispose() }
    Invoke-TestHook 'AfterArchiveTempWrite' $archive 'None'

$archiveEntries = @()
    $readStream = [IO.File]::OpenRead($archiveTemp)
    try {
        $zip = [IO.Compression.ZipArchive]::new($readStream, [IO.Compression.ZipArchiveMode]::Read, $false)
        try {
            $archiveEntries = @($zip.Entries)
            if ($archiveEntries.Count -ne $entries.Count) { throw 'Archive entry count does not match the source manifest.' }
            for ($index = 0; $index -lt $entries.Count; $index++) {
                $expected = $entries[$index]; $actual = $archiveEntries[$index]
                if ($actual.FullName -cne $expected.relativePath -or [int64]$actual.Length -ne [int64]$expected.byteSize -or $actual.LastWriteTime.DateTime -ne $timestamp.UtcDateTime) { throw 'Archive entry metadata does not match the source manifest.' }
                $entryStream = $actual.Open()
                try { if ((Get-StreamSha256 $entryStream) -cne $expected.sha256) { throw 'Archive entry hash does not match the source manifest.' } }
                finally { $entryStream.Dispose() }
            }
        }
        finally { $zip.Dispose() }
    }
    finally { $readStream.Dispose() }
    Invoke-TestHook 'AfterArchiveTempValidation' $archive 'None'

    $archiveInfo = Get-Item -LiteralPath $archiveTemp -Force
    $document = [ordered]@{
        schemaVersion = 1
        archiveFormat = 'zip'
        entryTimestampUtc = '1980-01-01T00:00:00Z'
        entries = $entries
        archive = [ordered]@{ byteSize = [int64]$archiveInfo.Length; sha256 = Get-Sha256 $archiveTemp }
    }
    $manifestStream = [IO.File]::Open($manifestTemp, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $ownsManifestTemp = $true
    try {
        Invoke-TestHook 'AfterManifestTempCreate' $manifest 'None'
        $writer = [IO.StreamWriter]::new($manifestStream, [Text.UTF8Encoding]::new($false), 1024, $true)
        try { $writer.Write(($document | ConvertTo-Json -Depth 16) + "`n") }
        finally { $writer.Dispose() }
    }
    finally { $manifestStream.Dispose() }
    Invoke-TestHook 'AfterManifestTempWrite' $manifest 'None'
    Invoke-TestHook 'BeforeArchivePromotion' $archive 'Archive'
    [IO.File]::Move($archiveTemp, $archive)
    $archiveTemp = ''
    $ownsArchiveTemp = $false
    Invoke-TestHook 'BeforeManifestPromotion' $manifest 'Manifest'
    [IO.File]::Move($manifestTemp, $manifest)
    $manifestTemp = ''
    $ownsManifestTemp = $false
}
finally {
    if ($ownsArchiveTemp -and -not [string]::IsNullOrWhiteSpace($archiveTemp) -and (Test-Path -LiteralPath $archiveTemp -PathType Leaf)) { Remove-Item -LiteralPath $archiveTemp -Force }
    if ($ownsManifestTemp -and -not [string]::IsNullOrWhiteSpace($manifestTemp) -and (Test-Path -LiteralPath $manifestTemp -PathType Leaf)) { Remove-Item -LiteralPath $manifestTemp -Force }
}

[pscustomobject]@{ archivePath = $archive; manifestPath = $manifest; archiveSha256 = $document.archive.sha256; archiveByteSize = $document.archive.byteSize; entryCount = $entries.Count }
