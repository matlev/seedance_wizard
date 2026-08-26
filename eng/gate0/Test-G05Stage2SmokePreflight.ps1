[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ArtifactRoot,
    [Parameter(Mandatory)] [string] $StagingRoot,
    [Parameter(Mandatory)] [string] $OutputDirectory,
    [switch] $EnableTestInjection,
    [string] $TestObservationPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-Sha256([string] $Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-AbsolutePath([string] $Path, [string] $Label) {
    if (-not [IO.Path]::IsPathRooted($Path)) { throw "$Label must be absolute." }
    [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
}

function Assert-NoReparsePoints([string] $Root, [string] $Label) {
    $items = @((Get-Item -LiteralPath $Root -Force)) + @(Get-ChildItem -LiteralPath $Root -Force -Recurse)
    $reparse = @($items | Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 })
    if ($reparse.Count -ne 0) { throw "$Label contains a reparse point: $($reparse[0].FullName)" }
}

function Assert-NoReparsePointAncestors([string] $Path, [string] $StopAt, [string] $Label) {
    $stop = [IO.Path]::GetFullPath($StopAt).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $current = Get-Item -LiteralPath $Path -Force
    while ($null -ne $current) {
        if (($current.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "$Label has a reparse-point ancestor: $($current.FullName)" }
        if ($current.FullName.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar).Equals($stop, [StringComparison]::OrdinalIgnoreCase)) { return }
        $current = $current.Parent
    }
    throw "$Label did not resolve beneath the approved repository-sibling parent."
}

function Assert-ExactRepositorySibling([string] $Candidate, [string] $ExpectedName, [string] $RepositoryParent, [string] $Label) {
    $expected = Join-Path $RepositoryParent $ExpectedName
    if (-not $Candidate.Equals($expected, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label must be the exact approved repository sibling: $ExpectedName"
    }
    if (-not (Test-Path -LiteralPath $Candidate -PathType Container)) { throw "$Label does not exist." }
    Assert-NoReparsePointAncestors $Candidate $RepositoryParent $Label
    Assert-NoReparsePoints $Candidate $Label
}

function Get-PhysicalMemoryObservation {
    if (-not ('Gate0.NativeMemoryStatus' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace Gate0 {
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
  public class MEMORYSTATUSEX {
    public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
    public uint dwMemoryLoad;
    public ulong ullTotalPhys;
    public ulong ullAvailPhys;
    public ulong ullTotalPageFile;
    public ulong ullAvailPageFile;
    public ulong ullTotalVirtual;
    public ulong ullAvailVirtual;
    public ulong ullAvailExtendedVirtual;
  }
  public static class NativeMemoryStatus {
    [DllImport("kernel32.dll", EntryPoint="GlobalMemoryStatusEx", SetLastError=true)] public static extern bool ReadGlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);
  }
}
'@
    }
    $status = [Gate0.MEMORYSTATUSEX]::new()
    if (-not [Gate0.NativeMemoryStatus]::ReadGlobalMemoryStatusEx($status)) { throw 'GlobalMemoryStatusEx failed.' }
    [pscustomobject]@{ totalPhysicalMemoryBytes = [int64]$status.ullTotalPhys; availablePhysicalMemoryBytes = [int64]$status.ullAvailPhys; memoryLoadPercent = [int]$status.dwMemoryLoad }
}

function Get-CommonParentFreeSpace([string] $RepositoryParent) {
    if (-not ('Gate0.DiskFreeSpaceEx' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace Gate0 {
  public static class DiskFreeSpaceEx {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    public static extern bool GetDiskFreeSpaceExW(string directoryName, out ulong freeBytesAvailable, out ulong totalBytes, out ulong totalFreeBytes);
  }
}
'@
    }
    [UInt64] $available = 0; [UInt64] $total = 0; [UInt64] $totalFree = 0
    if (-not [Gate0.DiskFreeSpaceEx]::GetDiskFreeSpaceExW($RepositoryParent, [ref]$available, [ref]$total, [ref]$totalFree)) { throw 'GetDiskFreeSpaceExW failed for the common repository parent.' }
    [int64]$available
}

function Get-CpuUtilizationObservation {
    try {
        $sample = Get-Counter '\\Processor(_Total)\\% Processor Time' -ErrorAction Stop
        [Math]::Round([double]$sample.CounterSamples[0].CookedValue, 3)
    }
    catch { $null }
}

function Convert-ToPortableFailure([string] $Message) {
    # Evidence is tracked/retained; never allow a machine-local path into it.
    $withoutDrivePaths = [regex]::Replace($Message, '(?i)[A-Z]:\\[^\r\n]+', '<absolute-path>')
    [regex]::Replace($withoutDrivePaths, '(?i)\\\\[^\r\n]+', '<absolute-path>')
}

function Assert-ExactProperties([object] $Value, [string[]] $Expected, [string] $Label) {
    if ($null -eq $Value) { throw "$Label is missing." }
    $actual = @($Value.PSObject.Properties.Name | Sort-Object)
    $difference = @(Compare-Object @($Expected | Sort-Object) $actual)
    if ($difference.Count -ne 0) { throw "$Label does not match its closed schema." }
}

function Get-TestObservation([string] $RepositoryRoot, [switch] $Enabled, [string] $Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        if ($Enabled) { throw 'Test injection requires TestObservationPath.' }
        return $null
    }
    if (-not $Enabled -or $env:REELFORGE_GATE0_TEST_INJECTION -ne '1') { throw 'Test observation injection requires both the explicit switch and REELFORGE_GATE0_TEST_INJECTION=1.' }
    $testMarker = Join-Path $RepositoryRoot '.gate0-test-repository-marker'
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    if (-not $RepositoryRoot.StartsWith("$temporaryRoot$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $testMarker -PathType Leaf) -or
        (Get-Content -LiteralPath $testMarker -Raw).Trim() -ne 'test-only') { throw 'Test observation injection is prohibited outside a dedicated temporary copied test repository.' }
    $resolved = Get-AbsolutePath $Path 'TestObservationPath'
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) { throw 'TestObservationPath does not exist.' }
    $observation = Get-Content -LiteralPath $resolved -Raw | ConvertFrom-Json -Depth 8
    foreach ($required in @('totalPhysicalMemoryBytes', 'availablePhysicalMemoryBytes', 'currentCpuUtilizationPercent', 'activeMediaProcesses', 'availableFreeSpaceBytes')) {
        if ($null -eq $observation.PSObject.Properties[$required]) { throw "Test observation is missing $required." }
    }
    $observation
}

function Test-ClosedPreflightPolicy([object] $Preflight) {
    Assert-ExactProperties $Preflight @('schemaVersion','contractId','status','authority','scope','bindings','host','ownerDecisionRequest','storage','requiredChecks','result','nonClaims') 'Smoke preflight contract'
    Assert-ExactProperties $Preflight.scope @('stage','workload','resolution','candidates','mediaExecutionPermitted','preflightExecutionPermitted','smokeAuthorizationClaimPermitted','fullMatrixOrLongFormClaimPermitted') 'Smoke preflight scope'
    foreach ($candidate in @($Preflight.scope.candidates)) { Assert-ExactProperties $candidate @('routeId','threadPolicyId') 'Smoke preflight candidate' }
    Assert-ExactProperties $Preflight.host @('referenceProfileOnly','minimumTotalPhysicalMemoryBytes','minimumAvailablePhysicalMemoryBytes','availableMemoryDerivation','requiredLogicalProcessorCount','activeMediaProcessesPermitted','activeMediaProcessNames','dynamicCpuUtilizationIsRecordedNotGated','publicHardwareMinimumClaimPermitted') 'Smoke preflight host policy'
    Assert-ExactProperties $Preflight.storage @('approvedArtifactRootName','approvedStagingRootName','rootsMustBeRepositorySiblings','reparsePointsPermitted','newSmokeStageMustBeDirectChild','fixedFreeSpaceReserveBytes','smokeRetainedGroupCeilingBytes','smokeScratchCeilingBytes','sameVolumeRequiredFreeBytes','calculation','existingCorpusBytesAreAlreadyAllocated','longFormSizingIncluded') 'Smoke preflight storage policy'
    if ($Preflight.schemaVersion -ne 1 -or $Preflight.contractId -ne 'Gate0.G05.Stage2.SmokePreflight.V1' -or
        $Preflight.status -ne 'owner-approved-execution-authorized' -or -not [bool]$Preflight.scope.preflightExecutionPermitted -or
        [bool]$Preflight.scope.mediaExecutionPermitted -or [bool]$Preflight.scope.smokeAuthorizationClaimPermitted -or
        [bool]$Preflight.scope.fullMatrixOrLongFormClaimPermitted) { throw 'The owner-approved smoke preflight identity or execution boundary changed.' }
    if ($Preflight.scope.workload -ne 'typical-2v4a' -or $Preflight.scope.resolution -ne '1080p') { throw 'The smoke preflight workload scope changed.' }
    if ([int64]$Preflight.host.minimumTotalPhysicalMemoryBytes -ne 32212254720 -or
        [int64]$Preflight.host.minimumAvailablePhysicalMemoryBytes -ne 8589934592 -or
        [int]$Preflight.host.requiredLogicalProcessorCount -ne 16 -or [bool]$Preflight.host.activeMediaProcessesPermitted -or
        -not [bool]$Preflight.host.dynamicCpuUtilizationIsRecordedNotGated -or [bool]$Preflight.host.publicHardwareMinimumClaimPermitted -or
        (@($Preflight.host.activeMediaProcessNames) -join '|') -ne 'ffmpeg|ffprobe') { throw 'The owner-approved host resource policy changed.' }
    if ($Preflight.storage.approvedArtifactRootName -ne 'ReelForge.Gate0Artifacts' -or
        $Preflight.storage.approvedStagingRootName -ne 'ReelForge.Gate0Staging' -or
        -not [bool]$Preflight.storage.rootsMustBeRepositorySiblings -or [bool]$Preflight.storage.reparsePointsPermitted -or
        -not [bool]$Preflight.storage.newSmokeStageMustBeDirectChild -or
        [int64]$Preflight.storage.fixedFreeSpaceReserveBytes -ne 2147483648 -or
        [int64]$Preflight.storage.smokeRetainedGroupCeilingBytes -ne 805306368 -or
        [int64]$Preflight.storage.smokeScratchCeilingBytes -ne 805306368 -or
        [int64]$Preflight.storage.sameVolumeRequiredFreeBytes -ne 3758096384 -or
        -not [bool]$Preflight.storage.existingCorpusBytesAreAlreadyAllocated -or [bool]$Preflight.storage.longFormSizingIncluded) { throw 'The owner-approved storage resource policy changed.' }
}

function Test-ExactContractBindings([object] $Preflight, [object] $Workload, [object] $Preparation, [string] $WorkloadSha256) {
    if ($Preflight.schemaVersion -ne 1 -or $Preflight.contractId -ne 'Gate0.G05.Stage2.SmokePreflight.V1') { throw 'The smoke preflight contract identity is unsupported.' }
    if ($Workload.schemaVersion -ne 1 -or $Workload.contractId -ne 'Gate0.G05.Stage2.Workloads.V1.OwnerApproved.20260826' -or $Workload.status -ne 'owner-approved-prerequisite-execution-authorized-full-matrix-blocked') { throw 'The Stage 2 workload contract identity or status changed.' }
    if ($WorkloadSha256 -ne [string]$Preflight.bindings.workloadContract.sha256) { throw 'Workload contract SHA-256 does not match the preflight contract binding.' }
    if ([string]$Preparation.status -ne [string]$Preflight.bindings.preparationSummary.requiredStatus) { throw 'Preparation summary status is not the required preflight status.' }
    $selectedWorkload = @($Workload.workloads | Where-Object { $_.id -eq [string]$Preflight.scope.workload })
    $variant = if ($selectedWorkload.Count -eq 1) { @($selectedWorkload[0].resolutionVariants | Where-Object { $_.id -eq [string]$Preflight.scope.resolution }) } else { @() }
    if ($selectedWorkload.Count -ne 1 -or $selectedWorkload[0].durationSeconds -ne 30 -or $selectedWorkload[0].evidenceBoundary -ne 'runtime-route' -or
        $variant.Count -ne 1 -or $variant[0].width -ne 1920 -or $variant[0].height -ne 1080 -or
        $selectedWorkload[0].canvas.frameRate -ne '25/1' -or $selectedWorkload[0].canvas.pixelFormat -ne 'yuv420p' -or
        $selectedWorkload[0].canvas.comparisonTimeBase -ne '1/1000') { throw 'The exact 30-second 1080p typical smoke workload is not present.' }
    $smoke = $Workload.matrix.preMatrixSmoke
    if ($smoke.maximumCandidates -ne 3 -or $smoke.workload -ne $Preflight.scope.workload -or $smoke.resolution -ne $Preflight.scope.resolution -or
        $smoke.attemptsPerAdmittedRouteThreadCandidate -ne 1 -or -not [bool]$smoke.retainEveryAttempt -or -not [bool]$smoke.failFastPerRoute) { throw 'The workload pre-matrix smoke definition is not bound to the approved shape.' }
    $actual = @($Preflight.scope.candidates | ForEach-Object { "$($_.routeId)|$($_.threadPolicyId)" })
    $expected = @('mp4-openh264-aac|one', 'webm-vp9-opus|one', 'webm-vp9-opus|half-logical')
    if (($actual.Count -ne $expected.Count) -or (Compare-Object $actual $expected)) { throw 'The preflight candidate set is not the exact approved three-candidate set.' }
    $routes = @{}; foreach ($route in @($Workload.routes)) { $routes[[string]$route.id] = $route }
    $threads = @($Workload.threadPolicies | ForEach-Object { [string]$_.id })
    foreach ($candidate in $Preflight.scope.candidates) {
        if (-not $routes.ContainsKey([string]$candidate.routeId) -or $candidate.threadPolicyId -notin $threads -or
            $candidate.threadPolicyId -notin @($routes[[string]$candidate.routeId].threadPolicies)) { throw 'An approved smoke candidate or its route-specific thread policy is absent from the workload contract.' }
    }
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..')).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$repositoryParent = [IO.Path]::GetDirectoryName($repositoryRoot)
$contractPath = Join-Path $PSScriptRoot 'g0.5-stage2-smoke-preflight-contract.json'
$expectedPreflightContractSha256 = '12852652BD5BBBF720689183FA04DD422AE3A124F5A2ED1E8E65CED4C0CE0B40'
$workloadPath = Join-Path $PSScriptRoot 'g0.5-stage2-workload-contract.json'
$preparationPath = Join-Path $PSScriptRoot 'g0.5-stage2-preparation-result-summary.json'
$outputPath = $null
$failures = [Collections.Generic.List[string]]::new()
$criteria = [ordered]@{}
$observations = [ordered]@{ noMediaInvoked = $true; absolutePathsExcluded = $true }
$started = [DateTimeOffset]::UtcNow
$persistenceFailure = $null

try {
    if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 or later is required.' }
    $artifactPath = Get-AbsolutePath $ArtifactRoot 'ArtifactRoot'
    $stagingPath = Get-AbsolutePath $StagingRoot 'StagingRoot'
    $outputPath = Get-AbsolutePath $OutputDirectory 'OutputDirectory'
    Assert-ExactRepositorySibling $stagingPath 'ReelForge.Gate0Staging' $repositoryParent 'StagingRoot'
    if ([IO.Path]::GetDirectoryName($outputPath).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) -ne $stagingPath) { throw 'OutputDirectory must be a direct child of StagingRoot.' }
    if (Test-Path -LiteralPath $outputPath) { throw 'OutputDirectory must be new.' }
    $outputName = [IO.Path]::GetFileName($outputPath)
    if ([string]::IsNullOrWhiteSpace($outputName) -or $outputName -in @('.', '..')) { throw 'OutputDirectory must have a safe direct-child name.' }
    New-Item -ItemType Directory -Path $outputPath -ErrorAction Stop | Out-Null
    Assert-NoReparsePointAncestors $outputPath $repositoryParent 'OutputDirectory'
    Assert-ExactRepositorySibling $artifactPath 'ReelForge.Gate0Artifacts' $repositoryParent 'ArtifactRoot'
    $criteria.roots = 'passed'

    $preflightBytes = Get-Content -LiteralPath $contractPath -Raw
    $preflight = $preflightBytes | ConvertFrom-Json -Depth 32
    if (-not $EnableTestInjection -and (Get-Sha256 $contractPath) -ne $expectedPreflightContractSha256) { throw 'The owner-approved smoke preflight contract SHA-256 changed.' }
    if (-not [bool]$preflight.scope.preflightExecutionPermitted) { throw 'The owner-approved contract keeps preflight execution disabled.' }
    Test-ClosedPreflightPolicy $preflight
    $workloadSha256 = Get-Sha256 $workloadPath
    $workload = (Get-Content -LiteralPath $workloadPath -Raw | ConvertFrom-Json -Depth 64)
    $preparation = (Get-Content -LiteralPath $preparationPath -Raw | ConvertFrom-Json -Depth 64)
    Test-ExactContractBindings $preflight $workload $preparation $workloadSha256
    $criteria.contractBindings = 'passed'
    $observations.contracts = [ordered]@{ preflightContractSha256 = Get-Sha256 $contractPath; workloadContractSha256 = $workloadSha256; preparationSummarySha256 = Get-Sha256 $preparationPath; scriptSha256 = Get-Sha256 $PSCommandPath }
    $observations.approvedScope = [ordered]@{
        workload = 'typical-2v4a'
        resolution = '1080p'
        candidates = @(
            [ordered]@{ routeId = 'mp4-openh264-aac'; threadPolicyId = 'one' },
            [ordered]@{ routeId = 'webm-vp9-opus'; threadPolicyId = 'one' },
            [ordered]@{ routeId = 'webm-vp9-opus'; threadPolicyId = 'half-logical' }
        )
    }

    $testObservation = Get-TestObservation -RepositoryRoot $repositoryRoot -Enabled:$EnableTestInjection -Path $TestObservationPath
    $observations.testInjectionUsed = ($null -ne $testObservation)
    $retention = & (Join-Path $PSScriptRoot 'Test-Gate0ArtifactRetention.ps1') -ArtifactRoot $artifactPath
    if ($null -eq $retention -or [string]$retention.status -ne 'verified') { throw 'Local artifact retention verification failed.' }
    $criteria.localCorpus = 'passed'; $observations.localCorpus = [ordered]@{
        artifactSetId = [string]$retention.artifactSetId
        status = 'verified'
        groupCount = [int]$retention.groupCount
        fileCount = [int]$retention.fileCount
        totalBytes = [int64]$retention.totalBytes
        manifestSha256 = [string]$retention.manifestSha256
        secondCopyVerified = [bool]$retention.secondCopyVerified
        twoCopyRetentionCondition = [string]$retention.twoCopyRetentionCondition
    }
    $durable = & (Join-Path $PSScriptRoot 'Test-Gate0ArtifactManifest.ps1')
    if ($null -eq $durable -or [string]::IsNullOrWhiteSpace([string]$durable.sourceManifestSha256)) { throw 'Offline durable-manifest binding verification failed.' }
    $criteria.durableManifestBinding = 'passed'; $observations.durableManifestBinding = [ordered]@{
        manifestId = [string]$durable.manifestId
        artifactSetId = [string]$durable.artifactSetId
        sourceManifestSha256 = [string]$durable.sourceManifestSha256
        selectedLogicalArtifactCount = [int]$durable.selectedLogicalArtifactCount
        selectedLogicalArtifactBytes = [int64]$durable.selectedLogicalArtifactBytes
        localByteVerificationPerformed = [bool]$durable.localByteVerificationPerformed
        remoteByteVerificationPerformed = [bool]$durable.remoteByteVerificationPerformed
        recordedRemoteVerifiedLogicalArtifacts = [int]$durable.recordedRemoteVerifiedLogicalArtifacts
        requiredLogicalArtifactCount = [int]$durable.requiredLogicalArtifactCount
        secondPrivateCopyVerified = [bool]$durable.secondPrivateCopyVerified
        retentionCondition = [string]$durable.retentionCondition
    }

    $memory = if ($null -eq $testObservation) { Get-PhysicalMemoryObservation } else { [pscustomobject]@{ totalPhysicalMemoryBytes = [int64]$testObservation.totalPhysicalMemoryBytes; availablePhysicalMemoryBytes = [int64]$testObservation.availablePhysicalMemoryBytes; memoryLoadPercent = $null } }
    $cpu = if ($null -eq $testObservation) { Get-CpuUtilizationObservation } else { [double]$testObservation.currentCpuUtilizationPercent }
    $observations.host = [ordered]@{ logicalProcessorCount = [Environment]::ProcessorCount; totalPhysicalMemoryBytes = $memory.totalPhysicalMemoryBytes; availablePhysicalMemoryBytes = $memory.availablePhysicalMemoryBytes; memoryLoadPercent = $memory.memoryLoadPercent; currentCpuUtilizationPercent = $cpu; cpuUtilizationGated = $false }
    if ([Environment]::ProcessorCount -ne [int]$preflight.host.requiredLogicalProcessorCount) { throw 'Logical processor count does not match the owner reference profile.' }
    if ($memory.totalPhysicalMemoryBytes -lt [int64]$preflight.host.minimumTotalPhysicalMemoryBytes) { throw 'Total physical memory is below the approved preflight minimum.' }
    if ($memory.availablePhysicalMemoryBytes -lt [int64]$preflight.host.minimumAvailablePhysicalMemoryBytes) { throw 'Available physical memory is below the approved preflight minimum.' }
    $criteria.hostCapacity = 'passed'

    $active = @(if ($null -eq $testObservation) { Get-Process -Name $preflight.host.activeMediaProcessNames -ErrorAction SilentlyContinue | Select-Object Id, ProcessName } else { $testObservation.activeMediaProcesses | ForEach-Object { [pscustomobject]@{ Id = [int]$_.id; ProcessName = [string]$_.processName } } })
    if (@($active | Where-Object { $_.ProcessName -notin @('ffmpeg','ffprobe') }).Count -ne 0) { throw 'The media-process observation contains an unsupported process name.' }
    $observations.activeMediaProcesses = @($active | ForEach-Object { [ordered]@{ id = $_.Id; processName = $_.ProcessName } })
    if ($active.Count -ne 0) { throw 'An active ffmpeg or ffprobe process blocks the preflight observation.' }
    $criteria.noActiveMediaProcess = 'passed'

    $availableFreeSpace = if ($null -eq $testObservation) { Get-CommonParentFreeSpace $repositoryParent } else { [int64]$testObservation.availableFreeSpaceBytes }
    $requiredFreeSpace = [int64]$preflight.storage.sameVolumeRequiredFreeBytes
    $observations.storage = [ordered]@{ sameRepositorySiblingVolume = $true; availableFreeSpaceBytes = $availableFreeSpace; requiredFreeSpaceBytes = $requiredFreeSpace; fixedReserveBytes = [int64]$preflight.storage.fixedFreeSpaceReserveBytes; retainedGroupCeilingBytes = [int64]$preflight.storage.smokeRetainedGroupCeilingBytes; scratchCeilingBytes = [int64]$preflight.storage.smokeScratchCeilingBytes }
    if ($availableFreeSpace -lt $requiredFreeSpace) { throw 'The common repository-sibling volume is below the approved free-space floor.' }
    $criteria.storage = 'passed'
}
catch {
    $failures.Add((Convert-ToPortableFailure $_.Exception.Message))
}
finally {
    $completed = [DateTimeOffset]::UtcNow
    if ($null -ne $outputPath -and (Test-Path -LiteralPath $outputPath -PathType Container)) {
        $evidence = [ordered]@{
            schemaId = 'Gate0.G05.Stage2.SmokePreflightEvidence.V1'
            status = if ($failures.Count -eq 0) { 'passed' } else { 'blocked' }
            startedAtUtc = $started.ToString('O')
            completedAtUtc = $completed.ToString('O')
            noMediaInvoked = $true
            smokeAuthorizationClaim = $false
            fullMatrixOrLongFormClaim = $false
            criteria = $criteria
            failures = @($failures)
            observations = $observations
            nonClaims = @(
                'No current ReelForge product rendering, responsiveness, preview, cache, cancellation, or project behavior claim.',
                'No media process was invoked by this preflight.',
                'No full matrix, long-form, shipping-runtime, distribution, patent, legal, or public hardware-floor conclusion.',
                'A passed resource preflight does not replace complete R2 byte verification or authorize the smoke.'
            )
        }
        $destination = Join-Path $outputPath 'g0.5-stage2-smoke-preflight-evidence.json'
        $temporary = Join-Path $outputPath '.g0.5-stage2-smoke-preflight-evidence.tmp'
        try {
            $evidence | ConvertTo-Json -Depth 64 | Set-Content -LiteralPath $temporary -Encoding utf8NoBOM -ErrorAction Stop
            Move-Item -LiteralPath $temporary -Destination $destination -ErrorAction Stop
        }
        catch {
            $persistenceFailure = Convert-ToPortableFailure $_.Exception.Message
        }
    }
}

if ($null -ne $persistenceFailure) { Write-Error ((@($failures) + @("Evidence persistence failed: $persistenceFailure")) -join [Environment]::NewLine); exit 1 }
if ($failures.Count -ne 0) { Write-Error ($failures -join [Environment]::NewLine); exit 1 }
[pscustomobject]@{ status = 'passed'; evidence = 'g0.5-stage2-smoke-preflight-evidence.json'; noMediaInvoked = $true }
