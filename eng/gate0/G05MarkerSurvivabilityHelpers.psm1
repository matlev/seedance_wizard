Set-StrictMode -Version Latest

function Get-G05MarkerDecode {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][byte[]] $Bytes,
        [Parameter(Mandatory)][int] $ExpectedFrames,
        [Parameter(Mandatory)][object[]] $FramePts
    )

    $width = 272; $height = 16; $cell = 16; $bits = 17; $frameBytes = $width * $height
    $failures = [Collections.Generic.List[string]]::new()
    if ($Bytes.Length % $frameBytes -ne 0) { $failures.Add('marker-strip-byte-length-is-not-frame-aligned') }
    $actualFrames = [int]($Bytes.Length / $frameBytes)
    if ($actualFrames -ne $ExpectedFrames) { $failures.Add('decoded-marker-frame-count-mismatch') }
    if ($FramePts.Count -ne $ExpectedFrames) { $failures.Add('probe-marker-frame-count-mismatch') }

    $decoded = @(); $seen = [Collections.Generic.HashSet[int]]::new(); $ambiguous = 0
    for ($frame = 0; $frame -lt [Math]::Min($actualFrames, $ExpectedFrames); $frame++) {
        $value = 0; $frameAmbiguous = $false
        for ($bit = 0; $bit -lt $bits; $bit++) {
            $sampleX = ($bit * $cell) + 8; $sampleY = 8
            $sample = $Bytes[($frame * $frameBytes) + ($sampleY * $width) + $sampleX]
            if ($sample -lt 64) { $bitValue = 0 }
            elseif ($sample -gt 192) { $bitValue = 1 }
            else { $frameAmbiguous = $true; $bitValue = 0 }
            $value = ($value -shl 1) -bor $bitValue
        }
        if ($frameAmbiguous) { $ambiguous++ }
        $duplicate = -not $seen.Add($value)
        $decoded += [pscustomobject]@{ frameIndex = $frame; recoveredId = $value; ambiguous = $frameAmbiguous; duplicate = $duplicate; expectedId = $frame; pts = if ($frame -lt $FramePts.Count) { $FramePts[$frame] } else { $null } }
    }
    $misidentified = @($decoded | Where-Object { $_.recoveredId -ne $_.expectedId }).Count
    $duplicates = @($decoded | Where-Object duplicate).Count
    # A collision is deliberately the same condition as duplicate recovery:
    # one recovered ID assigned to more than one sequential expected frame.
    $collisions = $duplicates
    $unexpectedValues = @($decoded | Where-Object { $_.recoveredId -lt 0 -or $_.recoveredId -ge $ExpectedFrames } | Select-Object -ExpandProperty recoveredId -Unique)
    $unexpected = $unexpectedValues.Count
    $expectedSet = [Collections.Generic.HashSet[int]]::new(); for($id=0;$id-lt$ExpectedFrames;$id++){[void]$expectedSet.Add($id)}
    $missingValues = @($expectedSet | Where-Object { -not $seen.Contains($_) })
    $missing = $missingValues.Count
    $badPts = @($decoded | Where-Object { $null -eq $_.pts -or [int64]$_.pts -ne ([int64]$_.frameIndex * 40) }).Count
    if ($ambiguous -ne 0) { $failures.Add('ambiguous-marker-bit-cell') }
    if ($misidentified -ne 0) { $failures.Add('marker-id-misidentification') }
    if ($duplicates -ne 0) { $failures.Add('duplicate-marker-id') }
    if ($collisions -ne 0) { $failures.Add('marker-id-collision') }
    if ($missing -ne 0) { $failures.Add('missing-marker-id') }
    if ($unexpected -ne 0) { $failures.Add('unexpected-marker-id') }
    if ($badPts -ne 0) { $failures.Add('marker-pts-mismatch') }
    [pscustomobject]@{
        passed = ($failures.Count -eq 0); failures = @($failures); geometry = [ordered]@{ stripWidth = $width; stripHeight = $height; bitCells = $bits; cellWidth = $cell; cellHeight = $cell; sample = 'center x=bit*16+8,y=8'; zeroThresholdExclusive = 64; oneThresholdExclusive = 192; ambiguousInclusive = '64..192' }
        expectedFrames = $ExpectedFrames; decodedFrames = $actualFrames; probeFrames = $FramePts.Count; ambiguousFrames = $ambiguous; misidentifiedFrames = $misidentified; duplicateIds = $duplicates; collisions = $collisions; missingIds = $missing; missingValues = $missingValues; unexpectedIds = $unexpected; unexpectedValues = $unexpectedValues; badPts = $badPts; decoded = $decoded
    }
}

function Get-G05DecodedAudioTiming {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][int64] $RawByteLength,
        [Parameter(Mandatory)][object] $AudioStream,
        [Parameter(Mandatory)][object[]] $AudioFrames,
        [Parameter(Mandatory)][object[]] $AudioPackets,
        [int64] $ExpectedSamplesPerChannel = 1440000,
        [int64] $MaximumRawTailSamples = 1024
    )

    $failures = [Collections.Generic.List[string]]::new()
    $rawSamples = if ($RawByteLength % 4 -eq 0) { [int64]($RawByteLength / 4) } else { -1 }
    if ($rawSamples -lt 0) { $failures.Add('decoded-audio-byte-length-is-not-stereo-s16le-aligned') }
    $framesMissingSampleCount = @($AudioFrames | Where-Object { $null -eq $_.PSObject.Properties['nb_samples'] }).Count
    $frameSampleSum = [int64](($AudioFrames | ForEach-Object { if ($null -ne $_.PSObject.Properties['nb_samples']) { [int64]$_.nb_samples } } | Measure-Object -Sum).Sum)
    if ($AudioFrames.Count -eq 0 -or $framesMissingSampleCount -ne 0) { $failures.Add('decoded-audio-frame-sample-evidence-incomplete') }

    $durationSamples = $null
    $durationProperty = $AudioStream.PSObject.Properties['duration_ts']
    $timeBaseProperty = $AudioStream.PSObject.Properties['time_base']
    $durationTicks = [int64]0
    if ($null -ne $durationProperty -and $null -ne $timeBaseProperty -and
        [int64]::TryParse([string]$durationProperty.Value, [Globalization.NumberStyles]::Integer, [Globalization.CultureInfo]::InvariantCulture, [ref]$durationTicks) -and
        [string]$timeBaseProperty.Value -match '^(-?\d+)/(\d+)$' -and [int64]$Matches[2] -ne 0) {
        $durationSamples = [int64][Math]::Round(
            ([decimal]$durationTicks * [int64]$Matches[1] * 48000) / [int64]$Matches[2],
            0,
            [MidpointRounding]::AwayFromZero)
    }

    $endpointSource = if ($durationSamples -eq $ExpectedSamplesPerChannel) { 'stream-duration-ts' }
        elseif ($frameSampleSum -eq $ExpectedSamplesPerChannel) { 'decoded-frame-sample-sum' }
        elseif ($rawSamples -eq $ExpectedSamplesPerChannel) { 'raw-decoder-sample-count' }
        else { $null }
    $rawTail = $rawSamples - $ExpectedSamplesPerChannel
    $firstPacket = if ($AudioPackets.Count) { $AudioPackets[0] } else { $null }
    $finalPacket = if ($AudioPackets.Count) { $AudioPackets[-1] } else { $null }
    $firstFrame = if ($AudioFrames.Count) { $AudioFrames[0] } else { $null }
    $finalFrame = if ($AudioFrames.Count) { $AudioFrames[-1] } else { $null }
    if ($null -eq $firstPacket -or $null -eq $finalPacket -or $null -eq $firstFrame -or $null -eq $finalFrame) { $failures.Add('decoded-audio-packet-frame-boundary-evidence-missing') }

    $skipSamples = 0; $discardPadding = 0
    foreach ($packet in $AudioPackets) {
        $sideProperty = $packet.PSObject.Properties['side_data_list']
        $sideData = if ($null -eq $sideProperty) { @() } else { @($sideProperty.Value) }
        foreach ($side in $sideData) {
            if ($null -ne $side -and [string]$side.side_data_type -eq 'Skip Samples') {
                if ($null -ne $side.PSObject.Properties['skip_samples']) { $skipSamples = [Math]::Max($skipSamples, [int]$side.skip_samples) }
                if ($null -ne $side.PSObject.Properties['discard_padding']) { $discardPadding = [Math]::Max($discardPadding, [int]$side.discard_padding) }
            }
        }
    }
    if ($skipSamples -gt $MaximumRawTailSamples -or $discardPadding -gt $MaximumRawTailSamples) { $failures.Add('decoded-audio-recorded-skip-discard-out-of-range') }

    $finalPacketDurationSamples = $null
    if ($null -ne $finalPacket -and $null -ne $finalPacket.PSObject.Properties['duration'] -and $null -ne $timeBaseProperty -and
        [int64]::TryParse([string]$finalPacket.duration, [Globalization.NumberStyles]::Integer, [Globalization.CultureInfo]::InvariantCulture, [ref]$durationTicks) -and
        [string]$timeBaseProperty.Value -match '^(-?\d+)/(\d+)$' -and [int64]$Matches[2] -ne 0) {
        $finalPacketDurationSamples = [int64][Math]::Round(
            ([decimal]$durationTicks * [int64]$Matches[1] * 48000) / [int64]$Matches[2],
            0,
            [MidpointRounding]::AwayFromZero)
    }
    $finalFrameSamples = if ($null -ne $finalFrame -and $null -ne $finalFrame.PSObject.Properties['nb_samples']) { [int64]$finalFrame.nb_samples } else { $null }
    $tailFromFinalPacketFrame = if ($null -ne $finalPacketDurationSamples -and $null -ne $finalFrameSamples) { $finalFrameSamples - $finalPacketDurationSamples } else { $null }
    $tailMetadataMatched = $rawTail -eq 0 -or $rawTail -eq $discardPadding -or $rawTail -eq $tailFromFinalPacketFrame
    if ($null -eq $endpointSource) { $failures.Add('decoded-audio-presentation-endpoint-mismatch') }
    if ($rawTail -lt 0 -or $rawTail -gt $MaximumRawTailSamples) { $failures.Add('decoded-audio-raw-tail-out-of-range') }
    if (-not $tailMetadataMatched) { $failures.Add('decoded-audio-raw-tail-lacks-exact-metadata') }

    [pscustomobject]@{
        passed = ($failures.Count -eq 0)
        failures = @($failures)
        expectedPresentationSamplesPerChannel = $ExpectedSamplesPerChannel
        maximumRawDecoderTailSamples = $MaximumRawTailSamples
        rawDecodedSamplesPerChannel = $rawSamples
        rawDecoderTailSamples = $rawTail
        decodedFrameSampleSum = $frameSampleSum
        streamDurationSamples = $durationSamples
        endpointSource = $endpointSource
        maximumRecordedSkipSamples = $skipSamples
        maximumRecordedDiscardPaddingSamples = $discardPadding
        finalPacketDurationSamples = $finalPacketDurationSamples
        finalFrameSamples = $finalFrameSamples
        tailFromFinalPacketFrame = $tailFromFinalPacketFrame
        rawTailMetadataMatched = $tailMetadataMatched
        boundaryEvidence = [pscustomobject]@{ firstPacket = $firstPacket; finalPacket = $finalPacket; firstFrame = $firstFrame; finalFrame = $finalFrame }
        proofSideTrimmingPerformed = $false
    }
}

Export-ModuleMember -Function Get-G05MarkerDecode, Get-G05DecodedAudioTiming
