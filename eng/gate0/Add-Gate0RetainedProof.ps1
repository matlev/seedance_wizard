[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ArtifactRoot,
    [Parameter(Mandatory)] [string] $SourceRoot,
    [string] $SourceTrustBoundary = '',
    [Parameter(Mandatory)] [string] $GroupId,
    [Parameter(Mandatory)] [string] $DestinationName,
    [Parameter(Mandatory)] [string] $Provenance,
    [string[]] $ProducerRuntimeIdentity = @(),
    [string[]] $LicenseRecords = @(),
    [Parameter(Mandatory)] [string[]] $ProofRunIdentity,
    [ValidateSet('None','AfterPayloadMove','AfterTrackedManifestWrite','AfterLocalManifestWrite','AfterBothManifestWrites')]
    [string] $FaultInjection = 'None'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-Sha256([string] $Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant() }
function To-PortablePath([string] $Path) { $Path.Replace('\', '/') }
function Assert-NoReparsePoints([string] $Root, [string] $Label) {
    $items = @((Get-Item -LiteralPath $Root -Force)) + @(Get-ChildItem -LiteralPath $Root -Force -Recurse)
    $bad = @($items | Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 })
    if ($bad.Count) { throw "$Label contains a reparse point: $($bad[0].FullName)" }
}
function Assert-NoReparsePointAncestors([string] $Path, [string] $Label, [string] $StopAt = '') {
    $current = Get-Item -LiteralPath $Path -Force
    $resolvedStop = if ([string]::IsNullOrWhiteSpace($StopAt)) { '' } else { [IO.Path]::GetFullPath($StopAt).TrimEnd([IO.Path]::DirectorySeparatorChar) }
    while ($null -ne $current) {
        if (($current.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "$Label has a reparse-point ancestor: $($current.FullName)" }
        if (-not [string]::IsNullOrEmpty($resolvedStop) -and $current.FullName.TrimEnd([IO.Path]::DirectorySeparatorChar).Equals($resolvedStop, [StringComparison]::OrdinalIgnoreCase)) { return }
        $current = if ($current -is [IO.DirectoryInfo]) { $current.Parent } else { $current.Directory }
    }
}
function Assert-SafeRelativePath([string] $Value, [string] $Label) {
    if ([string]::IsNullOrWhiteSpace($Value) -or [IO.Path]::IsPathRooted($Value) -or $Value.Contains('\')) { throw "$Label must be a non-empty portable relative path." }
    foreach ($segment in $Value.Replace('\', '/').Split('/')) {
        if ([string]::IsNullOrEmpty($segment) -or $segment -in @('.', '..')) { throw "$Label must be a non-empty relative path." }
    }
}
function Assert-ApprovedRoot([string] $Candidate, [string] $Approved) {
    if ($Candidate.Equals($Approved, [StringComparison]::OrdinalIgnoreCase)) { return }
    throw "The artifact root must be the approved repository sibling: $Approved"
}
function Write-Utf8Atomic([string] $Path, [string] $Text) {
    $temp = "$Path.tmp-$([Guid]::NewGuid().ToString('N'))"
    try { [IO.File]::WriteAllText($temp, $Text, [Text.UTF8Encoding]::new($false)); [IO.File]::Move($temp, $Path, $true) }
    finally { if (Test-Path -LiteralPath $temp -PathType Leaf) { Remove-Item -LiteralPath $temp -Force } }
}
function Invoke-Validation([string] $Root) {
    & (Join-Path $PSScriptRoot 'Test-Gate0ArtifactRetention.ps1') -ArtifactRoot $Root | Out-Null
}
function Assert-CandidateReferences([object] $Candidate, [string] $Root) {
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in @($Candidate.groups | ForEach-Object files)) { [void] $paths.Add([string] $file.filename) }
    foreach ($group in @($Candidate.groups)) {
        if (@($group.proofRunIdentity).Count -eq 0) { throw "Proof-run identity is missing: $($group.groupId)" }
        foreach ($reference in @($group.producerRuntimeIdentity) + @($group.licenseRecords) + @($group.proofRunIdentity)) {
            $value = [string] $reference
            if ($value.StartsWith('artifact:', [StringComparison]::Ordinal)) {
                if (-not $paths.Contains($value.Substring('artifact:'.Length))) { throw "Candidate artifact reference is not retained: $value" }
            } elseif ($value.StartsWith('repository:', [StringComparison]::Ordinal)) {
                $portableRelative = $value.Substring('repository:'.Length); Assert-SafeRelativePath $portableRelative 'Repository reference'
                $relative = $portableRelative.Replace('/', [IO.Path]::DirectorySeparatorChar)
                $candidate = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $relative))
                if (-not $candidate.StartsWith("$repositoryRoot$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $candidate -PathType Leaf)) { throw "Candidate repository reference is missing or escaped: $value" }
                Assert-NoReparsePointAncestors $candidate 'Repository reference' $repositoryRoot
            } elseif (-not $value.StartsWith('upstream:', [StringComparison]::Ordinal) -and $value -notin @('manifest:p3Authenticode','manifest:p3-proof-status-incomplete')) {
                throw "Candidate reference lacks an approved scope: $value"
            }
        }
    }
}
function ConvertTo-CanonicalJson([object] $Value) {
    if ($null -eq $Value) { return 'null' }
    if ($Value -is [string] -or $Value -is [ValueType]) { return ($Value | ConvertTo-Json -Compress) }
    if ($Value -is [Collections.IEnumerable]) {
        return '[' + ((@($Value | ForEach-Object { ConvertTo-CanonicalJson $_ }) -join ',')) + ']'
    }
    $properties = @($Value.PSObject.Properties | Sort-Object Name | ForEach-Object { ($_.Name | ConvertTo-Json -Compress) + ':' + (ConvertTo-CanonicalJson $_.Value) })
    return '{' + ($properties -join ',') + '}'
}
function Assert-ImmutableCandidateExtension([object] $Old, [object] $Candidate, [object] $Journal) {
    foreach ($name in @('schemaVersion','artifactSetId','storage','anchors','p3Authenticode','limitations')) {
        if ((ConvertTo-CanonicalJson $Old.$name) -ne (ConvertTo-CanonicalJson $Candidate.$name)) { throw "Append journal candidate changed immutable manifest field: $name" }
    }
    $oldGroups = @($Old.groups); $candidateGroups = @($Candidate.groups)
    if ($candidateGroups.Count -ne $oldGroups.Count + 1) { throw 'Append journal candidate is not an exact one-group extension.' }
    for ($index = 0; $index -lt $oldGroups.Count; $index++) {
        if ((ConvertTo-CanonicalJson $oldGroups[$index]) -ne (ConvertTo-CanonicalJson $candidateGroups[$index])) { throw "Append journal candidate changed existing group at index $index." }
    }
    $appended = $candidateGroups[$candidateGroups.Count - 1]
    if ([string]$appended.groupId -ne [string]$Journal.groupId) { throw 'Append journal candidate appended group does not match its journal group ID.' }
    if (@($appended.files).Count -eq 0) { throw 'Append journal candidate appended group contains no retained files.' }
    if (@($appended.proofRunIdentity | Where-Object { ([string]$_).StartsWith('artifact:', [StringComparison]::Ordinal) }).Count -eq 0) { throw 'Append journal appended group proof-run identity must bind retained artifact evidence.' }
    Assert-SafeRelativePath ([string]$Journal.destinationName) 'Journal destinationName'
    $prefix = (To-PortablePath ([string]$Journal.destinationName)).TrimEnd('/') + '/'
    $artifactIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $filenames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $calculatedFiles = 0; $calculatedBytes = [int64]0
    foreach ($group in $candidateGroups) {
        foreach ($file in @($group.files)) {
            Assert-SafeRelativePath ([string]$file.filename) 'Candidate artifact filename'
            if (-not $artifactIds.Add([string]$file.artifactId) -or -not $filenames.Add([string]$file.filename)) { throw 'Append journal candidate has duplicate artifact IDs or filenames.' }
            if ([string]$group.groupId -eq [string]$Journal.groupId -and -not ([string]$file.filename).StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Append journal candidate appended file escaped its immutable destination.' }
            $calculatedFiles++; $calculatedBytes += [int64]$file.size
        }
        $groupFiles = @($group.files).Count; $groupBytes = [int64](($group.files | ForEach-Object { [int64]$_.size } | Measure-Object -Sum).Sum)
        if ($groupFiles -ne [int]$group.fileCount -or $groupBytes -ne [int64]$group.totalBytes) { throw "Append journal candidate group totals are invalid: $($group.groupId)" }
    }
    if ($calculatedFiles -ne [int]$Candidate.totals.fileCount -or $calculatedBytes -ne [int64]$Candidate.totals.totalBytes -or $candidateGroups.Count -ne [int]$Candidate.totals.groupCount) { throw 'Append journal candidate derived totals are invalid.' }
    $allowed = @('schemaVersion','artifactSetId','storage','anchors','p3Authenticode','limitations','groups','generatedUtc','totals')
    foreach ($property in @($Candidate.PSObject.Properties.Name)) { if ($property -notin $allowed) { throw "Append journal candidate has an unexpected manifest field: $property" } }
    foreach ($property in @($Old.PSObject.Properties.Name)) { if ($property -notin $allowed) { throw "Old manifest has an unsupported manifest field: $property" } }
}
function Assert-JournalPayload([object] $Journal, [string] $Root, [object] $Candidate) {
    $group = @($Candidate.groups | Where-Object { $_.groupId -eq $Journal.groupId })
    if ($group.Count -ne 1) { throw 'Append journal candidate does not contain exactly one named appended group.' }
    Assert-SafeRelativePath ([string]$Journal.destinationName) 'Journal destinationName'
    $destinationPrefix = (To-PortablePath ([string]$Journal.destinationName)).TrimEnd('/') + '/'
    foreach ($file in @($group[0].files)) {
        if (-not ([string]$file.filename).StartsWith($destinationPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Append journal group file is outside its immutable destination.' }
        $path = [IO.Path]::GetFullPath((Join-Path $Root ([string]$file.filename).Replace('/', [IO.Path]::DirectorySeparatorChar)))
        if (-not $path.StartsWith("$Root$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'Append journal payload is missing or escaped.' }
        $item = Get-Item -LiteralPath $path -Force
        if ($item.Length -ne [int64]$file.size -or (Get-Sha256 $path) -ne [string]$file.sha256) { throw 'Append journal payload failed hash verification.' }
    }
}
function Assert-JournalStagingPath([object] $Journal, [string] $Root) {
    $staging = [IO.Path]::GetFullPath([string]$Journal.stagingPath).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $expectedPrefix = "$Root.append-staging-"
    if (-not $staging.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetDirectoryName($staging) -ne [IO.Path]::GetDirectoryName($Root) -or
        [IO.Path]::GetPathRoot($staging) -ne [IO.Path]::GetPathRoot($Root)) { throw 'Append journal staging path is not the transaction-owned sibling staging directory.' }
    if (Test-Path -LiteralPath $staging) { Assert-NoReparsePoints $staging 'Append journal staging root'; Assert-NoReparsePointAncestors $staging 'Append journal staging root' ([IO.Path]::GetDirectoryName($Root)) }
    return $staging
}
function Recover-KnownJournal([string] $JournalPath, [string] $Root, [string] $TrackedManifest) {
    if (-not (Test-Path -LiteralPath $JournalPath -PathType Leaf)) { return }
    $journal = Get-Content -LiteralPath $JournalPath -Raw | ConvertFrom-Json -Depth 32
    if ($journal.schemaVersion -ne 1 -or [string]::IsNullOrWhiteSpace([string] $journal.newManifestBase64)) { throw 'Append journal has an unknown schema or lacks a candidate manifest.' }
    $localManifest = Join-Path $Root 'artifact-retention-manifest.json'
    $trackedText = Get-Content -LiteralPath $TrackedManifest -Raw
    $trackedHash = Get-Sha256 $TrackedManifest
    $localHash = if (Test-Path -LiteralPath $localManifest -PathType Leaf) { Get-Sha256 $localManifest } else { '' }
    $newText = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String([string] $journal.newManifestBase64))
    $newHash = (New-Object Security.Cryptography.SHA256Managed).ComputeHash([Text.Encoding]::UTF8.GetBytes($newText)) | ForEach-Object ToString x2
    $newHash = ($newHash -join '').ToUpperInvariant()
    if ($newHash -ne [string] $journal.newManifestSha256) { throw 'Append journal candidate manifest hash does not match its recorded hash.' }
    if ($trackedHash -ne [string]$journal.oldManifestSha256 -and $trackedHash -ne [string]$journal.newManifestSha256) { throw 'Append journal does not bind to the current tracked manifest.' }
    if ([string]::IsNullOrWhiteSpace([string]$journal.oldManifestBase64)) { throw 'Append journal lacks its immutable old manifest.' }
    $oldText = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String([string]$journal.oldManifestBase64))
    $oldHash = ((New-Object Security.Cryptography.SHA256Managed).ComputeHash([Text.Encoding]::UTF8.GetBytes($oldText)) | ForEach-Object ToString x2) -join ''
    if ($oldHash.ToUpperInvariant() -ne [string]$journal.oldManifestSha256) { throw 'Append journal old manifest hash does not match its recorded hash.' }
    $candidate = $newText | ConvertFrom-Json -Depth 32
    $old = $oldText | ConvertFrom-Json -Depth 32
    Assert-ImmutableCandidateExtension $old $candidate $journal
    Assert-CandidateReferences $candidate $Root
    if ($journal.phase -in @('payloadCommitted','manifestCommitted') -and $trackedHash -eq $journal.newManifestSha256 -and $localHash -eq $journal.newManifestSha256) { Assert-JournalPayload $journal $Root $candidate; Invoke-Validation $Root; Remove-Item -LiteralPath $JournalPath -Force; return }
    if ($trackedHash -eq $journal.oldManifestSha256 -and $localHash -eq $journal.oldManifestSha256 -and $journal.phase -in @('prepared','payloadCommitted')) {
        Assert-SafeRelativePath ([string]$journal.destinationName) 'Journal destinationName'
        $destination = [IO.Path]::GetFullPath((Join-Path $Root ([string] $journal.destinationName)))
        if (-not $destination.StartsWith("$Root$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase)) { throw 'Append journal destination escaped the retained root.' }
        if ($journal.phase -eq 'prepared') {
            if (Test-Path -LiteralPath $destination) { Assert-JournalPayload $journal $Root $candidate; Write-Utf8Atomic $TrackedManifest $newText; Write-Utf8Atomic $localManifest $newText; $journal.phase='manifestCommitted'; Write-Utf8Atomic $JournalPath ($journal | ConvertTo-Json -Depth 32); Invoke-Validation $Root; Remove-Item -LiteralPath $JournalPath -Force; return }
            if ($journal.stagingPath) { $safeStaging = Assert-JournalStagingPath $journal $Root; if (Test-Path -LiteralPath $safeStaging -PathType Container) { Remove-Item -LiteralPath $safeStaging -Recurse -Force } }
            Remove-Item -LiteralPath $JournalPath -Force; return
        }
        if (-not (Test-Path -LiteralPath $destination -PathType Container)) { throw 'Payload-committed append journal is missing its immutable destination.' }
        Assert-JournalPayload $journal $Root $candidate
        Write-Utf8Atomic $TrackedManifest $newText; Write-Utf8Atomic $localManifest $newText
        $journal.phase = 'manifestCommitted'; Write-Utf8Atomic $JournalPath ($journal | ConvertTo-Json -Depth 32)
        Invoke-Validation $Root; Remove-Item -LiteralPath $JournalPath -Force; return
    }
    if ($journal.phase -in @('payloadCommitted','manifestCommitted') -and
        (($trackedHash -eq $journal.newManifestSha256 -and $localHash -eq $journal.oldManifestSha256) -or
         ($trackedHash -eq $journal.oldManifestSha256 -and $localHash -eq $journal.newManifestSha256))) {
        Assert-JournalPayload $journal $Root $candidate
        Write-Utf8Atomic $TrackedManifest $newText; Write-Utf8Atomic $localManifest $newText
        $journal.phase = 'manifestCommitted'; Write-Utf8Atomic $JournalPath ($journal | ConvertTo-Json -Depth 32)
        Invoke-Validation $Root; Remove-Item -LiteralPath $JournalPath -Force; return
    }
    throw 'Append journal state is unknown or inconsistent; refusing automatic recovery.'
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..')).TrimEnd([IO.Path]::DirectorySeparatorChar)
$trackedManifest = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'artifact-retention-manifest.json'))
$approvedRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetDirectoryName($repositoryRoot)) 'ReelForge.Gate0Artifacts')).TrimEnd([IO.Path]::DirectorySeparatorChar)
$resolvedRoot = [IO.Path]::GetFullPath($ArtifactRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
Assert-ApprovedRoot $resolvedRoot $approvedRoot
if (-not (Test-Path -LiteralPath $resolvedRoot -PathType Container)) { throw "Artifact root does not exist: $resolvedRoot" }
if (-not (Test-Path -LiteralPath $trackedManifest -PathType Leaf)) { throw 'Tracked artifact manifest does not exist.' }
Assert-SafeRelativePath $DestinationName 'DestinationName'
if ([string]::IsNullOrWhiteSpace($GroupId) -or $GroupId.Contains('/') -or $GroupId.Contains('\')) { throw 'GroupId must be a non-empty immutable identifier, not a path.' }
if ($ProofRunIdentity.Count -eq 0) { throw 'ProofRunIdentity is required.' }
Assert-NoReparsePoints $resolvedRoot 'Retained artifact root'
Assert-NoReparsePointAncestors $resolvedRoot 'Retained artifact root' ([IO.Path]::GetDirectoryName($repositoryRoot))
$source = [IO.Path]::GetFullPath($SourceRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
if (-not (Test-Path -LiteralPath $source -PathType Container)) { throw "Source root does not exist: $source" }
Assert-NoReparsePoints $source 'Source root'
if([string]::IsNullOrWhiteSpace($SourceTrustBoundary)){
    Assert-NoReparsePointAncestors $source 'Source root'
}else{
    $sourceBoundary=[IO.Path]::GetFullPath($SourceTrustBoundary).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $approvedSourceBoundary=[IO.Path]::GetDirectoryName($repositoryRoot)
    if(-not$sourceBoundary.Equals($approvedSourceBoundary,[StringComparison]::OrdinalIgnoreCase)-or-not$source.StartsWith("$sourceBoundary$([IO.Path]::DirectorySeparatorChar)",[StringComparison]::OrdinalIgnoreCase)){throw 'SourceTrustBoundary must be the repository parent and contain SourceRoot.'}
    Assert-NoReparsePointAncestors $source 'Source root' $sourceBoundary
}

$journalPath = "$resolvedRoot.append-journal.json"
$lockPath = "$resolvedRoot.append-lock"
$lock = $null
$staging = $null
if ($FaultInjection -ne 'None') {
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $sentinel = Join-Path $repositoryRoot '.gate0-append-test-sentinel'
    if (-not $repositoryRoot.StartsWith("$temporaryRoot$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase) -or
        -not $resolvedRoot.StartsWith("$temporaryRoot$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $sentinel -PathType Leaf) -or (Test-Path -LiteralPath (Join-Path $repositoryRoot '.git'))) {
        throw 'FaultInjection is permitted only in an isolated copied test repository.'
    }
}
try { $lock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
catch { throw 'Another Gate 0 retained-proof append is active or its exclusive lock cannot be acquired.' }
try {
Recover-KnownJournal $journalPath $resolvedRoot $trackedManifest
Invoke-Validation $resolvedRoot

$manifest = Get-Content -LiteralPath $trackedManifest -Raw | ConvertFrom-Json -Depth 32
if (@($manifest.groups | Where-Object { $_.groupId -eq $GroupId }).Count) { throw "Duplicate immutable group ID: $GroupId" }
$destination = [IO.Path]::GetFullPath((Join-Path $resolvedRoot $DestinationName))
if (-not $destination.StartsWith("$resolvedRoot$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $destination)) { throw 'Destination already exists or escapes the retained root.' }
$existingNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($file in @($manifest.groups | ForEach-Object files)) { [void]$existingNames.Add([string]$file.filename) }
$staging = "$resolvedRoot.append-staging-$([Guid]::NewGuid().ToString('N'))"
if ([IO.Path]::GetPathRoot($staging) -ne [IO.Path]::GetPathRoot($resolvedRoot)) { throw 'Append staging is not on the artifact volume.' }
$records = [Collections.Generic.List[object]]::new()
    [IO.Directory]::CreateDirectory($staging) | Out-Null
    foreach ($file in @(Get-ChildItem -LiteralPath $source -Force -File -Recurse | Sort-Object FullName)) {
        $relative = [IO.Path]::GetRelativePath($source, $file.FullName); Assert-SafeRelativePath (To-PortablePath $relative) 'Source file path'
        $portable = To-PortablePath ([IO.Path]::Combine($DestinationName, $relative))
        if (-not $existingNames.Add($portable)) { throw "Duplicate retained artifact filename: $portable" }
        $target = [IO.Path]::GetFullPath((Join-Path $staging $relative))
        if (-not $target.StartsWith("$staging$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase)) { throw 'Staged target escaped staging root.' }
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
        $sourceHash = Get-Sha256 $file.FullName; Copy-Item -LiteralPath $file.FullName -Destination $target
        $copied = Get-Item -LiteralPath $target -Force
        if ($copied.Length -ne $file.Length -or (Get-Sha256 $target) -ne $sourceHash) { throw "Copy verification failed: $relative" }
        $records.Add([ordered]@{ artifactId = "$GroupId/$((To-PortablePath $relative))"; filename = $portable; size = [int64]$copied.Length; sha256 = $sourceHash })
    }
    if ($records.Count -eq 0) { throw 'Source root contains no files to retain.' }
    $group = [ordered]@{ groupId=$GroupId; provenance=$Provenance; producerRuntimeIdentity=@($ProducerRuntimeIdentity); licenseRecords=@($LicenseRecords); proofRunIdentity=@($ProofRunIdentity); fileCount=$records.Count; totalBytes=[int64](($records | ForEach-Object { [int64]$_['size'] } | Measure-Object -Sum).Sum); files=@($records) }
    $manifest.groups = @($manifest.groups) + @([pscustomobject]$group)
    $manifest.generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    $manifest.totals = [pscustomobject]@{ groupCount=@($manifest.groups).Count; fileCount=(@($manifest.groups | ForEach-Object files)).Count; totalBytes=[int64](($manifest.groups | ForEach-Object { [int64]$_.totalBytes } | Measure-Object -Sum).Sum) }
    $candidateText = $manifest | ConvertTo-Json -Depth 32
    $oldText = Get-Content -LiteralPath $trackedManifest -Raw
    $oldHash = Get-Sha256 $trackedManifest
    $newHash = ((New-Object Security.Cryptography.SHA256Managed).ComputeHash([Text.Encoding]::UTF8.GetBytes($candidateText)) | ForEach-Object ToString x2) -join ''; $newHash=$newHash.ToUpperInvariant()
    Assert-CandidateReferences $manifest $resolvedRoot
    $journal = [ordered]@{ schemaVersion=1; phase='prepared'; oldManifestSha256=$oldHash; oldManifestBase64=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($oldText)); newManifestSha256=$newHash; newManifestBase64=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($candidateText)); destinationName=(To-PortablePath $DestinationName); stagingPath=$staging; groupId=$GroupId; createdUtc=[DateTimeOffset]::UtcNow.ToString('O') }
    Write-Utf8Atomic $journalPath ($journal | ConvertTo-Json -Depth 32)
    $destinationParent = [IO.Path]::GetDirectoryName($destination)
    if (-not $destinationParent.StartsWith("$resolvedRoot$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase)) { throw 'Destination parent escaped the retained root.' }
    [IO.Directory]::CreateDirectory($destinationParent) | Out-Null
    Assert-NoReparsePointAncestors $destinationParent 'Destination parent' $resolvedRoot
    Move-Item -LiteralPath $staging -Destination $destination
    if ($FaultInjection -eq 'AfterPayloadMove') { throw 'Fault injection: after payload move.' }
    $journal.phase='payloadCommitted'; Write-Utf8Atomic $journalPath ($journal | ConvertTo-Json -Depth 32)
    Write-Utf8Atomic $trackedManifest $candidateText
    if ($FaultInjection -eq 'AfterTrackedManifestWrite') { throw 'Fault injection: after tracked manifest write.' }
    Write-Utf8Atomic (Join-Path $resolvedRoot 'artifact-retention-manifest.json') $candidateText
    if ($FaultInjection -eq 'AfterLocalManifestWrite' -or $FaultInjection -eq 'AfterBothManifestWrites') { throw 'Fault injection: after local manifest write.' }
    $journal.phase='manifestCommitted'; Write-Utf8Atomic $journalPath ($journal | ConvertTo-Json -Depth 32)
    Invoke-Validation $resolvedRoot
    Remove-Item -LiteralPath $journalPath -Force
    [pscustomobject]@{ status='retained'; groupId=$GroupId; destinationName=(To-PortablePath $DestinationName); manifestSha256=$newHash; fileCount=$group.fileCount; totalBytes=$group.totalBytes }
}
catch {
    if ($null -ne $staging -and (Test-Path -LiteralPath $staging -PathType Container)) { Remove-Item -LiteralPath $staging -Recurse -Force }
    throw
}
finally {
    if ($null -ne $lock) { $lock.Dispose() }
}
