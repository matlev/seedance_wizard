Set-StrictMode -Version Latest

function Get-G05Stage2ASha256([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Required file is missing: $Path" }
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Read-G05Stage2AExecutionAuthorization([string] $Path, [string] $RepositoryRoot) {
    $authorization = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -Depth 16
    Assert-G05Stage2AExactProperties $authorization @('schemaVersion','authorizationId','status','exactCellCount','exactAttemptCount','bindings','limitations') 'Stage 2A execution authorization'
    if ($authorization.schemaVersion -ne 1 -or $authorization.authorizationId -ne 'Gate0.G05.Stage2A.ExecutionAuthorization.V1' -or
        [string]$authorization.status -notin @('owner-authorized-execution-implementation-pending','owner-authorized-and-prerequisites-verified') -or
        [int]$authorization.exactCellCount -ne 18 -or [int]$authorization.exactAttemptCount -ne 108) {
        throw 'Stage 2A execution authorization identity, status, or exact matrix counts are invalid.'
    }
    $expected = [ordered]@{
        'owner-decision' = 'docs/gate-0-g0.5-stage2a-owner-decisions.md'
        schedule = 'eng/gate0/g0.5-stage2a-schedule.json'
        runner = 'eng/gate0/Invoke-G05Stage2AMatrix.ps1'
        preflight = 'eng/gate0/Test-G05Stage2AMatrixPreflight.ps1'
        helper = 'eng/gate0/G05Stage2AMatrixHelpers.psm1'
        'semantic-executor' = 'eng/gate0/G05Stage2ASemanticExecutor.psm1'
        'semantic-helper' = 'eng/gate0/G05Stage2ASemanticHelpers.psm1'
        'smoke-helper' = 'eng/gate0/G05Stage2SmokeHelpers.psm1'
        'runtime-validator' = 'eng/gate0/Validate-P2Runtime.ps1'
        'runtime-manifest' = 'eng/gate0/manifests/p2-btbn-lgplv3-shared-windows-x64-20260820.json'
        'workload-contract' = 'eng/gate0/g0.5-stage2-workload-contract.json'
        'containment-contract' = 'eng/gate0/g0.5-stage2-containment-dry-run-contract.json'
        'audio-oracle-contract' = 'eng/gate0/g0.5-lossy-audio-oracle-contract.json'
        'retention-contract' = 'eng/gate0/g0.5-stage2a-retention-contract.json'
        'evidence-writer' = 'eng/gate0/Add-Gate0EvidenceShard.ps1'
        'evidence-containment' = 'eng/gate0/evidence/Gate0EvidenceContainment.psm1'
    }
    if (@($authorization.bindings).Count -ne $expected.Count) { throw 'Stage 2A execution authorization does not have the exact required binding count.' }
    foreach ($role in $expected.Keys) {
        $binding = @($authorization.bindings | Where-Object { $_.role -eq $role })
        if ($binding.Count -ne 1) { throw "Stage 2A execution authorization is missing or duplicates the $role binding." }
        Assert-G05Stage2AExactProperties $binding[0] @('role','path','sha256') "Stage 2A $role authorization binding"
        if ([string]$binding[0].path -ne [string]$expected[$role] -or [string]$binding[0].sha256 -notmatch '^[A-F0-9]{64}$') { throw "Stage 2A $role authorization binding path or hash is invalid." }
        $boundPath = Join-Path $RepositoryRoot ([string]$binding[0].path).Replace('/', [IO.Path]::DirectorySeparatorChar)
        if ((Get-G05Stage2ASha256 $boundPath) -ne [string]$binding[0].sha256) { throw "Stage 2A $role authorization binding changed." }
    }
    [pscustomobject]@{ Authorization=$authorization; Sha256=(Get-G05Stage2ASha256 $Path) }
}

function Assert-G05Stage2AExactProperties([object] $Value, [string[]] $Expected, [string] $Label) {
    if ($null -eq $Value) { throw "$Label is missing." }
    $actual = @($Value.PSObject.Properties.Name | Sort-Object)
    $wanted = @($Expected | Sort-Object)
    if (@(Compare-Object -ReferenceObject $wanted -DifferenceObject $actual).Count -ne 0) { throw "$Label does not match its closed schema." }
}

function Read-G05Stage2ASchedule([string] $Path) {
    $raw = Get-Content -LiteralPath $Path -Raw
    $schedule = $raw | ConvertFrom-Json -Depth 32
    Assert-G05Stage2AExactProperties $schedule @('schemaVersion','scheduleId','status','groupOrder','candidateRotations','attempts','limitations') 'Stage 2A schedule'
    if ($schedule.schemaVersion -ne 1 -or $schedule.scheduleId -ne 'Gate0.G05.Stage2A.Schedule.V1' -or $schedule.status -ne 'owner-approved-fixed-before-media') { throw 'Stage 2A schedule identity is invalid.' }
    if (@($schedule.groupOrder).Count -ne 6 -or @($schedule.candidateRotations).Count -ne 3 -or @($schedule.attempts).Count -ne 108) { throw 'Stage 2A schedule does not contain the approved 6-group/108-attempt shape.' }
    $expectedGroups = @('baseline-720p','typical-720p','stress-720p','baseline-1080p','typical-1080p','stress-1080p')
    $expectedWorkloads = @('baseline-1v1a','typical-2v4a','stress-4v8a','baseline-1v1a','typical-2v4a','stress-4v8a')
    $expectedResolutions = @('720p','720p','720p','1080p','1080p','1080p')
    if ((@($schedule.groupOrder) -join '|') -ne ($expectedGroups -join '|')) { throw 'Stage 2A group order differs from the owner-approved order.' }
    $expectedRotations = @('mp4-one|webm-one|webm-eight','webm-one|webm-eight|mp4-one','webm-eight|mp4-one|webm-one')
    if ((@($schedule.candidateRotations | ForEach-Object { @($_) -join '|' }) -join ';') -ne ($expectedRotations -join ';')) { throw 'Stage 2A candidate rotations differ from the owner-approved rotations.' }
    $seen = [Collections.Generic.HashSet[int]]::new()
    for ($i = 0; $i -lt 108; $i++) {
        $attempt = $schedule.attempts[$i]
        Assert-G05Stage2AExactProperties $attempt @('globalOrdinal','groupOrdinal','groupId','cellId','workloadId','resolutionId','candidateId','routeId','threadPolicyId','phase','cellAttemptOrdinal','phaseOrdinal') "Stage 2A attempt $($i + 1)"
        if ([int]$attempt.globalOrdinal -ne $i + 1 -or -not $seen.Add([int]$attempt.globalOrdinal)) { throw 'Stage 2A global attempt order is not contiguous and unique.' }
        $groupIndex = [int]$attempt.groupOrdinal - 1
        if ($groupIndex -lt 0 -or $groupIndex -ge 6 -or [string]$attempt.groupId -ne $expectedGroups[$groupIndex] -or
            [string]$attempt.workloadId -ne $expectedWorkloads[$groupIndex] -or [string]$attempt.resolutionId -ne $expectedResolutions[$groupIndex]) {
            throw 'Stage 2A attempt has an invalid group, workload, or resolution binding.'
        }
        $slot = ($i % 18) % 6
        $candidateIndex = [int][Math]::Floor((($i % 18) / 6))
        $expectedCandidate = $expectedRotations[$groupIndex % 3].Split('|')[$candidateIndex]
        $expectedPhaseOrdinal = if ($slot -eq 0) { 1 } else { $slot }
        $expectedRoute = if ($expectedCandidate -eq 'mp4-one') { 'mp4-openh264-aac' } else { 'webm-vp9-opus' }
        $expectedPolicy = if ($expectedCandidate -eq 'webm-eight') { 'half-logical' } else { 'one' }
        if ([string]$attempt.candidateId -ne $expectedCandidate -or [string]$attempt.routeId -ne $expectedRoute -or [string]$attempt.threadPolicyId -ne $expectedPolicy -or [string]$attempt.cellId -ne "$($expectedGroups[$groupIndex])-$expectedCandidate" -or (($slot -eq 0 -and $attempt.phase -ne 'warmup') -or ($slot -gt 0 -and $attempt.phase -ne 'measured')) -or [int]$attempt.cellAttemptOrdinal -ne ($slot + 1) -or [int]$attempt.phaseOrdinal -ne $expectedPhaseOrdinal) { throw 'Stage 2A attempt ordering does not preserve the fixed per-cell sequence.' }
    }
    [pscustomobject]@{ Schedule = $schedule; Sha256 = Get-G05Stage2ASha256 $Path }
}

function Get-G05Stage2AStatistics([double[]] $MeasuredValues) {
    if ($MeasuredValues.Count -ne 5 -or @($MeasuredValues | Where-Object { -not [double]::IsFinite($_) }).Count -ne 0) { throw 'Stage 2A statistics require exactly five finite measured values.' }
    $sorted = @($MeasuredValues | Sort-Object)
    $median = [double]$sorted[2]
    $deviations = @($MeasuredValues | ForEach-Object { [Math]::Abs($_ - $median) } | Sort-Object)
    [ordered]@{
        observations = @($MeasuredValues)
        minimum = [double]$sorted[0]
        maximum = [double]$sorted[-1]
        range = [double]($sorted[-1] - $sorted[0])
        median = $median
        medianAbsoluteDeviation = [double]$deviations[2]
        observationCount = 5
        warmupExcluded = $true
    }
}

function Get-G05Stage2AReservation([int64] $CurrentRetainedBytes, [int64] $ExpectedOrdinaryClosureBytes, [int64] $ExpectedCompactRepeatBytes, [int64] $ExpectedExceptionalClosureBytes) {
    $ceiling = [int64]805306368
    foreach ($value in @($CurrentRetainedBytes,$ExpectedOrdinaryClosureBytes,$ExpectedCompactRepeatBytes,$ExpectedExceptionalClosureBytes)) { if ($value -lt 0) { throw 'Stage 2A reservation values must be non-negative.' } }
    $required = $ExpectedOrdinaryClosureBytes + (5 * $ExpectedCompactRepeatBytes) + $ExpectedExceptionalClosureBytes
    [ordered]@{ ceilingBytes=$ceiling; currentRetainedBytes=$CurrentRetainedBytes; requiredForNextCellBytes=$required; remainingAfterReservationBytes=$ceiling-$CurrentRetainedBytes-$required; passed=($CurrentRetainedBytes+$required -le $ceiling) }
}

function Get-G05Stage2AEnvironmentObservation {
    $memory = Get-CimInstance Win32_OperatingSystem
    $total = [int64]$memory.TotalVisibleMemorySize * 1KB
    $available = [int64]$memory.FreePhysicalMemory * 1KB
    $cpu = (Get-CimInstance Win32_Processor | Measure-Object -Property LoadPercentage -Average).Average
    $power = (& powercfg /GETACTIVESCHEME 2>&1 | Out-String).Trim()
    $displays = @(Get-CimInstance Win32_DesktopMonitor -ErrorAction SilentlyContinue | ForEach-Object { [ordered]@{ availability=$_.Availability; screenWidth=$_.ScreenWidth; screenHeight=$_.ScreenHeight } })
    [ordered]@{
        logicalProcessorCount = [Environment]::ProcessorCount
        totalPhysicalMemoryBytes = $total
        availablePhysicalMemoryBytes = $available
        currentCpuUtilizationPercent = if ($null -eq $cpu) { $null } else { [double]$cpu }
        activeMediaProcesses = @(Get-Process -Name ffmpeg,ffprobe -ErrorAction SilentlyContinue | ForEach-Object { [ordered]@{ id=$_.Id; processName=$_.ProcessName } })
        operatingSystem = [ordered]@{ caption=$memory.Caption; version=$memory.Version; buildNumber=$memory.BuildNumber; architecture=[Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString() }
        powerScheme = $power
        interactiveSession = [Environment]::UserInteractive
        displays = $displays
    }
}

Export-ModuleMember -Function Get-G05Stage2ASha256,Assert-G05Stage2AExactProperties,Read-G05Stage2AExecutionAuthorization,Read-G05Stage2ASchedule,Get-G05Stage2AStatistics,Get-G05Stage2AReservation,Get-G05Stage2AEnvironmentObservation
