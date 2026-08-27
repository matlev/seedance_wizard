[CmdletBinding()]
param(
    [string] $ArtifactRoot,
    [switch] $Remote,
    [switch] $RequireEffectiveSeal,
    [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'evidence/Gate0EvidenceContainment.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Gate0ArtifactTools.psm1') -Force

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')).TrimEnd([IO.Path]::DirectorySeparatorChar)
$seal = Assert-Gate0LegacyEvidenceSeal $repositoryRoot -RequireEffective:$RequireEffectiveSeal
$root = $seal.Root
$artifactRootResolved = Resolve-Gate0ArtifactRoot $ArtifactRoot
Assert-Gate0EvidenceNoReparsePointAncestors $artifactRootResolved ([IO.Path]::GetDirectoryName($repositoryRoot))
$newArtifactCount = 0
$newArtifactBytes = [int64]0
$remoteVerified = 0
$indexedPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$bundle = $null
try {
    if ($Remote) { $bundle = New-Gate0R2ClientBundle }
    foreach ($run in @($root.Index.runs)) {
        $shardPath = Join-Path (Split-Path -Parent $root.Path) ([string]$run.shardPath).Replace('/', [IO.Path]::DirectorySeparatorChar)
        $shard = Read-Gate0EvidenceShard $shardPath
        foreach ($artifact in @($shard.Manifest.artifacts)) {
            [void]$indexedPaths.Add([string]$artifact.relativePath)
            $localPath = [IO.Path]::GetFullPath((Join-Path $artifactRootResolved ([string]$artifact.relativePath).Replace('/', [IO.Path]::DirectorySeparatorChar)))
            if (-not $localPath.StartsWith("$artifactRootResolved$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $localPath -PathType Leaf)) { throw "Future evidence artifact is missing or escaped: $($artifact.artifactId)" }
            Assert-Gate0EvidenceNoReparsePointAncestors $localPath $artifactRootResolved
            if ((Get-Item -LiteralPath $localPath -Force).Length -ne [int64]$artifact.byteSize -or (Get-Gate0EvidenceSha256 $localPath) -ne [string]$artifact.sha256) { throw "Future evidence artifact failed local byte verification: $($artifact.artifactId)" }
            if (([string]$artifact.relativePath).EndsWith('.compact-attempt.json', [StringComparison]::OrdinalIgnoreCase) -and [int64]$artifact.byteSize -gt 262144) { throw 'Compact attempt record exceeds its 256 KiB cap.' }
            if ($Remote) {
                $entry = [pscustomobject]@{ ArtifactId = [string]$artifact.artifactId; ObjectKey = [string]$artifact.r2ObjectKey; Size = [int64]$artifact.byteSize; Sha256 = [string]$artifact.sha256 }
                $metadata = $bundle.Client.HeadObjectAsync($bundle.BucketName, $entry.ObjectKey).GetAwaiter().GetResult()
                if ($null -eq $metadata -or ($null -ne $metadata.ContentLength -and [int64]$metadata.ContentLength -ne $entry.Size)) { throw "Future evidence R2 object is missing or has the wrong size: $($artifact.artifactId)" }
                Invoke-Gate0RemoteByteVerification $bundle $entry
                $remoteVerified++
            }
            $newArtifactCount++
            $newArtifactBytes += [int64]$artifact.byteSize
        }
    }
}
finally {
    if ($null -ne $bundle) { $bundle.HttpClient.Dispose() }
}

$futureRoot = Join-Path $artifactRootResolved 'future/stage2'
$physicalPaths = @(if (Test-Path -LiteralPath $futureRoot -PathType Container) {
    Assert-Gate0EvidenceNoReparsePointAncestors $futureRoot $artifactRootResolved
    $futureItems = @(Get-ChildItem -LiteralPath $futureRoot -Force -Recurse)
    $futureReparse = @($futureItems | Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 })
    if ($futureReparse.Count -ne 0) { throw "The future evidence tree contains a reparse point: $($futureReparse[0].FullName)" }
    @(Get-ChildItem -LiteralPath $futureRoot -File -Recurse | ForEach-Object { [IO.Path]::GetRelativePath($artifactRootResolved, $_.FullName).Replace('\','/') })
} else { @() })
if ($physicalPaths.Count -ne $indexedPaths.Count) { throw 'The future evidence tree contains an unindexed or missing file.' }
foreach ($physicalPath in $physicalPaths) { if (-not $indexedPaths.Contains($physicalPath)) { throw "The future evidence tree contains an unindexed file: $physicalPath" } }

$result = [ordered]@{
    schemaVersion = 1
    validationId = 'Gate0.Stage2Evidence.Validation.V1'
    validatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    legacySealEffective = [bool]$seal.Effective
    rootIndexSha256 = $root.Sha256
    runCount = @($root.Index.runs).Count
    logicalArtifactCount = $newArtifactCount
    logicalArtifactBytes = $newArtifactBytes
    localByteVerificationPerformed = $true
    remoteByteVerificationPerformed = [bool]$Remote
    remotelyVerifiedThisRun = $remoteVerified
    mediaProcessesInvoked = 0
    disposition = 'passed'
}

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $output = [IO.Path]::GetFullPath($OutputPath)
    $parent = Split-Path -Parent $output
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) { throw 'Validation output parent must already exist.' }
    Assert-Gate0EvidenceNoReparsePointAncestors $parent $parent
    Write-Gate0EvidenceUtf8Atomic $output (($result | ConvertTo-Json -Depth 16) + "`n")
    if (-not (Test-Path -LiteralPath $output -PathType Leaf)) { throw 'Evidence validation output could not be persisted.' }
}
[pscustomobject]$result
