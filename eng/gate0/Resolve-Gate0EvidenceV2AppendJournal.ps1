[CmdletBinding()]
param([Parameter(Mandatory)][string] $ArtifactRoot, [switch] $Remote)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'evidence/Gate0EvidenceContainmentV2.psm1') -Force
if ($Remote) { Import-Module (Join-Path $PSScriptRoot 'Gate0ArtifactTools.psm1') -Force }

function Assert-JournalStagedArtifact($Artifact, [string] $PayloadRoot) {
    Assert-Gate0EvidenceV2ExactProperties $Artifact @('artifactId','relativePath','byteSize','sha256','r2ObjectKey','purpose') 'V2 staged journal artifact'
    Assert-Gate0EvidenceV2Identifier ([string]$Artifact.artifactId) 'V2 staged journal artifactId'
    Assert-Gate0EvidenceV2RelativePath ([string]$Artifact.relativePath) 'V2 staged journal artifact path'
    if (-not ([string]$Artifact.relativePath).StartsWith($PayloadRoot, [StringComparison]::Ordinal) -or [int64]$Artifact.byteSize -lt 0 -or [string]$Artifact.sha256 -notmatch '^[A-F0-9]{64}$') { throw 'V2 staged journal artifact identity is invalid.' }
    $expectedKey = "objects/sha256/$(([string]$Artifact.sha256).Substring(0,2).ToLowerInvariant())/$(([string]$Artifact.sha256).ToLowerInvariant())"
    if ($Artifact.r2ObjectKey -ne $expectedKey) { throw 'V2 staged journal object key is invalid.' }
}

function Assert-JournalReceiptArtifact($Artifact, [string] $PayloadRoot) {
    Assert-Gate0EvidenceV2ExactProperties $Artifact @('artifactId','relativePath','byteSize','sha256','r2ObjectKey','purpose','retentionStatus','transferDisposition','remotelyVerifiedUtc') 'V2 verified journal artifact'
    $staged = [pscustomobject][ordered]@{ artifactId=$Artifact.artifactId;relativePath=$Artifact.relativePath;byteSize=$Artifact.byteSize;sha256=$Artifact.sha256;r2ObjectKey=$Artifact.r2ObjectKey;purpose=$Artifact.purpose }
    Assert-JournalStagedArtifact $staged $PayloadRoot
    if ($Artifact.retentionStatus -ne 'remote-verified' -or [string]$Artifact.transferDisposition -notin @('uploaded-and-verified','existing-object-verified','concurrent-create-verified','deduplicated-object-verified-in-this-run','independently-retrieved-and-verified')) { throw 'V2 verified journal artifact receipt is invalid.' }
    $verified = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse([string]$Artifact.remotelyVerifiedUtc, [ref]$verified)) { throw 'V2 verified journal artifact timestamp is invalid.' }
}

function Get-ArtifactMap($Artifacts, [string] $PayloadRoot, [switch] $Receipts) {
    $map = @{}
    foreach ($artifact in @($Artifacts)) {
        if ($Receipts) { Assert-JournalReceiptArtifact $artifact $PayloadRoot } else { Assert-JournalStagedArtifact $artifact $PayloadRoot }
        $key = ([string]$artifact.relativePath).ToLowerInvariant()
        if ($map.ContainsKey($key)) { throw 'V2 journal contains duplicate logical artifact paths.' }
        $map[$key] = $artifact
    }
    return $map
}

function Assert-CoreArtifactEquality($Left, $Right, [string] $Label) {
    foreach ($name in @('artifactId','relativePath','sha256','r2ObjectKey','purpose')) {
        if ([string]$Left.$name -cne [string]$Right.$name) { throw "$Label differs at $name." }
    }
    if ([int64]$Left.byteSize -ne [int64]$Right.byteSize) { throw "$Label differs at byteSize." }
}

function Assert-ReceiptArtifactEquality($Left, $Right, [string] $Label) {
    Assert-CoreArtifactEquality $Left $Right $Label
    foreach ($name in @('retentionStatus','transferDisposition','remotelyVerifiedUtc')) {
        if ([string]$Left.$name -cne [string]$Right.$name) { throw "$Label differs at $name." }
    }
}

function Assert-ExactJournalClosure($Journal, $Shard) {
    $staged = Get-ArtifactMap $Journal.stagedArtifacts ([string]$Journal.payloadRoot)
    $receipts = Get-ArtifactMap $Journal.artifacts ([string]$Journal.payloadRoot) -Receipts
    $shardMap = @{}
    foreach ($artifact in @($Shard.Manifest.artifacts)) {
        $key = ([string]$artifact.relativePath).ToLowerInvariant()
        if ($shardMap.ContainsKey($key)) { throw 'V2 shard contains duplicate logical artifact paths.' }
        $shardMap[$key] = $artifact
    }
    if ($staged.Count -ne $receipts.Count -or $receipts.Count -ne $shardMap.Count -or $receipts.Count -ne [int]$Journal.artifactCount) { throw 'V2 journal and shard artifact counts differ.' }
    foreach ($key in $staged.Keys) {
        if (-not $receipts.ContainsKey($key) -or -not $shardMap.ContainsKey($key)) { throw 'V2 journal and shard logical artifact sets differ.' }
        Assert-CoreArtifactEquality $staged[$key] $receipts[$key] 'V2 staged and verified artifact'
        Assert-ReceiptArtifactEquality $receipts[$key] $shardMap[$key] 'V2 journal and shard artifact'
    }
    $bytes = [int64](($receipts.Values | Measure-Object -Property byteSize -Sum).Sum)
    if ($bytes -ne [int64]$Journal.artifactBytes -or $bytes -ne [int64]$Shard.Manifest.totals.logicalArtifactBytes -or $receipts.Count -ne [int]$Shard.Manifest.totals.logicalArtifactCount) { throw 'V2 journal and shard artifact totals differ.' }
}

function Assert-PhysicalClosure($Artifacts, [string] $Base, [string] $PayloadRoot, [string] $ContainmentRoot) {
    Assert-Gate0EvidenceV2NoReparsePointAncestors $Base $ContainmentRoot
    foreach ($item in @((Get-Item -LiteralPath $Base -Force)) + @(Get-ChildItem -LiteralPath $Base -Force -Recurse)) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "V2 recovery refuses a reparse point in transaction-owned state: $($item.FullName)" }
    }
    $expected = @{}
    foreach ($artifact in @($Artifacts)) {
        $relative = ([string]$artifact.relativePath).Substring($PayloadRoot.Length)
        $key = $relative.Replace('\','/').ToLowerInvariant()
        if ($expected.ContainsKey($key)) { throw 'V2 recovery artifact closure contains duplicate paths.' }
        $expected[$key] = $artifact
    }
    $actual = @{}
    foreach ($file in @(Get-ChildItem -LiteralPath $Base -File -Force -Recurse)) {
        $key = [IO.Path]::GetRelativePath($Base, $file.FullName).Replace('\','/').ToLowerInvariant()
        if ($actual.ContainsKey($key)) { throw 'V2 physical closure contains duplicate paths.' }
        $actual[$key] = $file
    }
    if ($expected.Count -ne $actual.Count) { throw 'V2 physical artifact closure contains an unindexed or missing file.' }
    foreach ($key in $expected.Keys) {
        if (-not $actual.ContainsKey($key)) { throw 'V2 physical artifact closure contains an unindexed or missing file.' }
        $artifact = $expected[$key]; $file = $actual[$key]
        if ([int64]$file.Length -ne [int64]$artifact.byteSize -or (Get-Gate0EvidenceV2Sha256 $file.FullName) -ne [string]$artifact.sha256) { throw 'V2 physical artifact closure differs from the journal.' }
    }
}

$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')).TrimEnd([IO.Path]::DirectorySeparatorChar)
$artifactRoot = [IO.Path]::GetFullPath($ArtifactRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
if ([IO.Path]::GetDirectoryName($repo) -ne [IO.Path]::GetDirectoryName($artifactRoot)) { throw 'V2 artifact root must be a sibling of the repository.' }
Assert-Gate0EvidenceV2NoReparsePointAncestors $repo ([IO.Path]::GetDirectoryName($repo))
Assert-Gate0EvidenceV2NoReparsePointAncestors $artifactRoot ([IO.Path]::GetDirectoryName($artifactRoot))

$journalPath = "$artifactRoot.stage2-v2-append-journal.json"
$rootPath = Join-Path $PSScriptRoot 'evidence/v2/root-index.json'
$lockPath = "$artifactRoot.stage2-v2-append-lock"
$lock = $null
try {
    if ((Test-Path -LiteralPath $lockPath) -and ((Get-Item -LiteralPath $lockPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'V2 recovery lock is a reparse point.' }
    $lock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    if (-not (Test-Path -LiteralPath $journalPath -PathType Leaf)) { throw 'No V2 append journal requires recovery.' }
    Assert-Gate0EvidenceV2NoReparsePointAncestors $journalPath ([IO.Path]::GetDirectoryName($artifactRoot))
    $text = Get-Content -LiteralPath $journalPath -Raw
    Assert-Gate0EvidenceV2MetadataText $text 'V2 append journal'
    $journal = $text | ConvertFrom-Json -Depth 64
    if ($journal.phase -eq 'prepared') {
        $expected = @('schemaVersion','journalId','proofRunId','phase','oldRootIndexSha256','payloadRoot','stagingDirectoryName','artifactCount','artifactBytes','stagedArtifacts')
    } elseif ($journal.phase -eq 'remote-verified') {
        $expected = @('schemaVersion','journalId','proofRunId','phase','oldRootIndexSha256','candidateRootIndexSha256','shardPath','shardSha256','payloadRoot','stagingDirectoryName','artifactCount','artifactBytes','stagedArtifacts','artifacts')
    } else { throw 'V2 journal phase is invalid.' }
    Assert-Gate0EvidenceV2ExactProperties $journal $expected 'V2 journal'
    if ($journal.schemaVersion -ne 1 -or $journal.journalId -ne 'Gate0.Stage2Evidence.V2.AppendJournal.V1' -or $journal.oldRootIndexSha256 -notmatch '^[A-F0-9]{64}$') { throw 'V2 journal identity is invalid.' }
    Assert-Gate0EvidenceV2Identifier ([string]$journal.proofRunId) 'V2 journal proof run'
    if ($journal.payloadRoot -ne "future/stage2/v2/$($journal.proofRunId)/" -or $journal.stagingDirectoryName -notmatch '^.+\.stage2-v2-staging-[0-9a-f]{32}$') { throw 'V2 journal ownership is invalid.' }
    $stagedMap = Get-ArtifactMap $journal.stagedArtifacts ([string]$journal.payloadRoot)
    $stagedBytes = [int64](($stagedMap.Values | Measure-Object -Property byteSize -Sum).Sum)
    if ($stagedMap.Count -ne [int]$journal.artifactCount -or $stagedBytes -ne [int64]$journal.artifactBytes) { throw 'V2 staged journal totals are invalid.' }

    $root = Read-Gate0EvidenceV2RootIndex $rootPath
    $staging = Join-Path ([IO.Path]::GetDirectoryName($artifactRoot)) ([string]$journal.stagingDirectoryName)
    $payload = Join-Path $artifactRoot (($journal.payloadRoot.TrimEnd('/')).Replace('/','\'))
    if ($journal.phase -eq 'prepared') {
        if ($root.Sha256 -ne $journal.oldRootIndexSha256) { throw 'Prepared V2 journal cannot roll back from an unexpected root.' }
        if (Test-Path -LiteralPath $payload) { throw 'Prepared V2 journal unexpectedly has a payload.' }
        if (Test-Path -LiteralPath $staging) {
            Assert-PhysicalClosure $journal.stagedArtifacts $staging ([string]$journal.payloadRoot) ([IO.Path]::GetDirectoryName($artifactRoot))
            Remove-Item -LiteralPath $staging -Recurse -Force
        }
        Remove-Item -LiteralPath $journalPath -Force
        [pscustomobject]@{ disposition='prepared-state-rolled-back'; mediaProcessesInvoked=0 }
        return
    }

    if ($journal.candidateRootIndexSha256 -notmatch '^[A-F0-9]{64}$' -or $journal.shardSha256 -notmatch '^[A-F0-9]{64}$' -or $journal.shardPath -ne "stage2/$($journal.proofRunId).manifest.json") { throw 'Remote-verified V2 journal is invalid.' }
    $receiptMap = Get-ArtifactMap $journal.artifacts ([string]$journal.payloadRoot) -Receipts
    if ($receiptMap.Count -ne $stagedMap.Count) { throw 'V2 prepared and verified journal artifact counts differ.' }
    foreach ($key in $stagedMap.Keys) {
        if (-not $receiptMap.ContainsKey($key)) { throw 'V2 prepared and verified journal artifact sets differ.' }
        Assert-CoreArtifactEquality $stagedMap[$key] $receiptMap[$key] 'V2 prepared and verified journal artifact'
    }
    $shardPath = Join-Path (Split-Path -Parent $rootPath) (($journal.shardPath).Replace('/','\'))
    if ($root.Sha256 -eq $journal.candidateRootIndexSha256) {
        if (-not $Remote) { throw 'Accepted-root journal clearing requires explicit independent remote verification.' }
        $run = @($root.Index.runs | Where-Object { $_.proofRunId -eq $journal.proofRunId })
        if ($run.Count -ne 1 -or $run[0].shardSha256 -ne $journal.shardSha256) { throw 'Accepted V2 root does not bind the journal transaction.' }
        $shard = Read-Gate0EvidenceV2Shard $shardPath
        if ($shard.Sha256 -ne $journal.shardSha256) { throw 'Accepted V2 shard differs from the journal.' }
        Assert-ExactJournalClosure $journal $shard
        Assert-PhysicalClosure $journal.artifacts $payload ([string]$journal.payloadRoot) $artifactRoot
        $bundle = New-Gate0R2ClientBundle
        try {
            foreach ($artifact in @($journal.artifacts)) {
                Invoke-Gate0RemoteByteVerification $bundle ([pscustomobject]@{ ArtifactId=$artifact.artifactId; ObjectKey=$artifact.r2ObjectKey; Size=$artifact.byteSize; Sha256=$artifact.sha256 })
            }
        } finally { $bundle.HttpClient.Dispose() }
        Remove-Item -LiteralPath $journalPath -Force
        [pscustomobject]@{ disposition='accepted-root-journal-cleared'; mediaProcessesInvoked=0 }
        return
    }

    if ($root.Sha256 -ne $journal.oldRootIndexSha256) { throw 'V2 recovery cannot roll back from an unexpected root.' }
    if (Test-Path -LiteralPath $shardPath -PathType Leaf) {
        Assert-Gate0EvidenceV2NoReparsePointAncestors $shardPath $repo
        $shard = Read-Gate0EvidenceV2Shard $shardPath
        if ($shard.Sha256 -ne $journal.shardSha256) { throw 'V2 recovery refuses an unowned shard.' }
        Assert-ExactJournalClosure $journal $shard
    }
    if (Test-Path -LiteralPath $payload) { Assert-PhysicalClosure $journal.artifacts $payload ([string]$journal.payloadRoot) $artifactRoot }
    if (Test-Path -LiteralPath $staging) { Assert-PhysicalClosure $journal.stagedArtifacts $staging ([string]$journal.payloadRoot) ([IO.Path]::GetDirectoryName($artifactRoot)) }
    if (Test-Path -LiteralPath $shardPath -PathType Leaf) { Assert-Gate0EvidenceV2NoReparsePointAncestors $shardPath $repo; Remove-Item -LiteralPath $shardPath -Force }
    if (Test-Path -LiteralPath $payload) { Assert-PhysicalClosure $journal.artifacts $payload ([string]$journal.payloadRoot) $artifactRoot; Remove-Item -LiteralPath $payload -Recurse -Force }
    if (Test-Path -LiteralPath $staging) { Assert-PhysicalClosure $journal.stagedArtifacts $staging ([string]$journal.payloadRoot) ([IO.Path]::GetDirectoryName($artifactRoot)); Remove-Item -LiteralPath $staging -Recurse -Force }
    Remove-Item -LiteralPath $journalPath -Force
    [pscustomobject]@{ disposition='remote-verified-state-rolled-back'; mediaProcessesInvoked=0 }
} finally {
    if ($lock) { $lock.Dispose() }
}
