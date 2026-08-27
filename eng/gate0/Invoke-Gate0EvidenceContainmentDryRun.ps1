[CmdletBinding()]
param(
    [string] $ArtifactRoot,
    [string] $ProofRunId = '',
    [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'evidence/Gate0EvidenceContainment.psm1') -Force

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')).TrimEnd([IO.Path]::DirectorySeparatorChar)
$artifactRootResolved = if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) {
    [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetDirectoryName($repositoryRoot)) 'ReelForge.Gate0Artifacts')).TrimEnd([IO.Path]::DirectorySeparatorChar)
} else { [IO.Path]::GetFullPath($ArtifactRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) }
if ([string]::IsNullOrWhiteSpace($ProofRunId)) { $ProofRunId = "g05-stage2-containment-dry-run-$([DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))" }
Assert-Gate0EvidenceIdentifier $ProofRunId 'ProofRunId'
[void](Assert-Gate0LegacyEvidenceSeal $repositoryRoot -RequireEffective)

$source = "$artifactRootResolved.containment-dry-run-source-$([Guid]::NewGuid().ToString('N'))"
$resultSource = "$artifactRootResolved.containment-dry-run-result-source-$([Guid]::NewGuid().ToString('N'))"
$firstValidationPath = "$artifactRootResolved.stage2-validation-$ProofRunId-initial.json"
try {
    [IO.Directory]::CreateDirectory($source) | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'g0.5-stage2-containment-dry-run-contract.json') -Destination (Join-Path $source 'containment-contract.json')
    $probeText = "ReelForge Gate 0 no-media containment byte probe`ncontract=Gate0.G05.Stage2.ContainmentDryRun.V1`n"
    [IO.File]::WriteAllText((Join-Path $source 'no-media-byte-probe.txt'), $probeText, [Text.UTF8Encoding]::new($false))
    $append = & (Join-Path $PSScriptRoot 'Add-Gate0EvidenceShard.ps1') `
        -ArtifactRoot $artifactRootResolved `
        -SourceRoot $source `
        -ProofRunId $ProofRunId `
        -EvidenceGroupId 'g05-stage2-containment-dry-run' `
        -CellId 'containment-no-media-control' `
        -DestinationName "future/stage2/$ProofRunId" `
        -EvidenceBoundary 'containment-no-media' `
        -Disposition 'passed' `
        -ContractIdentity @(
            'repository:eng/gate0/g0.5-stage2-containment-dry-run-contract.json',
            'repository:eng/gate0/evidence/root-index.json'
        ) `
        -Provenance 'Owner-authorized Gate 0 no-media containment and R2 round-trip proof.' `
        -ProducerRuntimeIdentity @(
            'repository:eng/gate0/Invoke-Gate0EvidenceContainmentDryRun.ps1',
            'repository:eng/gate0/Add-Gate0EvidenceShard.ps1',
            'repository:eng/gate0/Test-Gate0EvidenceContainment.ps1'
        )
    $validation = & (Join-Path $PSScriptRoot 'Test-Gate0EvidenceContainment.ps1') -ArtifactRoot $artifactRootResolved -Remote -RequireEffectiveSeal -OutputPath $firstValidationPath

    [IO.Directory]::CreateDirectory($resultSource) | Out-Null
    Copy-Item -LiteralPath $firstValidationPath -Destination (Join-Path $resultSource 'initial-remote-validation.json')
    $retainedResult = [ordered]@{
        schemaVersion = 1
        resultId = 'Gate0.G05.Stage2.ContainmentDryRun.RetainedResult.V1'
        proofRunId = $ProofRunId
        completedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        initialShardPath = $append.shardPath
        initialShardSha256 = $append.shardSha256
        validatedRootIndexSha256 = $validation.rootIndexSha256
        localByteVerificationPerformed = $validation.localByteVerificationPerformed
        remoteByteVerificationPerformed = $validation.remoteByteVerificationPerformed
        remotelyVerifiedThisRun = $validation.remotelyVerifiedThisRun
        mediaProcessesInvoked = 0
        disposition = 'passed'
    }
    [IO.File]::WriteAllText((Join-Path $resultSource 'containment-dry-run-result.json'), (($retainedResult | ConvertTo-Json -Depth 16) + "`n"), [Text.UTF8Encoding]::new($false))
    $resultRunId = "$ProofRunId-result"
    $resultAppend = & (Join-Path $PSScriptRoot 'Add-Gate0EvidenceShard.ps1') `
        -ArtifactRoot $artifactRootResolved `
        -SourceRoot $resultSource `
        -ProofRunId $resultRunId `
        -EvidenceGroupId 'g05-stage2-containment-dry-run-result' `
        -CellId 'containment-no-media-result' `
        -DestinationName "future/stage2/$resultRunId" `
        -EvidenceBoundary 'containment-no-media' `
        -Disposition 'passed' `
        -ContractIdentity @('repository:eng/gate0/g0.5-stage2-containment-dry-run-contract.json') `
        -Provenance 'Retained result closure for the owner-authorized no-media containment proof.' `
        -ProducerRuntimeIdentity @(
            'repository:eng/gate0/Invoke-Gate0EvidenceContainmentDryRun.ps1',
            'repository:eng/gate0/Test-Gate0EvidenceContainment.ps1'
        )
    $validationPath = if ([string]::IsNullOrWhiteSpace($OutputPath)) { "$artifactRootResolved.stage2-validation-$ProofRunId-final.json" } else { [IO.Path]::GetFullPath($OutputPath) }
    $finalValidation = & (Join-Path $PSScriptRoot 'Test-Gate0EvidenceContainment.ps1') -ArtifactRoot $artifactRootResolved -Remote -RequireEffectiveSeal -OutputPath $validationPath
    [pscustomobject]@{
        schemaVersion = 1
        resultId = 'Gate0.G05.Stage2.ContainmentDryRun.Result.V1'
        proofRunId = $ProofRunId
        completedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        shardPath = $append.shardPath
        shardSha256 = $append.shardSha256
        resultShardPath = $resultAppend.shardPath
        resultShardSha256 = $resultAppend.shardSha256
        rootIndexSha256 = $finalValidation.rootIndexSha256
        logicalArtifactCount = $append.logicalArtifactCount
        logicalArtifactBytes = $append.logicalArtifactBytes
        localRetention = $append.localRetention
        r2Retention = $append.r2Retention
        remoteByteVerificationPerformed = $finalValidation.remoteByteVerificationPerformed
        remotelyVerifiedThisRun = $finalValidation.remotelyVerifiedThisRun
        mediaProcessesInvoked = 0
        disposition = 'passed'
    }
}
finally {
    if (Test-Path -LiteralPath $source -PathType Container) { Remove-Item -LiteralPath $source -Recurse -Force }
    if (Test-Path -LiteralPath $resultSource -PathType Container) { Remove-Item -LiteralPath $resultSource -Recurse -Force }
    if (Test-Path -LiteralPath $firstValidationPath -PathType Leaf) { Remove-Item -LiteralPath $firstValidationPath -Force }
}
