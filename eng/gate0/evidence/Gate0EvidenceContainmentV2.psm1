Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RootIndexId = 'Gate0.Stage2Evidence.Root.V1'
$script:ShardSchemaId = 'Gate0.Stage2Evidence.Shard.V1'
$script:SealId = 'Gate0.LegacyEvidenceSeal.20260827'
$script:ExpectedSourceSha256 = 'AE088727059D3686930C4422237A02E6691580D93C85E3862489C8F65FCDD0A0'
$script:ExpectedDurableSha256 = 'AF9B368D44FDE3EFD2C45E2D847CB989D38E52066607A0D3E61384588D23C113'
$script:ExpectedInitialRootIndexSha256 = '146936D12F54D0DC6D324F51330445E1B9F07C2C0DF13575F4EA0EB7C8643126'
$script:ExpectedLogicalArtifactCount = 4101
$script:ExpectedLogicalArtifactBytes = [int64]1121540509

function Get-Gate0EvidenceSha256([string] $Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-Gate0EvidenceTextSha256([string] $Text) {
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Text)))
}

function ConvertTo-Gate0CanonicalJson($Value) {
    if ($null -eq $Value) { return 'null' }
    if ($Value -is [string] -or $Value -is [ValueType]) { return ($Value | ConvertTo-Json -Compress) }
    if ($Value -is [Collections.IDictionary]) {
        $pairs = foreach ($key in @($Value.Keys | Sort-Object)) {
            ($key | ConvertTo-Json -Compress) + ':' + (ConvertTo-Gate0CanonicalJson $Value[$key])
        }
        return '{' + ($pairs -join ',') + '}'
    }
    if ($Value -is [Collections.IEnumerable]) {
        return '[' + ((@($Value | ForEach-Object { ConvertTo-Gate0CanonicalJson $_ }) -join ',')) + ']'
    }
    $properties = foreach ($property in @($Value.PSObject.Properties | Sort-Object Name)) {
        ($property.Name | ConvertTo-Json -Compress) + ':' + (ConvertTo-Gate0CanonicalJson $property.Value)
    }
    return '{' + ($properties -join ',') + '}'
}

function Write-Gate0EvidenceUtf8Atomic([string] $Path, [string] $Text) {
    $temporary = "$Path.tmp-$([Guid]::NewGuid().ToString('N'))"
    try {
        [IO.File]::WriteAllText($temporary, $Text, [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($temporary, $Path, $true)
    }
    finally {
        if (Test-Path -LiteralPath $temporary -PathType Leaf) { Remove-Item -LiteralPath $temporary -Force }
    }
}

function Assert-Gate0EvidenceExactProperties($Value, [string[]] $Expected, [string] $Label) {
    if ($null -eq $Value) { throw "$Label is missing." }
    $actual = @($Value.PSObject.Properties.Name | Sort-Object)
    $wanted = @($Expected | Sort-Object)
    if (@(Compare-Object -ReferenceObject $wanted -DifferenceObject $actual).Count -ne 0) {
        throw "$Label does not match the closed evidence schema."
    }
}

function Assert-Gate0EvidenceIdentifier([string] $Value, [string] $Label) {
    if ($Value -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$') { throw "$Label is not a bounded portable identifier." }
}

function Assert-Gate0EvidenceRelativePath([string] $Value, [string] $Label) {
    if ([string]::IsNullOrWhiteSpace($Value) -or [IO.Path]::IsPathRooted($Value) -or $Value.Contains('\')) {
        throw "$Label must be a portable relative path."
    }
    foreach ($segment in $Value.Split('/')) {
        if ($segment -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$' -or $segment -in @('.', '..')) {
            throw "$Label contains an unsafe path segment."
        }
    }
}

function Assert-Gate0EvidenceNoReparsePointAncestors([string] $Path, [string] $StopAt) {
    $current = Get-Item -LiteralPath $Path -Force
    $stop = [IO.Path]::GetFullPath($StopAt).TrimEnd([IO.Path]::DirectorySeparatorChar)
    while ($null -ne $current) {
        if (($current.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Gate 0 evidence path contains a reparse point: $($current.FullName)"
        }
        if ($current.FullName.TrimEnd([IO.Path]::DirectorySeparatorChar).Equals($stop, [StringComparison]::OrdinalIgnoreCase)) { return }
        $current = if ($current -is [IO.DirectoryInfo]) { $current.Parent } else { $current.Directory }
    }
    throw 'Gate 0 evidence path escaped its approved root.'
}

function Assert-Gate0EvidenceMetadataText([string] $Text, [string] $Label) {
    $prohibited = @(
        '(?i)[A-Z]:[\\/]',
        '\\\\',
        '(?i)(^|[^A-Za-z])(AppData|OneDrive|Users)[\\/]',
        '(?i)https?://',
        '(?i)(X-Amz-|[?&](sig|signature|token|key|credential)=)',
        '(?i)(secretaccesskey|accesskeyid|authorization\s*:|bearer\s+|password\s*=|credential\s*=)',
        '(?i)r2\.cloudflarestorage\.com',
        '(?i)file://'
    )
    foreach ($pattern in $prohibited) {
        if ($Text -match $pattern) { throw "$Label contains prohibited machine-local, endpoint, signed-query, or credential material." }
    }
}

function Get-Gate0EvidenceFileShape([string] $Path) {
    $bytes = (Get-Item -LiteralPath $Path -Force).Length
    $lines = @([IO.File]::ReadLines($Path)).Count
    return [pscustomobject]@{ Bytes = [int64]$bytes; Lines = [int]$lines }
}

function Get-Gate0EvidenceEntryHash($Entry) {
    $copy = [ordered]@{}
    foreach ($property in @($Entry.PSObject.Properties)) {
        if ($property.Name -ne 'entrySha256') { $copy[$property.Name] = $property.Value }
    }
    return Get-Gate0EvidenceTextSha256 (ConvertTo-Gate0CanonicalJson $copy)
}

function Read-Gate0EvidenceShard([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Evidence shard is missing: $Path" }
    Assert-Gate0EvidenceNoReparsePointAncestors $Path (Split-Path -Parent $Path)
    $text = Get-Content -LiteralPath $Path -Raw
    Assert-Gate0EvidenceMetadataText $text 'Evidence shard'
    $shape = Get-Gate0EvidenceFileShape $Path
    if ($shape.Bytes -gt 65536 -or $shape.Lines -gt 300) { throw 'Evidence shard exceeds its 64 KiB or 300-line cap.' }
    $shard = $text | ConvertFrom-Json -Depth 64
    Assert-Gate0EvidenceExactProperties $shard @(
        'schemaVersion','shardId','proofRunId','evidenceGroupId','cellId','evidenceBoundary','createdUtc',
        'contractIdentity','provenance','producerRuntimeIdentity','licenseRecords','artifacts','attempts','disposition','localRetention','r2Retention','totals','limitations'
    ) 'Evidence shard'
    if ($shard.schemaVersion -ne 1 -or $shard.shardId -ne $script:ShardSchemaId) { throw 'Unsupported evidence shard schema.' }
    foreach ($pair in @(@($shard.proofRunId,'proofRunId'),@($shard.evidenceGroupId,'evidenceGroupId'),@($shard.cellId,'cellId'))) {
        Assert-Gate0EvidenceIdentifier ([string]$pair[0]) ([string]$pair[1])
    }
    if ([string]$shard.evidenceBoundary -notin @('containment-no-media','p2-runtime-route')) { throw 'Evidence shard widened the approved evidence boundary.' }
    $timestamp = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse([string]$shard.createdUtc, [ref]$timestamp)) { throw 'Evidence shard createdUtc is invalid.' }
    if ([string]$shard.disposition -notin @('authoritative','passed','failed','blocked','superseded')) { throw 'Evidence shard disposition is invalid.' }
    if ([string]$shard.localRetention -ne 'verified' -or [string]$shard.r2Retention -ne 'independently-retrieved-and-verified') {
        throw 'Evidence shard does not record complete two-copy retention.'
    }
    foreach ($identity in @($shard.contractIdentity)) {
        if ([string]$identity -notmatch '^(repository|sha256):[A-Za-z0-9._/-]+$' -or ([string]$identity).Contains('\') -or ([string]$identity).Contains('..')) {
            throw 'Evidence shard contract identity is not portable or scoped.'
        }
    }
    if ([string]::IsNullOrWhiteSpace([string]$shard.provenance)) { throw 'Evidence shard provenance is required.' }
    foreach ($identity in @($shard.producerRuntimeIdentity) + @($shard.licenseRecords)) {
        if ([string]$identity -notmatch '^(repository|sha256):[A-Za-z0-9._/-]+$' -or ([string]$identity).Contains('\') -or ([string]$identity).Contains('..')) {
            throw 'Evidence shard producer or license identity is not portable or scoped.'
        }
    }
    if (@($shard.producerRuntimeIdentity).Count -eq 0) { throw 'Evidence shard producer/runtime identity is required.' }
    $artifactIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $bytes = [int64]0
    foreach ($artifact in @($shard.artifacts)) {
        Assert-Gate0EvidenceExactProperties $artifact @(
            'artifactId','relativePath','byteSize','sha256','r2ObjectKey','purpose','retentionStatus','transferDisposition','remotelyVerifiedUtc'
        ) 'Evidence shard artifact'
        Assert-Gate0EvidenceIdentifier ([string]$artifact.artifactId) 'artifactId'
        Assert-Gate0EvidenceRelativePath ([string]$artifact.relativePath) 'artifact relativePath'
        if (-not $artifactIds.Add([string]$artifact.artifactId) -or -not $paths.Add([string]$artifact.relativePath)) { throw 'Evidence shard contains duplicate artifact IDs or paths.' }
        if ([int64]$artifact.byteSize -lt 0 -or [string]$artifact.sha256 -notmatch '^[A-F0-9]{64}$') { throw 'Evidence shard artifact size or hash is invalid.' }
        $expectedKey = "objects/sha256/$(([string]$artifact.sha256).Substring(0,2).ToLowerInvariant())/$(([string]$artifact.sha256).ToLowerInvariant())"
        if ([string]$artifact.r2ObjectKey -ne $expectedKey) { throw 'Evidence shard artifact object key is not content-addressed by its SHA-256.' }
        if ([string]$artifact.retentionStatus -ne 'remote-verified' -or [string]$artifact.transferDisposition -notin @('uploaded-and-verified','existing-object-verified','concurrent-create-verified','deduplicated-object-verified-in-this-run','independently-retrieved-and-verified')) {
            throw 'Evidence shard artifact does not carry an approved R2 receipt.'
        }
        $verified = [DateTimeOffset]::MinValue
        if (-not [DateTimeOffset]::TryParse([string]$artifact.remotelyVerifiedUtc, [ref]$verified)) { throw 'Evidence shard artifact receipt timestamp is invalid.' }
        $bytes += [int64]$artifact.byteSize
    }
    $attemptIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($attempt in @($shard.attempts)) {
        Assert-Gate0EvidenceExactProperties $attempt @('attemptId','phase','ordinal','retentionClass','recordPath','recordSha256','disposition','completeClosureReference') 'Evidence shard attempt binding'
        Assert-Gate0EvidenceIdentifier ([string]$attempt.attemptId) 'attemptId'
        if (-not $attemptIds.Add([string]$attempt.attemptId)) { throw 'Evidence shard contains a duplicate attempt ID.' }
        if ([string]$attempt.phase -notin @('warmup','measured','control') -or [int]$attempt.ordinal -lt 0 -or [string]$attempt.retentionClass -notin @('compact','complete') -or [string]$attempt.disposition -notin @('passed','failed','blocked','cleanup-failed','orphan-producing','byte-divergent','semantically-divergent','structurally-divergent')) { throw 'Evidence shard attempt binding has invalid semantics.' }
        Assert-Gate0EvidenceRelativePath ([string]$attempt.recordPath) 'attempt record path'
        $bound = @($shard.artifacts | Where-Object { $_.relativePath -eq $attempt.recordPath })
        if ($bound.Count -ne 1 -or [string]$attempt.recordSha256 -ne [string]$bound[0].sha256) { throw 'Evidence shard attempt binding does not reference one exact retained artifact.' }
        if ([string]$attempt.retentionClass -eq 'compact') {
            if ([int64]$bound[0].byteSize -gt 262144) { throw 'Compact attempt record exceeds its 256 KiB cap.' }
            Assert-Gate0EvidenceIdentifier ([string]$attempt.completeClosureReference) 'completeClosureReference'
        } elseif ($null -ne $attempt.completeClosureReference -and -not [string]::IsNullOrWhiteSpace([string]$attempt.completeClosureReference)) {
            Assert-Gate0EvidenceIdentifier ([string]$attempt.completeClosureReference) 'completeClosureReference'
        }
    }
    Assert-Gate0EvidenceExactProperties $shard.totals @('logicalArtifactCount','logicalArtifactBytes') 'Evidence shard totals'
    if ([int]$shard.totals.logicalArtifactCount -ne @($shard.artifacts).Count -or [int64]$shard.totals.logicalArtifactBytes -ne $bytes) {
        throw 'Evidence shard totals do not match its artifact records.'
    }
    return [pscustomobject]@{ Path = [IO.Path]::GetFullPath($Path); Text = $text; Manifest = $shard; Sha256 = Get-Gate0EvidenceSha256 $Path; Shape = $shape }
}

function Read-Gate0EvidenceRootIndex([string] $Path, [switch] $AllowMissingShards) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Evidence root index is missing: $Path" }
    Assert-Gate0EvidenceNoReparsePointAncestors $Path (Split-Path -Parent $Path)
    $text = Get-Content -LiteralPath $Path -Raw
    Assert-Gate0EvidenceMetadataText $text 'Evidence root index'
    $shape = Get-Gate0EvidenceFileShape $Path
    if ($shape.Bytes -gt 131072 -or $shape.Lines -gt 400) { throw 'Evidence root index exceeds its 128 KiB or 400-line cap.' }
    $root = $text | ConvertFrom-Json -Depth 64
    Assert-Gate0EvidenceExactProperties $root @('schemaVersion','indexId','evidenceBoundary','legacySeal','limits','runs','totals','limitations') 'Evidence root index'
    Assert-Gate0EvidenceExactProperties $root.legacySeal @('sourceManifestPath','sourceManifestSha256','durableManifestPath','durableManifestSha256','logicalArtifactCount','logicalArtifactBytes') 'Root legacy seal'
    Assert-Gate0EvidenceExactProperties $root.limits @('stage2ARetentionCeilingBytes','maxShardBytes','maxShardLines','maxRootIndexBytes','maxRootIndexLines','maxCompactAttemptBytes','plannedCellShards','maxInfrastructureShards') 'Root limits'
    Assert-Gate0EvidenceExactProperties $root.totals @('runCount','logicalArtifactCount','logicalArtifactBytes') 'Root totals'
    if ($root.schemaVersion -ne 1 -or $root.indexId -ne $script:RootIndexId -or $root.evidenceBoundary -ne 'gate0-proof-infrastructure-only') { throw 'Unsupported evidence root index.' }
    if ($root.legacySeal.sourceManifestPath -ne 'eng/gate0/artifact-retention-manifest.json' -or $root.legacySeal.sourceManifestSha256 -ne $script:ExpectedSourceSha256 -or
        $root.legacySeal.durableManifestPath -ne 'eng/gate0/artifact-manifest.json' -or $root.legacySeal.durableManifestSha256 -ne $script:ExpectedDurableSha256 -or
        [int]$root.legacySeal.logicalArtifactCount -ne $script:ExpectedLogicalArtifactCount -or [int64]$root.legacySeal.logicalArtifactBytes -ne $script:ExpectedLogicalArtifactBytes) {
        throw 'Evidence root index changed the approved legacy seal.'
    }
    if ([int64]$root.limits.stage2ARetentionCeilingBytes -ne 805306368 -or [int]$root.limits.maxShardBytes -ne 65536 -or [int]$root.limits.maxShardLines -ne 300 -or
        [int]$root.limits.maxRootIndexBytes -ne 131072 -or [int]$root.limits.maxRootIndexLines -ne 400 -or [int]$root.limits.maxCompactAttemptBytes -ne 262144 -or [int]$root.limits.plannedCellShards -ne 18 -or [int]$root.limits.maxInfrastructureShards -ne 2) {
        throw 'Evidence root index changed an approved retention limit.'
    }
    $runIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $cellIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $expectedPreviousId = $null
    $expectedPreviousHash = $null
    $artifacts = 0
    $bytes = [int64]0
    $rootDirectory = Split-Path -Parent $Path
    $referencedShards = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $infrastructureRuns = 0
    $cellRuns = 0
    for ($index = 0; $index -lt @($root.runs).Count; $index++) {
        $run = @($root.runs)[$index]
        Assert-Gate0EvidenceExactProperties $run @('ordinal','runKind','proofRunId','evidenceGroupId','cellId','shardPath','shardSha256','entrySha256','previousRunId','previousRunEntrySha256','disposition','logicalArtifactCount','logicalArtifactBytes','localRetention','r2Retention') 'Evidence root run entry'
        if ([int]$run.ordinal -ne $index + 1) { throw 'Evidence root run entries are reordered or have a non-contiguous ordinal.' }
        foreach ($pair in @(@($run.proofRunId,'proofRunId'),@($run.evidenceGroupId,'evidenceGroupId'),@($run.cellId,'cellId'))) { Assert-Gate0EvidenceIdentifier ([string]$pair[0]) ([string]$pair[1]) }
        if (-not $runIds.Add([string]$run.proofRunId) -or -not $cellIds.Add([string]$run.cellId)) { throw 'Evidence root contains a duplicate proof-run or cell ID.' }
        if ([string]$run.runKind -eq 'infrastructure') { $infrastructureRuns++ } elseif ([string]$run.runKind -eq 'stage2a-cell') { $cellRuns++ } else { throw 'Evidence root run kind is invalid.' }
        if (($null -eq $expectedPreviousId -and $null -ne $run.previousRunId) -or ($null -ne $expectedPreviousId -and [string]$run.previousRunId -ne $expectedPreviousId) -or
            ($null -eq $expectedPreviousHash -and $null -ne $run.previousRunEntrySha256) -or ($null -ne $expectedPreviousHash -and [string]$run.previousRunEntrySha256 -ne $expectedPreviousHash)) {
            throw 'Evidence root run-entry chain is reordered or broken.'
        }
        if ([string]$run.entrySha256 -ne (Get-Gate0EvidenceEntryHash $run)) { throw 'Evidence root run-entry hash is invalid.' }
        Assert-Gate0EvidenceRelativePath ([string]$run.shardPath) 'shardPath'
        if (-not ([string]$run.shardPath).StartsWith('stage2/', [StringComparison]::Ordinal) -or -not ([string]$run.shardPath).EndsWith('.manifest.json', [StringComparison]::Ordinal)) { throw 'Evidence root shard path is outside the approved Stage 2 layout.' }
        [void]$referencedShards.Add([string]$run.shardPath)
        $shardPath = [IO.Path]::GetFullPath((Join-Path $rootDirectory ([string]$run.shardPath).Replace('/', [IO.Path]::DirectorySeparatorChar)))
        if (-not $AllowMissingShards) {
            if (-not $shardPath.StartsWith("$rootDirectory$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase)) { throw 'Evidence shard escaped the evidence root.' }
            Assert-Gate0EvidenceNoReparsePointAncestors $shardPath $rootDirectory
            $shard = Read-Gate0EvidenceShard $shardPath
            if ($shard.Sha256 -ne [string]$run.shardSha256 -or $shard.Manifest.proofRunId -ne $run.proofRunId -or $shard.Manifest.evidenceGroupId -ne $run.evidenceGroupId -or $shard.Manifest.cellId -ne $run.cellId -or
                [int]$shard.Manifest.totals.logicalArtifactCount -ne [int]$run.logicalArtifactCount -or [int64]$shard.Manifest.totals.logicalArtifactBytes -ne [int64]$run.logicalArtifactBytes) {
                throw 'Evidence root entry does not bind its exact shard identity and totals.'
            }
        }
        if ([string]$run.disposition -notin @('authoritative','passed','failed','blocked','superseded') -or [string]$run.localRetention -ne 'verified' -or [string]$run.r2Retention -ne 'independently-retrieved-and-verified') { throw 'Evidence root run entry has an invalid disposition or retention state.' }
        $artifacts += [int]$run.logicalArtifactCount
        $bytes += [int64]$run.logicalArtifactBytes
        $expectedPreviousId = [string]$run.proofRunId
        $expectedPreviousHash = [string]$run.entrySha256
    }
    if ([int]$root.totals.runCount -ne @($root.runs).Count -or [int]$root.totals.logicalArtifactCount -ne $artifacts -or [int64]$root.totals.logicalArtifactBytes -ne $bytes) { throw 'Evidence root totals do not match its run entries.' }
    if ($infrastructureRuns -gt [int]$root.limits.maxInfrastructureShards -or $cellRuns -gt [int]$root.limits.plannedCellShards) { throw 'Evidence root exceeds its approved infrastructure or Stage 2A cell-shard count.' }
    if ([int64]$root.totals.logicalArtifactBytes -gt [int64]$root.limits.stage2ARetentionCeilingBytes) { throw 'Evidence root exceeds the approved Stage 2A retention ceiling.' }
    if (-not $AllowMissingShards) {
        $stage2 = Join-Path $rootDirectory 'stage2'
        $stage2Items = @()
        if (Test-Path -LiteralPath $stage2 -PathType Container) {
            $stage2Items = @(Get-ChildItem -LiteralPath $stage2 -Force -Recurse)
            $stage2Reparse = @($stage2Items | Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 })
            if ($stage2Reparse.Count -ne 0) { throw "The tracked evidence shard tree contains a reparse point: $($stage2Reparse[0].FullName)" }
        }
        $unexpectedDirectories = @($stage2Items | Where-Object { $_.PSIsContainer })
        if ($unexpectedDirectories.Count -ne 0) { throw "The tracked evidence shard tree contains an unexpected directory: $($unexpectedDirectories[0].FullName)" }
        $actual = @($stage2Items | Where-Object { -not $_.PSIsContainer } | ForEach-Object { "stage2/$($_.Name)" })
        foreach ($pathValue in $actual) { if (-not $referencedShards.Contains($pathValue)) { throw "Evidence directory contains an unindexed shard: $pathValue" } }
    }
    return [pscustomobject]@{ Path = [IO.Path]::GetFullPath($Path); Text = $text; Index = $root; Sha256 = Get-Gate0EvidenceSha256 $Path; Shape = $shape }
}

function Assert-Gate0LegacyEvidenceSeal([string] $RepositoryRoot, [switch] $RequireEffective) {
    $repository = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $gate0 = Join-Path $repository 'eng/gate0'
    $source = Join-Path $gate0 'artifact-retention-manifest.json'
    $durable = Join-Path $gate0 'artifact-manifest.json'
    if ((Get-Gate0EvidenceSha256 $source) -ne $script:ExpectedSourceSha256 -or (Get-Gate0EvidenceSha256 $durable) -ne $script:ExpectedDurableSha256) { throw 'A sealed legacy manifest hash changed.' }
    $sourceJson = Get-Content -LiteralPath $source -Raw | ConvertFrom-Json -Depth 64
    $durableJson = Get-Content -LiteralPath $durable -Raw | ConvertFrom-Json -Depth 64
    if ([int]$sourceJson.totals.fileCount -ne $script:ExpectedLogicalArtifactCount -or [int64]$sourceJson.totals.totalBytes -ne $script:ExpectedLogicalArtifactBytes -or
        $durableJson.sourceInventory.sha256 -ne $script:ExpectedSourceSha256 -or [int]$durableJson.sourceInventory.logicalArtifactCount -ne $script:ExpectedLogicalArtifactCount -or
        [int64]$durableJson.sourceInventory.logicalArtifactBytes -ne $script:ExpectedLogicalArtifactBytes -or -not [bool]$durableJson.status.secondPrivateCopyVerified -or $durableJson.status.retentionCondition -ne 'complete') {
        throw 'Legacy manifest counts, durable binding, or R2 receipt status changed.'
    }
    $rootPath = Join-Path $gate0 'evidence/root-index.json'
    $root = Read-Gate0EvidenceRootIndex $rootPath
    $sealPath = Join-Path $gate0 'evidence/legacy-seal.json'
    if (-not (Test-Path -LiteralPath $sealPath -PathType Leaf)) {
        if ($RequireEffective) { throw 'The legacy evidence seal is not effective.' }
        return [pscustomobject]@{ Effective = $false; Root = $root }
    }
    $text = Get-Content -LiteralPath $sealPath -Raw
    Assert-Gate0EvidenceMetadataText $text 'Legacy evidence seal'
    $seal = $text | ConvertFrom-Json -Depth 32
    Assert-Gate0EvidenceExactProperties $seal @('schemaVersion','sealId','effectiveUtc','sourceManifestPath','sourceManifestSha256','durableManifestPath','durableManifestSha256','logicalArtifactCount','logicalArtifactBytes','rootIndexPath','initialRootIndexSha256','retentionCondition','limitations') 'Legacy evidence seal'
    if ($seal.schemaVersion -ne 1 -or $seal.sealId -ne $script:SealId -or $seal.sourceManifestPath -ne 'eng/gate0/artifact-retention-manifest.json' -or $seal.sourceManifestSha256 -ne $script:ExpectedSourceSha256 -or
        $seal.durableManifestPath -ne 'eng/gate0/artifact-manifest.json' -or $seal.durableManifestSha256 -ne $script:ExpectedDurableSha256 -or [int]$seal.logicalArtifactCount -ne $script:ExpectedLogicalArtifactCount -or
        [int64]$seal.logicalArtifactBytes -ne $script:ExpectedLogicalArtifactBytes -or $seal.rootIndexPath -ne 'eng/gate0/evidence/root-index.json' -or $seal.initialRootIndexSha256 -ne $script:ExpectedInitialRootIndexSha256 -or $seal.retentionCondition -ne 'complete-and-independently-byte-verified') {
        throw 'Legacy evidence seal does not match the approved effective seal contract.'
    }
    $effective = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse([string]$seal.effectiveUtc, [ref]$effective)) { throw 'Legacy evidence seal timestamp is invalid.' }
    return [pscustomobject]@{ Effective = $true; Root = $root; Seal = $seal; SealPath = $sealPath }
}

Export-ModuleMember -Function @(
    'Assert-Gate0EvidenceIdentifier',
    'Assert-Gate0EvidenceMetadataText',
    'Assert-Gate0EvidenceNoReparsePointAncestors',
    'Assert-Gate0EvidenceRelativePath',
    'Assert-Gate0LegacyEvidenceSeal',
    'ConvertTo-Gate0CanonicalJson',
    'Get-Gate0EvidenceEntryHash',
    'Get-Gate0EvidenceFileShape',
    'Get-Gate0EvidenceSha256',
    'Get-Gate0EvidenceTextSha256',
    'Read-Gate0EvidenceRootIndex',
    'Read-Gate0EvidenceShard',
    'Write-Gate0EvidenceUtf8Atomic'
)

# V2-specific validators are deliberately separate from V1.  V1 helpers above
# are retained only for canonical JSON, hashing, atomic writes, metadata, and
# non-reparse path checks; no V1 evidence data is ever written by this module.
$script:V2RootId = 'Gate0.Stage2Evidence.Root.V2'
$script:V2ShardId = 'Gate0.Stage2Evidence.Shard.V2'
$script:V2V1RootSha = 'C6D3CD9E7B0FC62E199E6FAD0A7D0FBAB6AFE1BBA6C8EFD4EAE427BBB79E30EA'
$script:V2V1FinalRun = 'g05-stage2a-20260827T170732499Z-df30a192-stress-1080p-webm-one'
$script:V2V1FinalEntry = '29E21E5A56B0D82B25D914698E3CFA30EFC565F42345F436A26BCF8C1F97EB94'
$script:V2V1Bytes = [int64]78538843
$script:V2Ceiling = [int64]805306368

function Get-Gate0EvidenceV2Sha256([string] $Path) { Get-Gate0EvidenceSha256 $Path }
function Get-Gate0EvidenceV2TextSha256([string] $Text) { Get-Gate0EvidenceTextSha256 $Text }
function Get-Gate0EvidenceV2Shape([string] $Path) { Get-Gate0EvidenceFileShape $Path }
function Write-Gate0EvidenceV2Utf8Atomic([string] $Path, [string] $Text) { Write-Gate0EvidenceUtf8Atomic $Path $Text }
function Assert-Gate0EvidenceV2CanonicalSerializerRuntime {
    if ($PSVersionTable.PSEdition -ne 'Core' -or [string]$PSVersionTable.PSVersion -ne '7.6.4' -or [string]$PSVersionTable.GitCommitId -ne '7.6.4') {
        throw 'V2 canonical shard serialization requires PowerShell Core 7.6.4 / GitCommitId 7.6.4.'
    }
}
function ConvertTo-Gate0EvidenceV2OrderedWriterValue($Value) {
    if ($null -eq $Value -or $Value -is [string] -or $Value -is [ValueType]) { return $Value }
    if ($Value -is [Collections.IDictionary]) {
        $ordered = [ordered]@{}
        foreach ($key in $Value.Keys) { $ordered[[string]$key] = ConvertTo-Gate0EvidenceV2OrderedWriterValue $Value[$key] }
        return $ordered
    }
    if ($Value -is [Collections.IEnumerable]) {
        $items = [object[]]@($Value | ForEach-Object { ConvertTo-Gate0EvidenceV2OrderedWriterValue $_ })
        return ,$items
    }
    $ordered = [ordered]@{}
    foreach ($property in $Value.PSObject.Properties) { $ordered[$property.Name] = ConvertTo-Gate0EvidenceV2OrderedWriterValue $property.Value }
    return $ordered
}
function ConvertTo-Gate0EvidenceV2CanonicalShardUtf8($Shard) {
    Assert-Gate0EvidenceV2CanonicalSerializerRuntime
    $writerValue = ConvertTo-Gate0EvidenceV2OrderedWriterValue $Shard
    $text = ($writerValue | ConvertTo-Json -Depth 64 -Compress) + "`n"
    return [Text.UTF8Encoding]::new($false).GetBytes($text)
}
function Assert-Gate0EvidenceV2ExactProperties($Value, [string[]] $Expected, [string] $Label) { Assert-Gate0EvidenceExactProperties $Value $Expected $Label }
function Assert-Gate0EvidenceV2Identifier([string] $Value, [string] $Label) { Assert-Gate0EvidenceIdentifier $Value $Label }
function Assert-Gate0EvidenceV2RelativePath([string] $Value, [string] $Label) { Assert-Gate0EvidenceRelativePath $Value $Label }
function Assert-Gate0EvidenceV2MetadataText([string] $Text, [string] $Label) { Assert-Gate0EvidenceMetadataText $Text $Label }
function Assert-Gate0EvidenceV2NoReparsePointAncestors([string] $Path, [string] $StopAt) { Assert-Gate0EvidenceNoReparsePointAncestors $Path $StopAt }
function ConvertTo-Gate0EvidenceV2CanonicalJson($Value) { ConvertTo-Gate0CanonicalJson $Value }
function Get-Gate0EvidenceV2EntryHash($Entry) {
    $copy = [ordered]@{}
    foreach ($property in @($Entry.PSObject.Properties)) { if ($property.Name -ne 'entrySha256') { $copy[$property.Name] = $property.Value } }
    Get-Gate0EvidenceV2TextSha256 (ConvertTo-Gate0EvidenceV2CanonicalJson $copy)
}
function Assert-Gate0EvidenceV2Timestamp([string] $Value, [string] $Label) {
    $parsed = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse($Value, [ref]$parsed)) { throw "$Label is not an ISO-8601 timestamp." }
}
function Assert-Gate0EvidenceV2Identity([string] $Value, [string] $Label) {
    if ($Value -notmatch '^(repository|sha256):[A-Za-z0-9._/-]+$' -or $Value.Contains('..') -or $Value.Contains('\')) { throw "$Label is not portable and scoped." }
}
function Assert-Gate0EvidenceV2Identities($Values, [string] $Label, [switch] $Optional) {
    if (-not $Optional -and @($Values).Count -eq 0) { throw "$Label is required." }
    foreach ($value in @($Values)) { Assert-Gate0EvidenceV2Identity ([string]$value) $Label }
}
function Get-Gate0EvidenceV2RepositoryRoot { [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..')).TrimEnd([IO.Path]::DirectorySeparatorChar) }
function Assert-Gate0EvidenceV2Predecessor([string] $RepositoryRoot) {
    $path = Join-Path $RepositoryRoot 'eng/gate0/evidence/root-index.json'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'The immutable V1 predecessor root is missing.' }
    Assert-Gate0EvidenceV2NoReparsePointAncestors $path $RepositoryRoot
    if ((Get-Gate0EvidenceV2Sha256 $path) -ne $script:V2V1RootSha) { throw 'The immutable V1 predecessor root hash changed.' }
    $root = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -Depth 64
    $last = @($root.runs)[-1]
    if ($root.indexId -ne 'Gate0.Stage2Evidence.Root.V1' -or @($root.runs).Count -ne 20 -or [int64]$root.totals.logicalArtifactBytes -ne $script:V2V1Bytes -or $last.proofRunId -ne $script:V2V1FinalRun -or $last.entrySha256 -ne $script:V2V1FinalEntry) { throw 'The immutable V1 predecessor identity is not exact.' }
    [pscustomobject]@{ Path = $path; Sha256 = $script:V2V1RootSha; Index = $root }
}
function Read-Gate0EvidenceV2Shard([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw 'V2 evidence shard is missing.' }
    Assert-Gate0EvidenceV2NoReparsePointAncestors $Path (Split-Path -Parent $Path)
    $text = Get-Content -LiteralPath $Path -Raw; Assert-Gate0EvidenceV2MetadataText $text 'V2 evidence shard'
    $shape = Get-Gate0EvidenceV2Shape $Path
    if ($shape.Bytes -gt 65536 -or $shape.Lines -gt 300) { throw 'V2 evidence shard exceeds its cap.' }
    $shard = $text | ConvertFrom-Json -Depth 64
    Assert-Gate0EvidenceV2ExactProperties $shard @('schemaVersion','shardId','proofRunId','evidenceGroupId','cellId','evidenceBoundary','createdUtc','contractIdentity','provenance','producerRuntimeIdentity','licenseRecords','artifacts','attempts','disposition','localRetention','r2Retention','totals','limitations') 'V2 evidence shard'
    if ($shard.schemaVersion -ne 1 -or $shard.shardId -ne $script:V2ShardId -or [string]$shard.evidenceBoundary -notin @('containment-no-media','p2-runtime-route') -or [string]$shard.disposition -notin @('authoritative','passed','failed','blocked','superseded') -or $shard.localRetention -ne 'verified' -or $shard.r2Retention -ne 'independently-retrieved-and-verified') { throw 'V2 evidence shard status or schema is invalid.' }
    foreach ($name in @('proofRunId','evidenceGroupId','cellId')) { Assert-Gate0EvidenceV2Identifier ([string]$shard.$name) "V2 shard $name" }
    Assert-Gate0EvidenceV2Timestamp ([string]$shard.createdUtc) 'V2 shard createdUtc'; Assert-Gate0EvidenceV2Identities $shard.contractIdentity 'V2 contract identity'; Assert-Gate0EvidenceV2Identities $shard.producerRuntimeIdentity 'V2 producer identity'; Assert-Gate0EvidenceV2Identities $shard.licenseRecords 'V2 license identity' -Optional
    if ([string]::IsNullOrWhiteSpace([string]$shard.provenance)) { throw 'V2 shard provenance is required.' }
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal); $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase); $bytes = [int64]0
    foreach ($artifact in @($shard.artifacts)) {
        Assert-Gate0EvidenceV2ExactProperties $artifact @('artifactId','relativePath','byteSize','sha256','r2ObjectKey','purpose','retentionStatus','transferDisposition','remotelyVerifiedUtc') 'V2 artifact'
        Assert-Gate0EvidenceV2Identifier ([string]$artifact.artifactId) 'V2 artifactId'; Assert-Gate0EvidenceV2RelativePath ([string]$artifact.relativePath) 'V2 artifact path'
        if (-not $ids.Add([string]$artifact.artifactId) -or -not $paths.Add([string]$artifact.relativePath) -or -not ([string]$artifact.relativePath).StartsWith("future/stage2/v2/$($shard.proofRunId)/", [StringComparison]::Ordinal) -or [int64]$artifact.byteSize -lt 0 -or [string]$artifact.sha256 -notmatch '^[A-F0-9]{64}$') { throw 'V2 artifact identity, namespace, or size is invalid.' }
        $key = "objects/sha256/$(([string]$artifact.sha256).Substring(0,2).ToLowerInvariant())/$(([string]$artifact.sha256).ToLowerInvariant())"
        if ($artifact.r2ObjectKey -ne $key -or $artifact.retentionStatus -ne 'remote-verified' -or [string]$artifact.transferDisposition -notin @('uploaded-and-verified','existing-object-verified','concurrent-create-verified','deduplicated-object-verified-in-this-run','independently-retrieved-and-verified')) { throw 'V2 artifact R2 receipt is invalid.' }
        Assert-Gate0EvidenceV2Timestamp ([string]$artifact.remotelyVerifiedUtc) 'V2 artifact receipt'; $bytes += [int64]$artifact.byteSize
    }
    $attemptIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($attempt in @($shard.attempts)) {
        Assert-Gate0EvidenceV2ExactProperties $attempt @('attemptId','originalAttemptId','phase','ordinal','retentionClass','recordPath','recordSha256','disposition','completeClosureReference') 'V2 attempt'
        Assert-Gate0EvidenceV2Identifier ([string]$attempt.attemptId) 'V2 attemptId'; Assert-Gate0EvidenceV2Identifier ([string]$attempt.originalAttemptId) 'V2 originalAttemptId'; Assert-Gate0EvidenceV2RelativePath ([string]$attempt.recordPath) 'V2 attempt recordPath'
        $record = @($shard.artifacts | Where-Object { $_.relativePath -eq $attempt.recordPath })
        if (-not $attemptIds.Add([string]$attempt.attemptId) -or $attempt.attemptId -eq $attempt.originalAttemptId -or [string]$attempt.phase -notin @('warmup','measured','control') -or [int]$attempt.ordinal -lt 1 -or [string]$attempt.retentionClass -notin @('compact','complete') -or [string]$attempt.disposition -notin @('passed','failed','blocked','cleanup-failed','orphan-producing','byte-divergent','semantically-divergent','structurally-divergent') -or $record.Count -ne 1 -or $record[0].sha256 -ne $attempt.recordSha256) { throw 'V2 attempt binding is invalid.' }
        if ($attempt.retentionClass -eq 'compact' -and ([int64]$record[0].byteSize -gt 262144 -or [string]::IsNullOrWhiteSpace([string]$attempt.completeClosureReference))) { throw 'V2 compact attempt is invalid.' }
    }
    if ($shard.evidenceBoundary -eq 'containment-no-media' -and @($shard.attempts).Count -ne 0) { throw 'Infrastructure shards cannot contain attempts.' }
    if ($shard.evidenceBoundary -eq 'p2-runtime-route' -and @($shard.attempts).Count -eq 0) { throw 'Continuation shards require attempts.' }
    Assert-Gate0EvidenceV2ExactProperties $shard.totals @('logicalArtifactCount','logicalArtifactBytes') 'V2 shard totals'
    if ([int]$shard.totals.logicalArtifactCount -ne @($shard.artifacts).Count -or [int64]$shard.totals.logicalArtifactBytes -ne $bytes) { throw 'V2 shard totals do not match artifacts.' }
    [pscustomobject]@{ Path = [IO.Path]::GetFullPath($Path); Text = $text; Manifest = $shard; Sha256 = Get-Gate0EvidenceV2Sha256 $Path; Shape = $shape }
}
function Read-Gate0EvidenceV2RootIndex([string] $Path, [switch] $AllowMissingShards) {
    $repo = Get-Gate0EvidenceV2RepositoryRoot; $predecessor = Assert-Gate0EvidenceV2Predecessor $repo
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw 'V2 root index is missing.' }; Assert-Gate0EvidenceV2NoReparsePointAncestors $Path (Split-Path -Parent $Path)
    $text = Get-Content -LiteralPath $Path -Raw; Assert-Gate0EvidenceV2MetadataText $text 'V2 root index'; $shape = Get-Gate0EvidenceV2Shape $Path
    if ($shape.Bytes -gt 131072 -or $shape.Lines -gt 400) { throw 'V2 root index exceeds its cap.' }; $root = $text | ConvertFrom-Json -Depth 64
    Assert-Gate0EvidenceV2ExactProperties $root @('schemaVersion','indexId','evidenceBoundary','predecessor','limits','runs','totals','limitations') 'V2 root index'
    Assert-Gate0EvidenceV2ExactProperties $root.predecessor @('rootIndexPath','rootIndexSha256','indexId','finalProofRunId','finalEntrySha256','logicalArtifactBytes') 'V2 predecessor'
    Assert-Gate0EvidenceV2ExactProperties $root.limits @('globalRetentionCeilingBytes','maxShardBytes','maxShardLines','maxRootIndexBytes','maxRootIndexLines','maxCompactAttemptBytes','plannedContinuationCellShards','maxInfrastructureShards') 'V2 limits'
    Assert-Gate0EvidenceV2ExactProperties $root.totals @('runCount','logicalArtifactCount','logicalArtifactBytes') 'V2 totals'
    if ($root.schemaVersion -ne 1 -or $root.indexId -ne $script:V2RootId -or $root.evidenceBoundary -ne 'gate0-proof-infrastructure-only' -or $root.predecessor.rootIndexPath -ne 'eng/gate0/evidence/root-index.json' -or $root.predecessor.rootIndexSha256 -ne $script:V2V1RootSha -or $root.predecessor.indexId -ne 'Gate0.Stage2Evidence.Root.V1' -or $root.predecessor.finalProofRunId -ne $script:V2V1FinalRun -or $root.predecessor.finalEntrySha256 -ne $script:V2V1FinalEntry -or [int64]$root.predecessor.logicalArtifactBytes -ne $script:V2V1Bytes) { throw 'V2 immutable predecessor binding is invalid.' }
    $limits = $root.limits
    if ([int64]$limits.globalRetentionCeilingBytes -ne $script:V2Ceiling -or [int]$limits.maxShardBytes -ne 65536 -or [int]$limits.maxShardLines -ne 300 -or [int]$limits.maxRootIndexBytes -ne 131072 -or [int]$limits.maxRootIndexLines -ne 400 -or [int]$limits.maxCompactAttemptBytes -ne 262144 -or [int]$limits.plannedContinuationCellShards -ne 12 -or [int]$limits.maxInfrastructureShards -ne 2) { throw 'V2 limits are not exact.' }
    $ids=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$cells=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$paths=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);$previousId=$null;$previousHash=$null;$bytes=[int64]0;$count=0;$infra=0;$cellsCount=0;$rootDir=Split-Path -Parent $Path
    for($i=0;$i -lt @($root.runs).Count;$i++) { $entry=@($root.runs)[$i]; Assert-Gate0EvidenceV2ExactProperties $entry @('ordinal','runKind','proofRunId','evidenceGroupId','cellId','shardPath','shardSha256','entrySha256','previousRunId','previousRunEntrySha256','disposition','logicalArtifactCount','logicalArtifactBytes','localRetention','r2Retention') 'V2 root entry'; foreach($name in @('proofRunId','evidenceGroupId','cellId')){Assert-Gate0EvidenceV2Identifier ([string]$entry.$name) "V2 root $name"}; Assert-Gate0EvidenceV2RelativePath ([string]$entry.shardPath) 'V2 root shardPath'; if([int]$entry.ordinal -ne ($i+1) -or -not $ids.Add([string]$entry.proofRunId) -or -not $cells.Add([string]$entry.cellId) -or -not $paths.Add([string]$entry.shardPath) -or $entry.entrySha256 -ne (Get-Gate0EvidenceV2EntryHash $entry) -or $entry.previousRunId -ne $previousId -or $entry.previousRunEntrySha256 -ne $previousHash -or [string]$entry.disposition -notin @('authoritative','passed','failed','blocked','superseded') -or $entry.localRetention -ne 'verified' -or $entry.r2Retention -ne 'independently-retrieved-and-verified' -or $entry.shardPath -notmatch '^stage2/[A-Za-z0-9][A-Za-z0-9._-]{0,127}\.manifest\.json$' -or [string]$entry.shardSha256 -notmatch '^[A-F0-9]{64}$'){throw 'V2 root entry is invalid.'}; if($entry.runKind -eq 'infrastructure'){$infra++}elseif($entry.runKind -eq 'stage2a-continuation-cell'){$cellsCount++}else{throw 'V2 root run kind is invalid.'}; if(-not $AllowMissingShards){$shardPath=Join-Path $rootDir (($entry.shardPath).Replace('/','\'));$shard=Read-Gate0EvidenceV2Shard $shardPath;$expectedBoundary=if($entry.runKind-eq'infrastructure'){'containment-no-media'}else{'p2-runtime-route'};if($shard.Sha256-ne$entry.shardSha256-or$shard.Manifest.proofRunId-ne$entry.proofRunId-or$shard.Manifest.evidenceGroupId-ne$entry.evidenceGroupId-or$shard.Manifest.cellId-ne$entry.cellId-or$shard.Manifest.evidenceBoundary-ne$expectedBoundary-or$shard.Manifest.disposition-ne$entry.disposition-or$shard.Manifest.localRetention-ne$entry.localRetention-or$shard.Manifest.r2Retention-ne$entry.r2Retention-or[int]$shard.Manifest.totals.logicalArtifactCount-ne[int]$entry.logicalArtifactCount-or[int64]$shard.Manifest.totals.logicalArtifactBytes-ne[int64]$entry.logicalArtifactBytes){throw 'V2 root to shard binding is invalid.'}};$count += [int]$entry.logicalArtifactCount;$bytes += [int64]$entry.logicalArtifactBytes;$previousId=[string]$entry.proofRunId;$previousHash=[string]$entry.entrySha256 }
    if([int]$root.totals.runCount-ne@($root.runs).Count-or[int]$root.totals.logicalArtifactCount-ne$count-or[int64]$root.totals.logicalArtifactBytes-ne$bytes-or$infra-gt2-or$cellsCount-gt12-or($script:V2V1Bytes+$bytes)-gt$script:V2Ceiling){throw 'V2 root totals or capacity are invalid.'}
    [pscustomobject]@{Path=[IO.Path]::GetFullPath($Path);Text=$text;Index=$root;Sha256=Get-Gate0EvidenceV2Sha256 $Path;Predecessor=$predecessor;Shape=$shape}
}
Export-ModuleMember -Function *-Gate0EvidenceV2*
