[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ArtifactRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-Sha256([string] $Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Assert-NoReparsePoints([string] $Root) {
    $reparsePoints = @((@((Get-Item -LiteralPath $Root -Force)) + @(Get-ChildItem -LiteralPath $Root -Force -Recurse)) | Where-Object {
        ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
    })
    if ($reparsePoints.Count -ne 0) {
        throw "The retained artifact root contains a reparse point: $($reparsePoints[0].FullName)"
    }
}
function Assert-NoReparsePointAncestors([string] $Path, [string] $StopAt) {
    $current = Get-Item -LiteralPath $Path -Force
    $resolvedStop = [IO.Path]::GetFullPath($StopAt).TrimEnd([IO.Path]::DirectorySeparatorChar)
    while ($null -ne $current) {
        if (($current.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "The retained artifact root has a reparse-point ancestor: $($current.FullName)"
        }
        if ($current.FullName.TrimEnd([IO.Path]::DirectorySeparatorChar).Equals($resolvedStop, [StringComparison]::OrdinalIgnoreCase)) { return }
        $current = if ($current -is [IO.DirectoryInfo]) { $current.Parent } else { $current.Directory }
    }
}
function Assert-SafeRelativePath([string] $Value, [string] $Label) {
    if ([string]::IsNullOrWhiteSpace($Value) -or [IO.Path]::IsPathRooted($Value) -or $Value.Contains('\')) { throw "Unsafe $Label." }
    foreach ($segment in $Value.Replace('\', '/').Split('/')) {
        if ([string]::IsNullOrEmpty($segment) -or $segment -in @('.', '..')) { throw "Unsafe $Label." }
    }
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..')).TrimEnd([IO.Path]::DirectorySeparatorChar)
$resolvedManifestPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'artifact-retention-manifest.json'))
$approvedArtifactRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetDirectoryName($repositoryRoot)) 'ReelForge.Gate0Artifacts')).TrimEnd([IO.Path]::DirectorySeparatorChar)
$candidateArtifactRoot = [IO.Path]::GetFullPath($ArtifactRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
if (-not $candidateArtifactRoot.Equals($approvedArtifactRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "The artifact root must be the approved repository sibling: $approvedArtifactRoot"
}

if (-not (Test-Path -LiteralPath $candidateArtifactRoot -PathType Container)) {
    throw "Artifact root does not exist: $candidateArtifactRoot"
}
if (-not (Test-Path -LiteralPath $resolvedManifestPath -PathType Leaf)) {
    throw "Tracked artifact manifest does not exist: $resolvedManifestPath"
}

$resolvedArtifactRoot = (Get-Item -LiteralPath $candidateArtifactRoot -Force).FullName.TrimEnd([IO.Path]::DirectorySeparatorChar)
Assert-NoReparsePoints $resolvedArtifactRoot
Assert-NoReparsePointAncestors $resolvedArtifactRoot ([IO.Path]::GetDirectoryName($repositoryRoot))
$manifest = Get-Content -LiteralPath $resolvedManifestPath -Raw | ConvertFrom-Json -Depth 20

if ($manifest.schemaVersion -ne 1 -or $manifest.artifactSetId -ne 'Gate0.InterimCorpus.20260825') {
    throw 'The tracked artifact manifest has an unsupported schema or artifact-set identity.'
}
if ($manifest.storage.rootName -ne 'ReelForge.Gate0Artifacts' -or $manifest.storage.productionArtifactRepository -or $manifest.storage.hostedCiEligible) {
    throw 'The tracked manifest does not preserve the approved interim-only storage boundary.'
}
if ($manifest.storage.classification -ne 'interim-local-only' -or $manifest.storage.separatelyBackedUpPrivateCopyVerified) {
    throw 'The tracked manifest must describe one local retained copy, not a synced or separately backed-up copy.'
}

$artifactIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$filenames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$fileCount = 0
$totalBytes = [int64] 0
$retainedPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

foreach ($group in @($manifest.groups)) {
    $groupCount = 0
    $groupBytes = [int64] 0
    foreach ($file in @($group.files)) {
        $artifactId = [string] $file.artifactId
        $relative = ([string] $file.filename).Replace('/', [IO.Path]::DirectorySeparatorChar)
        if (-not $artifactIds.Add($artifactId)) {
            throw "Duplicate artifact ID: $artifactId"
        }
        Assert-SafeRelativePath ([string] $file.filename) 'artifact filename'
        if (-not $filenames.Add([string] $file.filename)) {
            throw "Duplicate retained filename: $($file.filename)"
        }

        $path = [IO.Path]::GetFullPath((Join-Path $resolvedArtifactRoot $relative))
        if (-not $path.StartsWith("$resolvedArtifactRoot$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase)) {
            throw "Artifact filename escaped the retained root: $($file.filename)"
        }
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Retained artifact is missing: $($file.filename)"
        }

        $item = Get-Item -LiteralPath $path -Force
        $hash = Get-Sha256 $path
        if ($item.Length -ne [int64] $file.size -or $hash -ne [string] $file.sha256) {
            throw "Retained artifact failed size or SHA-256 verification: $($file.filename)"
        }

        [void] $retainedPaths.Add(([string] $file.filename).Replace('\', '/'))
        $groupCount++
        $groupBytes += $item.Length
        $fileCount++
        $totalBytes += $item.Length
    }

    if ($groupCount -ne [int] $group.fileCount -or $groupBytes -ne [int64] $group.totalBytes) {
        throw "Group totals do not match verified files: $($group.groupId)"
    }
}

$allGroups = @($manifest.groups)
foreach ($group in $allGroups) {
    if (@($group.proofRunIdentity).Count -eq 0) {
        throw "Proof-run identity is missing: $($group.groupId)"
    }

    foreach ($reference in @($group.producerRuntimeIdentity) + @($group.licenseRecords) + @($group.proofRunIdentity)) {
        $value = [string] $reference
        if ($value.StartsWith('artifact:', [StringComparison]::Ordinal)) {
            if (-not $retainedPaths.Contains($value.Substring('artifact:'.Length))) {
                throw "Artifact reference is not retained: $value"
            }
        }
        elseif ($value.StartsWith('repository:', [StringComparison]::Ordinal)) {
            $portableRepositoryPath = $value.Substring('repository:'.Length)
            Assert-SafeRelativePath $portableRepositoryPath 'repository reference'
            $relativeRepositoryPath = $portableRepositoryPath.Replace('/', [IO.Path]::DirectorySeparatorChar)
            $repositoryPath = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $relativeRepositoryPath))
            if (-not $repositoryPath.StartsWith("$repositoryRoot$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase) -or
                -not (Test-Path -LiteralPath $repositoryPath -PathType Leaf)) {
                throw "Repository reference is missing or escaped the repository: $value"
            }
            Assert-NoReparsePointAncestors $repositoryPath $repositoryRoot
        }
        elseif ($value.StartsWith('manifest:', [StringComparison]::Ordinal)) {
            if ($value -ne 'manifest:p3Authenticode' -and $value -ne 'manifest:p3-proof-status-incomplete') {
                throw "Unknown manifest-scoped reference: $value"
            }
        }
        elseif (-not $value.StartsWith('upstream:', [StringComparison]::Ordinal)) {
            throw "Reference lacks an explicit artifact, repository, upstream, or manifest scope: $value"
        }
    }
}

if ($fileCount -ne [int] $manifest.totals.fileCount -or
    $totalBytes -ne [int64] $manifest.totals.totalBytes -or
    $allGroups.Count -ne [int] $manifest.totals.groupCount) {
    throw 'Manifest totals do not match the verified retained corpus.'
}

$localManifest = Join-Path $resolvedArtifactRoot 'artifact-retention-manifest.json'
if (-not (Test-Path -LiteralPath $localManifest -PathType Leaf)) {
    throw 'The retained root does not contain its tracked manifest copy.'
}
$trackedManifestSha256 = Get-Sha256 $resolvedManifestPath
$localManifestSha256 = Get-Sha256 $localManifest
if ($trackedManifestSha256 -ne $localManifestSha256) {
    throw 'The retained manifest copy does not match the tracked manifest.'
}

$actualRelativeFiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($item in @(Get-ChildItem -LiteralPath $resolvedArtifactRoot -Force -File -Recurse)) {
    $relative = [IO.Path]::GetRelativePath($resolvedArtifactRoot, $item.FullName).Replace('\', '/')
    [void] $actualRelativeFiles.Add($relative)
}
$expectedRelativeFiles = [Collections.Generic.HashSet[string]]::new($retainedPaths, [StringComparer]::OrdinalIgnoreCase)
[void] $expectedRelativeFiles.Add('artifact-retention-manifest.json')
if (-not $actualRelativeFiles.SetEquals($expectedRelativeFiles)) {
    $unexpected = @($actualRelativeFiles | Where-Object { -not $expectedRelativeFiles.Contains($_) } | Sort-Object)
    $missing = @($expectedRelativeFiles | Where-Object { -not $actualRelativeFiles.Contains($_) } | Sort-Object)
    throw "The retained root contains an unmanifested or missing file. Unexpected: $($unexpected -join ', '); missing: $($missing -join ', ')."
}

[pscustomobject]@{
    artifactSetId = [string] $manifest.artifactSetId
    status = 'verified'
    groupCount = $allGroups.Count
    fileCount = $fileCount
    totalBytes = $totalBytes
    manifestSha256 = $trackedManifestSha256
    secondCopyVerified = [bool] $manifest.storage.separatelyBackedUpPrivateCopyVerified
    twoCopyRetentionCondition = [string] $manifest.storage.twoCopyRetentionCondition
}
