Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:BucketName = 'reelforge-artifacts'
$script:AccountIdSecretName = 'ReelForge.Engineering.R2.AccountId'
$script:AccessKeyIdSecretName = 'ReelForge.Engineering.R2.AccessKeyId'
$script:SecretAccessKeySecretName = 'ReelForge.Engineering.R2.SecretAccessKey'

$clientSource = Join-Path $PSScriptRoot 'Gate0ArtifactR2Client.cs'
if (-not ('ReelForge.Gate0.Artifacts.Gate0ArtifactR2Client' -as [type])) {
    Add-Type -Path $clientSource
}

function Get-Gate0Sha256([string] $Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Assert-Gate0SafeRelativePath([string] $Value, [string] $Label) {
    if ([string]::IsNullOrWhiteSpace($Value) -or [IO.Path]::IsPathRooted($Value) -or $Value.Contains('\')) {
        throw "Unsafe $Label."
    }
    foreach ($segment in $Value.Split('/')) {
        if ([string]::IsNullOrWhiteSpace($segment) -or $segment -in @('.', '..')) { throw "Unsafe $Label." }
    }
}

function Assert-Gate0ExactProperties($Value, [string[]] $Expected, [string] $Label) {
    if ($null -eq $Value) { throw "$Label is missing." }
    $actual = @($Value.PSObject.Properties.Name)
    $difference = @(Compare-Object -ReferenceObject @($Expected | Sort-Object) -DifferenceObject @($actual | Sort-Object))
    if ($difference.Count -ne 0) { throw "$Label does not match the closed manifest schema." }
}

function Assert-Gate0NoReparsePointAncestors([string] $Path, [string] $StopAt) {
    $current = Get-Item -LiteralPath $Path -Force
    $resolvedStop = [IO.Path]::GetFullPath($StopAt).TrimEnd([IO.Path]::DirectorySeparatorChar)
    while ($null -ne $current) {
        if (($current.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Gate 0 artifact path contains a reparse point: $($current.FullName)"
        }
        if ($current.FullName.TrimEnd([IO.Path]::DirectorySeparatorChar).Equals($resolvedStop, [StringComparison]::OrdinalIgnoreCase)) { return }
        $current = if ($current -is [IO.DirectoryInfo]) { $current.Parent } else { $current.Directory }
    }
    throw 'Gate 0 artifact path did not resolve through the approved artifact root.'
}

function Get-Gate0ObjectKey([string] $Sha256) {
    if ($Sha256 -notmatch '^[A-Fa-f0-9]{64}$') { throw 'Artifact SHA-256 must contain exactly 64 hexadecimal characters.' }
    $normalized = $Sha256.ToLowerInvariant()
    return "objects/sha256/$($normalized.Substring(0, 2))/$normalized"
}

function Resolve-Gate0ArtifactRoot([string] $ArtifactRoot) {
    $repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..')).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $candidate = $ArtifactRoot
    if ([string]::IsNullOrWhiteSpace($candidate)) { $candidate = [Environment]::GetEnvironmentVariable('REELFORGE_GATE0_ARTIFACT_ROOT') }
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        $candidate = Join-Path ([IO.Path]::GetDirectoryName($repositoryRoot)) 'ReelForge.Gate0Artifacts'
    }
    if (-not [IO.Path]::IsPathRooted($candidate)) { throw 'ArtifactRoot must be absolute.' }
    $resolved = [IO.Path]::GetFullPath($candidate).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $resolved -PathType Container)) { throw "Artifact root does not exist: $resolved" }
    $actual = (Get-Item -LiteralPath $resolved -Force).FullName.TrimEnd([IO.Path]::DirectorySeparatorChar)
    Assert-Gate0NoReparsePointAncestors $actual $actual
    return $actual
}

function Read-Gate0SourceInventory([string] $SourceManifestPath) {
    if (-not [IO.Path]::IsPathRooted($SourceManifestPath)) { throw 'Source manifest path must be absolute.' }
    $resolved = [IO.Path]::GetFullPath($SourceManifestPath)
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) { throw "Source manifest does not exist: $resolved" }
    $text = Get-Content -Raw -LiteralPath $resolved
    $manifest = $text | ConvertFrom-Json -Depth 30
    if ($manifest.schemaVersion -ne 1 -or [string]::IsNullOrWhiteSpace([string] $manifest.artifactSetId)) {
        throw 'Unsupported Gate 0 source-inventory manifest.'
    }

    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $entries = [Collections.Generic.List[object]]::new()
    foreach ($group in @($manifest.groups)) {
        foreach ($file in @($group.files)) {
            $id = [string] $file.artifactId
            $relative = [string] $file.filename
            Assert-Gate0SafeRelativePath $relative 'source artifact filename'
            if (-not $ids.Add($id)) { throw "Duplicate source artifact ID: $id" }
            if (-not $paths.Add($relative)) { throw "Duplicate source artifact filename: $relative" }
            $sha256 = ([string] $file.sha256).ToUpperInvariant()
            $objectKey = Get-Gate0ObjectKey $sha256
            $entries.Add([pscustomobject]@{
                ArtifactId = $id
                Purpose = "Durable Gate 0 proof input or evidence from $([string] $group.groupId)."
                OriginalFilename = [IO.Path]::GetFileName($relative)
                LocalRelativePath = $relative
                Size = [int64] $file.size
                Sha256 = $sha256
                ObjectKey = $objectKey
                SourceProvenance = [string] $group.provenance
                ProducerRuntimeIdentity = @($group.producerRuntimeIdentity | ForEach-Object { [string] $_ })
                ProofRunOrContractIdentity = @($group.proofRunIdentity | ForEach-Object { [string] $_ })
                LicenseProvenanceRecords = @($group.licenseRecords | ForEach-Object { [string] $_ })
            })
        }
    }
    if ($entries.Count -ne [int] $manifest.totals.fileCount -or
        ($entries | Measure-Object -Property Size -Sum).Sum -ne [int64] $manifest.totals.totalBytes) {
        throw 'Source manifest totals do not match its flattened artifact inventory.'
    }
    return [pscustomobject]@{
        Path = $resolved
        Sha256 = Get-Gate0Sha256 $resolved
        Manifest = $manifest
        Entries = @($entries)
    }
}

function Read-Gate0RemoteManifest([string] $ManifestPath) {
    if (-not [IO.Path]::IsPathRooted($ManifestPath)) { throw 'Remote manifest path must be absolute.' }
    $resolved = [IO.Path]::GetFullPath($ManifestPath)
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) { throw "Remote manifest does not exist: $resolved" }
    $text = Get-Content -Raw -LiteralPath $resolved
    if ($text -match '[A-Za-z]:\\' -or $text -match 'X-Amz-' -or $text -match '(?i)secretaccesskey\s*[:=]\s*[^\"\s]') {
        throw 'Remote manifest contains a prohibited machine path, signed-query field, or credential-like value.'
    }
    $manifest = $text | ConvertFrom-Json -Depth 30
    Assert-Gate0ExactProperties $manifest @('schemaVersion', 'manifestId', 'sourceInventory', 'storage', 'credentialContract', 'status', 'artifacts', 'limitations') 'Durable artifact manifest'
    Assert-Gate0ExactProperties $manifest.sourceInventory @('path', 'artifactSetId', 'sha256', 'logicalArtifactCount', 'logicalArtifactBytes') 'Durable source-inventory descriptor'
    Assert-Gate0ExactProperties $manifest.storage @('provider', 'bucketName', 'privateBucket', 'automaticDeletionLifecycle', 'objectKeyLayout', 'authoritativeIdentity', 'temporaryProviderReferenceStorage', 'productionReleaseStorage', 'ordinaryPullRequestWriteAccess', 'hostedCiCredentialRequired') 'Durable storage descriptor'
    Assert-Gate0ExactProperties $manifest.credentialContract @('provider', 'credentialType', 'secretNames', 'credentialsCommitted') 'Durable credential contract'
    Assert-Gate0ExactProperties $manifest.status @('retentionCondition', 'secondPrivateCopyVerified', 'verifiedLogicalArtifactCount', 'verifiedLogicalArtifactBytes', 'verifiedDistinctObjectCount', 'verifiedDistinctObjectBytes', 'lastRemoteVerificationUtc', 'blocker') 'Durable retention status'
    if ($manifest.schemaVersion -ne 1 -or $manifest.manifestId -ne 'Gate0.DurableR2Retention.V1') {
        throw 'Unsupported durable artifact manifest.'
    }
    if ($manifest.storage.provider -ne 'cloudflare-r2' -or $manifest.storage.bucketName -ne $script:BucketName -or
        -not $manifest.storage.privateBucket -or $manifest.storage.automaticDeletionLifecycle) {
        throw 'Durable artifact manifest does not preserve the approved private R2 boundary.'
    }
    if ($manifest.storage.objectKeyLayout -ne 'objects/sha256/<first-two-lowercase-hex>/<full-lowercase-sha256>') {
        throw 'Durable artifact manifest has an unsupported object-key layout.'
    }
    if ($manifest.sourceInventory.path -ne 'eng/gate0/artifact-retention-manifest.json' -or
        $manifest.storage.authoritativeIdentity -ne 'sha256-content-addressed-object-key' -or
        $manifest.storage.temporaryProviderReferenceStorage -or $manifest.storage.productionReleaseStorage -or
        $manifest.storage.ordinaryPullRequestWriteAccess -or $manifest.storage.hostedCiCredentialRequired) {
        throw 'Durable artifact manifest widened its approved storage or CI boundary.'
    }
    if ($manifest.credentialContract.provider -ne 'Windows Credential Manager' -or
        $manifest.credentialContract.credentialType -ne 'Generic' -or $manifest.credentialContract.credentialsCommitted -or
        (@($manifest.credentialContract.secretNames) -join '|') -ne (@($script:AccountIdSecretName, $script:AccessKeyIdSecretName, $script:SecretAccessKeySecretName) -join '|')) {
        throw 'Durable artifact manifest changed its approved Windows Credential Manager contract.'
    }
    return [pscustomobject]@{ Path = $resolved; Manifest = $manifest }
}

function Assert-Gate0ManifestPair($SourceInventory, $RemoteManifest) {
    $source = $SourceInventory.Manifest
    $remote = $RemoteManifest.Manifest
    if ($remote.sourceInventory.artifactSetId -ne $source.artifactSetId -or
        $remote.sourceInventory.sha256 -ne $SourceInventory.Sha256 -or
        [int] $remote.sourceInventory.logicalArtifactCount -ne [int] $source.totals.fileCount -or
        [int64] $remote.sourceInventory.logicalArtifactBytes -ne [int64] $source.totals.totalBytes) {
        throw 'Durable artifact manifest is stale relative to the tracked source inventory. Refresh it explicitly before transfer.'
    }

    $sourceById = @{}
    foreach ($entry in @($SourceInventory.Entries)) { $sourceById[$entry.ArtifactId] = $entry }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($record in @($remote.artifacts)) {
        Assert-Gate0ExactProperties $record @(
            'logicalArtifactId', 'purpose', 'originalFilename', 'localRelativePath', 'byteSize', 'sha256',
            'r2ObjectKey', 'sourceProvenance', 'producerRuntimeIdentity', 'proofRunOrContractIdentity',
            'licenseProvenanceRecords', 'retentionStatus', 'transferDisposition', 'remotelyVerifiedUtc'
        ) 'Durable artifact receipt'
        $id = [string] $record.logicalArtifactId
        if (-not $seen.Add($id) -or -not $sourceById.ContainsKey($id)) { throw "Unknown or duplicate durable artifact record: $id" }
        $entry = $sourceById[$id]
        if ([int64] $record.byteSize -ne $entry.Size -or [string] $record.sha256 -ne $entry.Sha256 -or
            [string] $record.r2ObjectKey -ne $entry.ObjectKey -or [string] $record.localRelativePath -ne $entry.LocalRelativePath -or
            [string] $record.purpose -ne $entry.Purpose -or [string] $record.originalFilename -ne $entry.OriginalFilename -or
            [string] $record.sourceProvenance -ne $entry.SourceProvenance -or
            (@($record.producerRuntimeIdentity) -join "`n") -ne (@($entry.ProducerRuntimeIdentity) -join "`n") -or
            (@($record.proofRunOrContractIdentity) -join "`n") -ne (@($entry.ProofRunOrContractIdentity) -join "`n") -or
            (@($record.licenseProvenanceRecords) -join "`n") -ne (@($entry.LicenseProvenanceRecords) -join "`n")) {
            throw "Durable artifact record drifted from the source inventory: $id"
        }
        if ($record.retentionStatus -ne 'remote-verified') { throw "Unsupported durable artifact retention status: $id" }
        if ($record.transferDisposition -notin @('uploaded-and-verified', 'existing-object-verified', 'concurrent-create-verified', 'deduplicated-object-verified-in-this-run', 'independently-retrieved-and-verified')) {
            throw "Unsupported durable artifact transfer disposition: $id"
        }
        $verifiedTimestamp = [DateTimeOffset]::MinValue
        if (-not [DateTimeOffset]::TryParse([string] $record.remotelyVerifiedUtc, [ref] $verifiedTimestamp)) {
            throw "Invalid durable artifact verification timestamp: $id"
        }
    }

    $records = @($remote.artifacts)
    $distinct = @($records | Group-Object r2ObjectKey | ForEach-Object { $_.Group[0] })
    $logicalBytes = if ($records.Count -eq 0) { [int64] 0 } else { [int64] (($records | Measure-Object byteSize -Sum).Sum) }
    $distinctBytes = if ($distinct.Count -eq 0) { [int64] 0 } else { [int64] (($distinct | Measure-Object byteSize -Sum).Sum) }
    $complete = $records.Count -eq [int] $source.totals.fileCount
    if ([int] $remote.status.verifiedLogicalArtifactCount -ne $records.Count -or
        [int64] $remote.status.verifiedLogicalArtifactBytes -ne $logicalBytes -or
        [int] $remote.status.verifiedDistinctObjectCount -ne $distinct.Count -or
        [int64] $remote.status.verifiedDistinctObjectBytes -ne $distinctBytes -or
        [bool] $remote.status.secondPrivateCopyVerified -ne $complete -or
        [string] $remote.status.retentionCondition -ne $(if ($complete) { 'complete' } else { 'incomplete' })) {
        throw 'Durable artifact status is inconsistent with its verified receipt set.'
    }
    if (($complete -and -not [string]::IsNullOrWhiteSpace([string] $remote.status.blocker)) -or
        (-not $complete -and [string]::IsNullOrWhiteSpace([string] $remote.status.blocker))) {
        throw 'Durable artifact completion blocker is inconsistent with its verified receipt set.'
    }
    if (($records.Count -eq 0 -and $null -ne $remote.status.lastRemoteVerificationUtc) -or
        ($records.Count -ne 0 -and [string]::IsNullOrWhiteSpace([string] $remote.status.lastRemoteVerificationUtc))) {
        throw 'Durable artifact last-verification status is inconsistent with its verified receipt set.'
    }
    if ($records.Count -ne 0) {
        $statusTimestamp = [DateTimeOffset]::MinValue
        if (-not [DateTimeOffset]::TryParse([string] $remote.status.lastRemoteVerificationUtc, [ref] $statusTimestamp)) {
            throw 'Durable artifact last-verification timestamp is invalid.'
        }
        $latestReceipt = @($records | ForEach-Object { [DateTimeOffset]::Parse([string] $_.remotelyVerifiedUtc) } | Sort-Object -Descending)[0]
        if ($statusTimestamp -ne $latestReceipt) { throw 'Durable artifact last-verification timestamp does not match its latest receipt.' }
    }
}

function Get-Gate0LocalArtifactPath($Entry, [string] $ArtifactRoot) {
    $relative = ([string] $Entry.LocalRelativePath).Replace('/', [IO.Path]::DirectorySeparatorChar)
    $path = [IO.Path]::GetFullPath((Join-Path $ArtifactRoot $relative))
    if (-not $path.StartsWith("$ArtifactRoot$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase)) {
        throw "Artifact escaped the local root: $($Entry.ArtifactId)"
    }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Local artifact is missing: $($Entry.ArtifactId)" }
    Assert-Gate0NoReparsePointAncestors $path $ArtifactRoot
    $item = Get-Item -LiteralPath $path -Force
    $hash = Get-Gate0Sha256 $path
    if ($item.Length -ne $Entry.Size -or $hash -ne $Entry.Sha256) {
        throw "Local artifact failed size or SHA-256 verification: $($Entry.ArtifactId)"
    }
    return $item.FullName
}

function Get-Gate0R2Configuration {
    $names = @($script:AccountIdSecretName, $script:AccessKeyIdSecretName, $script:SecretAccessKeySecretName)
    $values = @{}
    foreach ($name in $names) {
        $values[$name] = [ReelForge.Gate0.Artifacts.Gate0WindowsCredentialReader]::ReadRequired($name)
    }
    $accountId = $values[$script:AccountIdSecretName]
    if ($accountId -notmatch '^[A-Fa-f0-9]{32}$') { throw "Windows Generic Credential '$script:AccountIdSecretName' is not a 32-character Cloudflare account ID." }
    return [pscustomobject]@{
        BucketName = $script:BucketName
        Endpoint = [Uri] "https://$($accountId.ToLowerInvariant()).r2.cloudflarestorage.com/"
        AccessKeyId = $values[$script:AccessKeyIdSecretName]
        SecretAccessKey = $values[$script:SecretAccessKeySecretName]
    }
}

function New-Gate0R2ClientBundle {
    $configuration = Get-Gate0R2Configuration
    $httpClient = [Net.Http.HttpClient]::new()
    $httpClient.Timeout = [TimeSpan]::FromMinutes(30)
    $client = [ReelForge.Gate0.Artifacts.Gate0ArtifactR2Client]::new(
        $httpClient,
        $configuration.Endpoint,
        $configuration.AccessKeyId,
        $configuration.SecretAccessKey)
    # Do not return the credential-bearing configuration object. The signer owns
    # the credentials after construction; callers receive only non-secret state.
    return [pscustomobject]@{ BucketName = $configuration.BucketName; HttpClient = $httpClient; Client = $client }
}

function Test-Gate0DownloadedArtifact($Entry, [string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "R2 retrieval produced no artifact bytes: $($Entry.ArtifactId)" }
    $item = Get-Item -LiteralPath $Path -Force
    $hash = Get-Gate0Sha256 $Path
    if ($item.Length -ne $Entry.Size -or $hash -ne $Entry.Sha256) {
        throw "R2 artifact failed downloaded size or SHA-256 verification: $($Entry.ArtifactId)"
    }
}

function Invoke-Gate0RemoteByteVerification($ClientBundle, $Entry) {
    $directory = Join-Path ([IO.Path]::GetTempPath()) "ReelForge-Gate0R2Verify-$([Guid]::NewGuid().ToString('N'))"
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $path = Join-Path $directory 'artifact.bin'
    try {
        [void] $ClientBundle.Client.DownloadObjectAsync(
            $ClientBundle.BucketName,
            $Entry.ObjectKey,
            $path).GetAwaiter().GetResult()
        Test-Gate0DownloadedArtifact $Entry $path
    }
    finally {
        if (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path -Force }
        if (Test-Path -LiteralPath $directory -PathType Container) { Remove-Item -LiteralPath $directory -Force }
    }
}

function Set-Gate0DerivedRetentionStatus($Manifest, [string] $IncompleteBlocker, [string] $LastVerifiedUtc) {
    $records = @($Manifest.artifacts)
    $distinct = @($records | Group-Object r2ObjectKey | ForEach-Object { $_.Group[0] })
    $complete = $records.Count -eq [int] $Manifest.sourceInventory.logicalArtifactCount
    $Manifest.status.verifiedLogicalArtifactCount = $records.Count
    $Manifest.status.verifiedLogicalArtifactBytes = if ($records.Count -eq 0) { [int64] 0 } else { [int64] (($records | Measure-Object byteSize -Sum).Sum) }
    $Manifest.status.verifiedDistinctObjectCount = $distinct.Count
    $Manifest.status.verifiedDistinctObjectBytes = if ($distinct.Count -eq 0) { [int64] 0 } else { [int64] (($distinct | Measure-Object byteSize -Sum).Sum) }
    $Manifest.status.secondPrivateCopyVerified = $complete
    $Manifest.status.retentionCondition = if ($complete) { 'complete' } else { 'incomplete' }
    $Manifest.status.blocker = if ($complete) { $null } else { $IncompleteBlocker }
    if (-not [string]::IsNullOrWhiteSpace($LastVerifiedUtc)) { $Manifest.status.lastRemoteVerificationUtc = $LastVerifiedUtc }
}

function Set-Gate0RemoteVerified($RemoteManifest, $Entry, [string] $Disposition, [string] $VerifiedUtc) {
    $manifest = $RemoteManifest.Manifest
    $remaining = @($manifest.artifacts | Where-Object { $_.logicalArtifactId -ne $Entry.ArtifactId })
    $record = [ordered]@{
        logicalArtifactId = $Entry.ArtifactId
        purpose = $Entry.Purpose
        originalFilename = $Entry.OriginalFilename
        localRelativePath = $Entry.LocalRelativePath
        byteSize = $Entry.Size
        sha256 = $Entry.Sha256
        r2ObjectKey = $Entry.ObjectKey
        sourceProvenance = $Entry.SourceProvenance
        producerRuntimeIdentity = @($Entry.ProducerRuntimeIdentity)
        proofRunOrContractIdentity = @($Entry.ProofRunOrContractIdentity)
        licenseProvenanceRecords = @($Entry.LicenseProvenanceRecords)
        retentionStatus = 'remote-verified'
        transferDisposition = $Disposition
        remotelyVerifiedUtc = $VerifiedUtc
    }
    $manifest.artifacts = @($remaining + [pscustomobject] $record | Sort-Object logicalArtifactId)
    Set-Gate0DerivedRetentionStatus $manifest 'Not every source-inventory artifact has completed independent R2 byte verification.' $VerifiedUtc
}

function Write-Gate0RemoteManifest($RemoteManifest) {
    $path = $RemoteManifest.Path
    $temporary = "$path.tmp-$([Guid]::NewGuid().ToString('N'))"
    try {
        $json = ($RemoteManifest.Manifest | ConvertTo-Json -Depth 30) + [Environment]::NewLine
        [IO.File]::WriteAllText($temporary, $json, [Text.UTF8Encoding]::new($false))
        $maximumAttempts = 20
        for ($attempt = 1; $attempt -le $maximumAttempts; $attempt++) {
            try {
                [IO.File]::Move($temporary, $path, $true)
                break
            }
            catch {
                $cause = $_.Exception
                while ($null -ne $cause.InnerException) { $cause = $cause.InnerException }
                $retryable = $cause -is [IO.IOException] -or $cause -is [UnauthorizedAccessException]
                if (-not $retryable -or $attempt -eq $maximumAttempts) { throw }
                Start-Sleep -Milliseconds ([Math]::Min(1000, 100 * $attempt))
            }
        }
    }
    finally {
        if (Test-Path -LiteralPath $temporary -PathType Leaf) { Remove-Item -LiteralPath $temporary -Force }
    }
}

function Invoke-Gate0LockedManifestMutation([string] $ManifestPath, [scriptblock] $Mutation) {
    $resolved = [IO.Path]::GetFullPath($ManifestPath)
    $identityBytes = [Text.Encoding]::UTF8.GetBytes($resolved.ToUpperInvariant())
    $identity = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($identityBytes))
    $mutex = [Threading.Mutex]::new($false, "Local\ReelForge.Gate0ArtifactManifest.$identity")
    $acquired = $false
    try {
        try { $acquired = $mutex.WaitOne([TimeSpan]::FromSeconds(30)) }
        catch [Threading.AbandonedMutexException] { $acquired = $true }
        if (-not $acquired) { throw 'Timed out waiting for the durable artifact manifest mutation lock.' }
        return & $Mutation
    }
    finally {
        if ($acquired) { $mutex.ReleaseMutex() }
        $mutex.Dispose()
    }
}

function Save-Gate0RemoteVerifiedReceipt($SourceInventory, [string] $ManifestPath, $Entry, [string] $Disposition, [string] $VerifiedUtc) {
    return Invoke-Gate0LockedManifestMutation $ManifestPath {
        $current = Read-Gate0RemoteManifest ([IO.Path]::GetFullPath($ManifestPath))
        Assert-Gate0ManifestPair $SourceInventory $current
        Set-Gate0RemoteVerified $current $Entry $Disposition $VerifiedUtc
        Write-Gate0RemoteManifest $current
        return Read-Gate0RemoteManifest ([IO.Path]::GetFullPath($ManifestPath))
    }
}

function Update-Gate0RemoteSourceInventoryUnlocked($SourceInventory, $RemoteManifest) {
    $sourceById = @{}
    foreach ($entry in @($SourceInventory.Entries)) { $sourceById[$entry.ArtifactId] = $entry }
    foreach ($record in @($RemoteManifest.Manifest.artifacts)) {
        if (-not $sourceById.ContainsKey([string] $record.logicalArtifactId)) {
            throw "Cannot refresh: verified artifact is absent from the current source inventory: $($record.logicalArtifactId)"
        }
        $entry = $sourceById[[string] $record.logicalArtifactId]
        if ($record.sha256 -ne $entry.Sha256 -or [int64] $record.byteSize -ne $entry.Size -or $record.r2ObjectKey -ne $entry.ObjectKey) {
            throw "Cannot refresh: verified artifact identity changed: $($record.logicalArtifactId)"
        }
        $record.purpose = $entry.Purpose
        $record.originalFilename = $entry.OriginalFilename
        $record.localRelativePath = $entry.LocalRelativePath
        $record.sourceProvenance = $entry.SourceProvenance
        $record.producerRuntimeIdentity = @($entry.ProducerRuntimeIdentity)
        $record.proofRunOrContractIdentity = @($entry.ProofRunOrContractIdentity)
        $record.licenseProvenanceRecords = @($entry.LicenseProvenanceRecords)
    }
    $RemoteManifest.Manifest.sourceInventory.artifactSetId = [string] $SourceInventory.Manifest.artifactSetId
    $RemoteManifest.Manifest.sourceInventory.sha256 = $SourceInventory.Sha256
    $RemoteManifest.Manifest.sourceInventory.logicalArtifactCount = [int] $SourceInventory.Manifest.totals.fileCount
    $RemoteManifest.Manifest.sourceInventory.logicalArtifactBytes = [int64] $SourceInventory.Manifest.totals.totalBytes
    Set-Gate0DerivedRetentionStatus $RemoteManifest.Manifest 'The source inventory changed; new or changed artifacts require independent R2 byte verification.' $null
    Write-Gate0RemoteManifest $RemoteManifest
}

function Update-Gate0RemoteSourceInventory($SourceInventory, [string] $ManifestPath) {
    return Invoke-Gate0LockedManifestMutation $ManifestPath {
        $current = Read-Gate0RemoteManifest ([IO.Path]::GetFullPath($ManifestPath))
        Update-Gate0RemoteSourceInventoryUnlocked $SourceInventory $current
        return Read-Gate0RemoteManifest ([IO.Path]::GetFullPath($ManifestPath))
    }
}

Export-ModuleMember -Function @(
    'Assert-Gate0ManifestPair',
    'Get-Gate0LocalArtifactPath',
    'Get-Gate0ObjectKey',
    'Get-Gate0Sha256',
    'Invoke-Gate0RemoteByteVerification',
    'New-Gate0R2ClientBundle',
    'Read-Gate0RemoteManifest',
    'Read-Gate0SourceInventory',
    'Resolve-Gate0ArtifactRoot',
    'Save-Gate0RemoteVerifiedReceipt',
    'Test-Gate0DownloadedArtifact',
    'Update-Gate0RemoteSourceInventory'
)
