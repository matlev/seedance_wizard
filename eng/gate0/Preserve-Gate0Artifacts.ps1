[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ArtifactRoot,
    [Parameter(Mandatory)] [string] $P2Root,
    [Parameter(Mandatory)] [string] $FixtureRoot,
    [Parameter(Mandatory)] [string] $CorrectedProofRoot,
    [Parameter(Mandatory)] [string] $P3Root
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$expected = [ordered]@{
    p2ArchiveSha256 = 'D311C8C7B86E06B54588E442652F963BAE165BD4D8393E73CC9EBB445B025547'
    p3InstallerSha256 = '662761D8BA8DAE04AEC74023EBAECEB856C2B56B9B59CFD180759D26300DDA42'
    correctedEvidenceSha256 = 'F9D0A742F011BA19D1B7A30B547555D7DE7CC7A64B97F8294DD3CE828FFFD969'
    g04InputContractSha256 = 'FAD245D5664B49D52565834F01C0430E36CEFFEAB235A7E6BBA460AA5C599BD0'
    fixtureSourceInventorySha256 = 'EF53040D51229F25FA5C965E415DD62AA93E98623E36BE7CC9942DA2F4DC1595'
    generatedFixtureReportSha256 = 'C10C84827C7D45567EFB92506F86AB0EC8176A20B94D0AFC5E134D64D657D16F'
}

function Resolve-ExistingDirectory([string] $Path, [string] $Label) {
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "$Label does not exist or is not a directory: $Path"
    }

    return (Get-Item -LiteralPath $Path -Force).FullName
}

function Assert-NoReparsePoints([string] $Root, [string] $Label) {
    $reparsePoints = @((@((Get-Item -LiteralPath $Root -Force)) + @(Get-ChildItem -LiteralPath $Root -Force -Recurse)) | Where-Object {
        ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
    })
    if ($reparsePoints.Count -ne 0) {
        throw "$Label contains a reparse point and cannot be retained safely: $($reparsePoints[0].FullName)"
    }
}

function Get-Sha256([string] $Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Assert-Anchor([string] $Path, [string] $ExpectedSha256, [string] $Label) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label is missing: $Path"
    }

    $actual = Get-Sha256 $Path
    if ($actual -ne $ExpectedSha256) {
        throw "$Label hash mismatch. Expected $ExpectedSha256; observed $actual."
    }
}

function Convert-ToForwardSlash([string] $Path) {
    return $Path.Replace([IO.Path]::DirectorySeparatorChar, '/').Replace([IO.Path]::AltDirectorySeparatorChar, '/')
}

function Copy-VerifiedGroup(
    [string] $GroupId,
    [string] $SourceRoot,
    [string] $DestinationName,
    [string] $Provenance,
    [string[]] $IdentityReferences,
    [string[]] $LicenseReferences,
    [string[]] $ProofRunReferences,
    [string[]] $IncludeRelativePaths = @()
) {
    $destinationRoot = Join-Path $resolvedArtifactRoot $DestinationName
    [IO.Directory]::CreateDirectory($destinationRoot) | Out-Null
    $records = [Collections.Generic.List[object]]::new()
    $sourceFiles = if ($IncludeRelativePaths.Count -eq 0) {
        @(Get-ChildItem -LiteralPath $SourceRoot -Force -File -Recurse | Sort-Object FullName)
    }
    else {
        @($IncludeRelativePaths | ForEach-Object {
            $relative = $_.Replace('/', [IO.Path]::DirectorySeparatorChar)
            if ([IO.Path]::IsPathRooted($relative) -or $relative.StartsWith('..')) {
                throw "Unsafe included source path in ${GroupId}: $_"
            }
            $path = [IO.Path]::GetFullPath((Join-Path $SourceRoot $relative))
            if (-not $path.StartsWith("$SourceRoot$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase) -or
                -not (Test-Path -LiteralPath $path -PathType Leaf)) {
                throw "Included source file is missing or escaped its root in ${GroupId}: $_"
            }
            Get-Item -LiteralPath $path -Force
        } | Sort-Object FullName)
    }

    foreach ($sourceFile in $sourceFiles) {
        $relative = [IO.Path]::GetRelativePath($SourceRoot, $sourceFile.FullName)
        if ([IO.Path]::IsPathRooted($relative) -or $relative.StartsWith('..')) {
            throw "Unsafe relative source path in ${GroupId}: $relative"
        }

        $destination = [IO.Path]::GetFullPath((Join-Path $destinationRoot $relative))
        if (-not $destination.StartsWith("$destinationRoot$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase)) {
            throw "Destination escaped the artifact group root in ${GroupId}: $relative"
        }

        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
        $sourceSha256 = Get-Sha256 $sourceFile.FullName
        Copy-Item -LiteralPath $sourceFile.FullName -Destination $destination
        $destinationItem = Get-Item -LiteralPath $destination -Force
        $destinationSha256 = Get-Sha256 $destination
        if ($destinationItem.Length -ne $sourceFile.Length -or $destinationSha256 -ne $sourceSha256) {
            throw "Retained-copy verification failed in ${GroupId}: $relative"
        }

        $retainedRelative = Convert-ToForwardSlash ([IO.Path]::GetRelativePath($resolvedArtifactRoot, $destination))
        $records.Add([ordered]@{
            artifactId = "$GroupId/$((Convert-ToForwardSlash $relative))"
            filename = $retainedRelative
            size = [int64] $destinationItem.Length
            sha256 = $destinationSha256
        })
    }

    return [ordered]@{
        groupId = $GroupId
        provenance = $Provenance
        producerRuntimeIdentity = $IdentityReferences
        licenseRecords = $LicenseReferences
        proofRunIdentity = $ProofRunReferences
        fileCount = $records.Count
        totalBytes = [int64] (($records | ForEach-Object { [int64] $_['size'] } | Measure-Object -Sum).Sum)
        files = @($records)
    }
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..')).TrimEnd([IO.Path]::DirectorySeparatorChar)
$resolvedManifestPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'artifact-retention-manifest.json'))
$approvedArtifactRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetDirectoryName($repositoryRoot)) 'ReelForge.Gate0Artifacts')).TrimEnd([IO.Path]::DirectorySeparatorChar)
$candidateArtifactRoot = [IO.Path]::GetFullPath($ArtifactRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
if (-not $candidateArtifactRoot.Equals($approvedArtifactRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "The artifact root must be the approved repository sibling: $approvedArtifactRoot"
}
if (Test-Path -LiteralPath $candidateArtifactRoot) {
    throw 'The first interim-retention run requires a new artifact root; refusing to merge with or overwrite an existing directory.'
}

$resolvedP2Root = Resolve-ExistingDirectory $P2Root 'P2 source root'
$resolvedFixtureRoot = Resolve-ExistingDirectory $FixtureRoot 'Fixture source root'
$resolvedCorrectedProofRoot = Resolve-ExistingDirectory $CorrectedProofRoot 'Corrected proof source root'
$resolvedP3Root = Resolve-ExistingDirectory $P3Root 'P3 source root'

foreach ($source in @(
    @{ Path = $resolvedP2Root; Label = 'P2 source root' },
    @{ Path = $resolvedFixtureRoot; Label = 'Fixture source root' },
    @{ Path = $resolvedCorrectedProofRoot; Label = 'Corrected proof source root' },
    @{ Path = $resolvedP3Root; Label = 'P3 source root' },
    @{ Path = $PSScriptRoot; Label = 'Repository contract source root' }
)) {
    Assert-NoReparsePoints $source.Path $source.Label
}

$p2Archive = Join-Path $resolvedP2Root 'ffmpeg-n8.1.2-44-g7c533d0f86-win64-lgpl-shared-8.1.zip'
$p3Installer = Join-Path $resolvedP3Root 'libjpeg-turbo-3.2.0-vc-x64.exe'
$correctedEvidence = Join-Path $resolvedCorrectedProofRoot 'g0.4-input-proof-evidence.json'
Assert-Anchor $p2Archive $expected.p2ArchiveSha256 'Exact approved P2 archive'
Assert-Anchor $p3Installer $expected.p3InstallerSha256 'Exact approved P3 installer'
Assert-Anchor $correctedEvidence $expected.correctedEvidenceSha256 'Corrected G0.4 evidence'
Assert-Anchor (Join-Path $PSScriptRoot 'g0.4-input-proof-contract.json') $expected.g04InputContractSha256 'Exact G0.4 input contract snapshot'
Assert-Anchor (Join-Path $PSScriptRoot 'fixture-source-inventory.json') $expected.fixtureSourceInventorySha256 'Exact fixture source inventory snapshot'
Assert-Anchor (Join-Path $resolvedFixtureRoot 'generated-fixture-report.json') $expected.generatedFixtureReportSha256 'Exact generated fixture report'

[IO.Directory]::CreateDirectory($candidateArtifactRoot) | Out-Null
$resolvedArtifactRoot = (Get-Item -LiteralPath $candidateArtifactRoot -Force).FullName
Assert-NoReparsePoints $resolvedArtifactRoot 'Approved artifact root'

$p3Signature = Get-AuthenticodeSignature -LiteralPath $p3Installer
if ($p3Signature.Status -ne [Management.Automation.SignatureStatus]::Valid) {
    throw "The retained P3 installer no longer has a valid Authenticode signature: $($p3Signature.Status)"
}

$groupSpecifications = @(
    @{
        GroupId = 'P2.BtbnLgplShared.WindowsX64.20260820'
        SourceRoot = $resolvedP2Root
        DestinationName = 'p2'
        Provenance = 'Exact content-pinned BtbN daily LGPL shared Windows x64 archive and extracted proof runtime.'
        IdentityReferences = @('repository:eng/gate0/manifests/p2-btbn-lgplv3-shared-windows-x64-20260820.json')
        LicenseReferences = @('artifact:p2/runtime/ffmpeg-n8.1.2-44-g7c533d0f86-win64-lgpl-shared-8.1/LICENSE.txt')
        ProofRunReferences = @('artifact:proofs/g0.4-input-corrected/g0.4-input-proof-evidence.json')
    },
    @{
        GroupId = 'Gate0.Fixtures.F1-F8.20260824'
        SourceRoot = $resolvedFixtureRoot
        DestinationName = 'fixtures'
        Provenance = 'Repository-authored deterministic Gate 0 fixture sources and their generated fixture report.'
        IdentityReferences = @('artifact:fixtures/fixture-manifest.json', 'artifact:fixtures/generated-fixture-report.json')
        LicenseReferences = @('artifact:contracts/artifacts/fonts/licenses/NotoSans-OFL.txt', 'artifact:contracts/artifacts/fonts/licenses/NotoSansArabic-OFL.txt', 'artifact:contracts/artifacts/fonts/licenses/NotoSansCJKsc-OFL.txt')
        ProofRunReferences = @('artifact:fixtures/generated-fixture-report.json')
    },
    @{
        GroupId = 'Gate0.G04.Input.Corrected.20260825'
        SourceRoot = $resolvedCorrectedProofRoot
        DestinationName = 'proofs/g0.4-input-corrected'
        Provenance = 'Fresh corrected 256-row G0.4 input proof closure, including generated media, command records, logs, and runtime identity.'
        IdentityReferences = @('artifact:proofs/g0.4-input-corrected/runtime-identity.json')
        LicenseReferences = @('artifact:p2/runtime/ffmpeg-n8.1.2-44-g7c533d0f86-win64-lgpl-shared-8.1/LICENSE.txt')
        ProofRunReferences = @('artifact:proofs/g0.4-input-corrected/g0.4-input-proof-evidence.json')
    },
    @{
        GroupId = 'P3.LibjpegTurboCjpeg.WindowsX64.3.2.0'
        SourceRoot = $resolvedP3Root
        DestinationName = 'p3/libjpeg-turbo-3.2.0'
        Provenance = 'Official libjpeg-turbo 3.2.0 Windows x64 installer and extracted fixture-producer closure.'
        IdentityReferences = @('upstream:https://github.com/libjpeg-turbo/libjpeg-turbo/releases/tag/3.2.0', 'manifest:p3Authenticode')
        LicenseReferences = @('artifact:p3/libjpeg-turbo-3.2.0/extracted/doc/LICENSE.md', 'artifact:p3/libjpeg-turbo-3.2.0/extracted/doc/README.ijg')
        ProofRunReferences = @('manifest:p3-proof-status-incomplete')
    },
    @{
        GroupId = 'Gate0.RepositoryContracts.20260825'
        SourceRoot = $PSScriptRoot
        DestinationName = 'contracts'
        Provenance = 'Exact repository contract and provenance snapshots bound to the retained G0.4 corpus.'
        IdentityReferences = @('artifact:contracts/g0.4-input-proof-contract.json', 'artifact:contracts/fixture-source-inventory.json', 'artifact:contracts/manifests/p2-btbn-lgplv3-shared-windows-x64-20260820.json', 'artifact:contracts/font-proof-artifacts.json')
        LicenseReferences = @('artifact:contracts/artifacts/fonts/licenses/NotoSans-OFL.txt', 'artifact:contracts/artifacts/fonts/licenses/NotoSansArabic-OFL.txt', 'artifact:contracts/artifacts/fonts/licenses/NotoSansCJKsc-OFL.txt')
        ProofRunReferences = @('artifact:proofs/g0.4-input-corrected/g0.4-input-proof-evidence.json')
        IncludeRelativePaths = @(
            'g0.4-input-proof-contract.json',
            'fixture-source-inventory.json',
            'fixture-manifest.json',
            'expected-truths.json',
            'font-proof-artifacts.json',
            'manifests/p2-btbn-lgplv3-shared-windows-x64-20260820.json',
            'artifacts/fonts/README.md',
            'artifacts/fonts/NotoSans-Regular.ttf',
            'artifacts/fonts/NotoSansArabic-Regular.ttf',
            'artifacts/fonts/NotoSansCJKsc-Regular.otf',
            'artifacts/fonts/licenses/NotoSans-OFL.txt',
            'artifacts/fonts/licenses/NotoSansArabic-OFL.txt',
            'artifacts/fonts/licenses/NotoSansCJKsc-OFL.txt'
        )
    }
)
$groups = @($groupSpecifications | ForEach-Object { Copy-VerifiedGroup @_ })
Assert-NoReparsePoints $resolvedArtifactRoot 'Completed artifact root'

$manifest = [ordered]@{
    schemaVersion = 1
    artifactSetId = 'Gate0.InterimCorpus.20260825'
    generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    storage = [ordered]@{
        rootName = 'ReelForge.Gate0Artifacts'
        pathPolicy = 'Project-controlled sibling directory; manifest paths are relative and machine-independent.'
        classification = 'local-working-copy-r2-retention-pending'
        productionArtifactRepository = $false
        hostedCiEligible = $false
        heavyProofMode = 'manual-or-opt-in'
        separatelyBackedUpPrivateCopyVerified = $false
        twoCopyRetentionCondition = 'incomplete'
        secondCopyBlocker = 'The dedicated private reelforge-artifacts R2 bucket is configured as the durable target, but SecretStore credentials and independently verified remote bytes are not yet present.'
        temporaryProviderR2Permitted = $false
    }
    anchors = $expected
    p3Authenticode = [ordered]@{
        status = [string] $p3Signature.Status
        signerSubject = [string] $p3Signature.SignerCertificate.Subject
        signerThumbprint = [string] $p3Signature.SignerCertificate.Thumbprint
        timestampSubject = [string] $p3Signature.TimeStamperCertificate.Subject
        timestampThumbprint = [string] $p3Signature.TimeStamperCertificate.Thumbprint
    }
    groups = $groups
    totals = [ordered]@{
        groupCount = $groups.Count
        fileCount = (@($groups | ForEach-Object { @($_['files']) })).Count
        totalBytes = [int64] (($groups | ForEach-Object { [int64] $_['totalBytes'] } | Measure-Object -Sum).Sum)
    }
    limitations = @(
        'This manifest proves one verified local retained copy only.',
        'It does not select or approve a shipping runtime or public-distribution component.',
        'P3 fixture-production proof and cleanup remain incomplete at initial retention.',
        'Unattended hosted CI must not depend on this machine-local corpus.'
    )
}

$manifestJson = $manifest | ConvertTo-Json -Depth 12
$temporaryManifestPath = "$resolvedManifestPath.tmp-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.File]::WriteAllText($temporaryManifestPath, $manifestJson, [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($temporaryManifestPath, $resolvedManifestPath, $true)
}
finally {
    if (Test-Path -LiteralPath $temporaryManifestPath -PathType Leaf) {
        Remove-Item -LiteralPath $temporaryManifestPath -Force
    }
}

$manifestHash = Get-Sha256 $resolvedManifestPath
$localManifestCopy = Join-Path $resolvedArtifactRoot 'artifact-retention-manifest.json'
Copy-Item -LiteralPath $resolvedManifestPath -Destination $localManifestCopy
if ((Get-Sha256 $localManifestCopy) -ne $manifestHash) {
    throw 'The local retained manifest copy failed hash verification.'
}

[pscustomobject]@{
    artifactSetId = $manifest.artifactSetId
    artifactRoot = $resolvedArtifactRoot
    trackedManifest = $resolvedManifestPath
    manifestSha256 = $manifestHash
    fileCount = $manifest.totals.fileCount
    totalBytes = $manifest.totals.totalBytes
    secondCopyVerified = $false
}
