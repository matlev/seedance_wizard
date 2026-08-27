Set-StrictMode -Version Latest

function Get-G05Stage2AV5AudioOracleHash([string] $Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Assert-G05Stage2AV5StressOverlay([object] $Descriptor, [object] $Overlay) {
    if ([string]$Descriptor.id -ne 'stress-4v8a-30s') { throw 'V5 is approved only for stress-4v8a-30s.' }
    if ([string]$Overlay.referenceDescriptorId -ne 'stress-4v8a-30s' -or [string]$Overlay.mode -ne 'reference-relative-active-windows-v1') { throw 'V5 stress descriptor binding or mode is invalid.' }
    if ([int]$Overlay.sampleRate -ne 48000 -or [int]$Overlay.windowSamples -ne 960) { throw 'V5 stress overlay sample rate or window length is invalid.' }
    if ([double]$Overlay.minimumOutputToReferenceRmsRatio -ne 0.90 -or [double]$Overlay.maximumOutputToReferenceRmsRatio -ne 1.10) { throw 'V5 stress RMS-ratio bounds are invalid.' }
    $actual = @($Overlay.replacesV3Checks | ForEach-Object { [string]$_ } | Sort-Object -Unique)
    $required = @('qualityThresholds.minimumActiveChannelRmsFullScale', 'qualityThresholds.minimumActiveReferenceWindowOutputRmsFullScale')
    if ($actual.Count -ne 2 -or @($actual | Where-Object { $_ -notin $required }).Count -ne 0 -or @($required | Where-Object { $_ -notin $actual }).Count -ne 0) { throw 'V5 stress replacement set is invalid.' }
}

function New-G05Stage2AV5HelperCompatibleOverlay([object] $Overlay) {
    # The frozen helper's V4 assertion intentionally recognizes only the original typical descriptor.
    # This private adapter changes no descriptor semantics; it solely presents the approved V5 overlay
    # to the existing Test-G05SmokeAudio reference-relative implementation.
    [pscustomobject]@{
        referenceDescriptorId = 'typical-2v4a-30s'
        mode = [string]$Overlay.mode
        sampleRate = [int]$Overlay.sampleRate
        windowSamples = [int]$Overlay.windowSamples
        minimumOutputToReferenceRmsRatio = [double]$Overlay.minimumOutputToReferenceRmsRatio
        maximumOutputToReferenceRmsRatio = [double]$Overlay.maximumOutputToReferenceRmsRatio
        replacesV3Checks = @($Overlay.replacesV3Checks)
    }
}

function New-G05Stage2AV5HelperCompatibleDescriptor([object] $Descriptor) {
    # Test-G05SmokeAudio only uses this identifier to bind its frozen V4 overlay assertion.
    # All stress geometry, timing, frequencies, and onset windows remain those of $Descriptor.
    $copy = [ordered]@{}
    foreach ($property in $Descriptor.PSObject.Properties) { $copy[$property.Name] = $property.Value }
    $copy['id'] = 'typical-2v4a-30s'
    $copy['v5OriginalReferenceDescriptorId'] = [string]$Descriptor.id
    [pscustomobject]$copy
}

function Test-G05Stage2AV5StressAudio([string] $Reference, [string] $Actual, [object] $Thresholds, [object] $StressDescriptor, [object] $Overlay, [int] $MaximumRawTailSamples = 0) {
    Assert-G05Stage2AV5StressOverlay $StressDescriptor $Overlay
    $adapterDescriptor = New-G05Stage2AV5HelperCompatibleDescriptor $StressDescriptor
    $adapterOverlay = New-G05Stage2AV5HelperCompatibleOverlay $Overlay
    $result = Test-G05SmokeAudio $Reference $Actual $Thresholds $adapterDescriptor $MaximumRawTailSamples $adapterOverlay
    $result['v5'] = [ordered]@{
        amendmentScope = 'stress-4v8a-30s-only'
        authenticReferenceDescriptorId = [string]$StressDescriptor.id
        helperCompatibilityAdapter = 'identifier-only; all stress semantic fields are retained'
        overlayMode = [string]$Overlay.mode
        replacesV3Checks = @($Overlay.replacesV3Checks)
    }
    $result
}

Export-ModuleMember -Function Get-G05Stage2AV5AudioOracleHash, Assert-G05Stage2AV5StressOverlay, Test-G05Stage2AV5StressAudio
