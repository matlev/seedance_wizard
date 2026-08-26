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

Export-ModuleMember -Function Get-G05MarkerDecode
