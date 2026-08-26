Set-StrictMode -Version Latest

function Get-G05PropertyValue([object] $Value, [string] $Name) {
    if ($null -eq $Value) { return $null }
    $property = $Value.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    $property.Value
}

function Get-G05MatrixKey([object] $Attempt) {
    "$($Attempt.row.routeId)|$($Attempt.row.resolutionId)|$($Attempt.row.threadPolicyId)|$($Attempt.row.repetitionKind)|$($Attempt.row.repetitionOrdinal)"
}

function Convert-G05OptionalDouble([object] $Value) {
    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string] $Value) -or [string] $Value -eq 'N/A') { return $null }
    [double] $number = 0
    if (-not [double]::TryParse([string] $Value, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref] $number)) { return $null }
    $number
}

function Assert-G05Stage1Matrix([object[]] $Attempts) {
    $routes = @('Video.Export.Compatibility.Mp4H264Aac.P2OpenH264', 'Video.Export.Open.WebmVp9Opus')
    $resolutions = @('720p', '1080p')
    $threadPolicies = @('auto', 'one', 'half-logical', 'full-logical')
    $repetitions = @(@('warmup', 1), @('measured', 1), @('measured', 2))
    $expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($route in $routes) {
        foreach ($resolution in $resolutions) {
            foreach ($threadPolicy in $threadPolicies) {
                foreach ($repetition in $repetitions) {
                    [void] $expected.Add("$route|$resolution|$threadPolicy|$($repetition[0])|$($repetition[1])")
                }
            }
        }
    }

    if ($Attempts.Count -ne 48) { throw "Stage 1 evidence must contain exactly 48 attempts, observed $($Attempts.Count)." }
    $actual = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($attempt in $Attempts) {
        if ($attempt.row.routeId -notin $routes) { throw "Unknown route ID: $($attempt.row.routeId)" }
        $key = Get-G05MatrixKey $attempt
        if (-not $actual.Add($key)) { throw "Duplicate Stage 1 matrix coordinate: $key" }
        $measurement = Get-G05PropertyValue $attempt 'measurement'
        $rawExitCode = Get-G05PropertyValue $measurement 'exitCode'
        [int] $exitCode = -1
        if ($null -eq $rawExitCode -or -not [int]::TryParse([string] $rawExitCode, [Globalization.NumberStyles]::Integer, [Globalization.CultureInfo]::InvariantCulture, [ref] $exitCode)) { throw "Original FFmpeg attempt has no numeric exit code: $key" }
        if ($exitCode -ne 0) { throw "Original FFmpeg attempt did not exit zero: $key" }
        $expectedHistoricalStatus = if ($attempt.row.routeId -eq 'Video.Export.Compatibility.Mp4H264Aac.P2OpenH264') { 'failed' } else { 'passed' }
        if ([string] $attempt.status -ne $expectedHistoricalStatus) { throw "Original Stage 1 status does not match the retained historical result: $key" }
    }
    foreach ($key in $expected) {
        if (-not $actual.Contains($key)) { throw "Missing Stage 1 matrix coordinate: $key" }
    }
}

function Convert-G05TicksToSamples([double] $Ticks, [string] $TimeBase) {
    $parts = $TimeBase.Split('/')
    $numerator = if ($parts.Count -eq 2) { Convert-G05OptionalDouble $parts[0] } else { $null }
    $denominator = if ($parts.Count -eq 2) { Convert-G05OptionalDouble $parts[1] } else { $null }
    if ($null -eq $numerator -or $null -eq $denominator -or $denominator -eq 0) { throw "Invalid audio time base: $TimeBase" }
    [int64] [Math]::Round($Ticks * $numerator / $denominator * 48000, [MidpointRounding]::ToEven)
}

function Get-G05Timestamp([object] $Value) {
    $timestamp = Get-G05PropertyValue $Value 'pts'
    if ($null -eq $timestamp) { $timestamp = Get-G05PropertyValue $Value 'best_effort_timestamp' }
    $timestamp
}

function Get-G05SideDataValues([object[]] $Items) {
    $result = @()
    foreach ($item in $Items) {
        $sideData = Get-G05PropertyValue $item 'side_data_list'
        foreach ($entry in @($sideData)) {
            if (([string] (Get-G05PropertyValue $entry 'side_data_type')) -notmatch 'Skip Samples') { continue }
            $skip = Get-G05PropertyValue $entry 'skip_samples'
            $discard = Get-G05PropertyValue $entry 'discard_padding'
            $result += [ordered]@{
                skipSamples = if ($null -eq $skip) { 0 } else { [int64] $skip }
                discardPaddingSamples = if ($null -eq $discard) { 0 } else { [int64] $discard }
            }
        }
    }
    @($result)
}

function Get-G05AudioTiming([object] $Stream, [object[]] $Packets, [object[]] $Frames, [int] $DecodedSamples) {
    $timeBase = [string] (Get-G05PropertyValue $Stream 'time_base')
    $sideData = Get-G05SideDataValues @($Packets + $Frames)
    $invalidSideData = @($sideData | Where-Object {
        $_.skipSamples -lt 0 -or $_.discardPaddingSamples -lt 0 -or
        $_.skipSamples -gt 1024 -or $_.discardPaddingSamples -gt 1024
    })
    $failures = @()
    if ($invalidSideData.Count -gt 0) { $failures += 'priming-or-discard-padding-out-of-range' }

    $candidates = [ordered]@{}
    $firstFrame = @($Frames | Select-Object -First 1)
    $finalFrame = @($Frames | Select-Object -Last 1)
    if ($firstFrame.Count -eq 1 -and $finalFrame.Count -eq 1 -and -not [string]::IsNullOrWhiteSpace($timeBase)) {
        $firstFrameTimestamp = Convert-G05OptionalDouble (Get-G05Timestamp $firstFrame[0])
        $finalFrameTimestamp = Convert-G05OptionalDouble (Get-G05Timestamp $finalFrame[0])
        $finalFrameSamples = Convert-G05OptionalDouble (Get-G05PropertyValue $finalFrame[0] 'nb_samples')
        if ($null -ne $firstFrameTimestamp -and $null -ne $finalFrameTimestamp -and $null -ne $finalFrameSamples) {
            $frameTicks = [double] $finalFrameTimestamp - [double] $firstFrameTimestamp
            $candidates.decodedFrameSpan = (Convert-G05TicksToSamples $frameTicks $timeBase) + [int64] $finalFrameSamples
        }
    }

    $rawDurationTicks = Get-G05PropertyValue $Stream 'duration_ts'
    $durationTicks = Convert-G05OptionalDouble $rawDurationTicks
    if ($null -ne $durationTicks -and -not [string]::IsNullOrWhiteSpace($timeBase)) {
        $candidates.streamDuration = Convert-G05TicksToSamples ([double] $durationTicks) $timeBase
    }

    $firstPacket = @($Packets | Select-Object -First 1)
    $finalPacket = @($Packets | Select-Object -Last 1)
    if ($firstPacket.Count -eq 1 -and $finalPacket.Count -eq 1 -and -not [string]::IsNullOrWhiteSpace($timeBase)) {
        $firstPacketTimestamp = Convert-G05OptionalDouble (Get-G05Timestamp $firstPacket[0])
        $finalPacketTimestamp = Convert-G05OptionalDouble (Get-G05Timestamp $finalPacket[0])
        $finalPacketDuration = Convert-G05OptionalDouble (Get-G05PropertyValue $finalPacket[0] 'duration')
        if ($null -ne $firstPacketTimestamp -and $null -ne $finalPacketTimestamp -and $null -ne $finalPacketDuration) {
            $packetTicks = ([double] $finalPacketTimestamp - [double] $firstPacketTimestamp) + [double] $finalPacketDuration
            $candidates.packetSpan = Convert-G05TicksToSamples $packetTicks $timeBase
        }
    }

    $endpointSource = $null
    $endpoint = $null
    foreach ($candidateName in @('decodedFrameSpan', 'streamDuration', 'packetSpan')) {
        $candidate = $candidates[$candidateName]
        if ($null -ne $candidate) {
            $endpointSource = switch ($candidateName) {
                'decodedFrameSpan' { 'decoded-frame-span' }
                'streamDuration' { 'stream-duration' }
                'packetSpan' { 'packet-span' }
            }
            $endpoint = [int64] $candidate
            break
        }
    }
    if ($null -eq $endpoint) { $failures += 'presentation-timing-metadata-unavailable' }

    $decodedSampleDelta = $DecodedSamples - 384000
    if ($decodedSampleDelta -ne 0) { $failures += 'content-normalized-sample-count' }
    $endpointDelta = if ($null -eq $endpoint) { $null } else { [Math]::Abs([double] $endpoint - 384000) }
    if ($null -ne $endpointDelta -and $endpointDelta -gt 1) { $failures += 'presentation-endpoint' }

    [int64] $maximumSkip = 0
    [int64] $maximumDiscard = 0
    foreach ($entry in $sideData) {
        $maximumSkip = [Math]::Max($maximumSkip, [int64] $entry.skipSamples)
        $maximumDiscard = [Math]::Max($maximumDiscard, [int64] $entry.discardPaddingSamples)
    }
    [ordered]@{
        rawStreamStartTime = Get-G05PropertyValue $Stream 'start_time'
        rawStreamStartPts = Get-G05PropertyValue $Stream 'start_pts'
        streamDurationTs = $rawDurationTicks
        streamTimeBase = $timeBase
        sideData = $sideData
        maximumRecordedSkipSamples = $maximumSkip
        maximumRecordedDiscardPaddingSamples = $maximumDiscard
        primingGatePassed = $invalidSideData.Count -eq 0
        contentNormalizedStartSample = 0
        proofSideTrimming = 'none'
        endpointSource = $endpointSource
        endpointCandidates = $candidates
        endpointDeltaSamples = $endpointDelta
        decodedSampleDelta = $decodedSampleDelta
        failures = @($failures)
        timingPassed = $failures.Count -eq 0
    }
}

Export-ModuleMember -Function Get-G05MatrixKey, Assert-G05Stage1Matrix, Get-G05AudioTiming
