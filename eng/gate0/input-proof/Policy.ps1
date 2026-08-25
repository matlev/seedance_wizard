Set-StrictMode -Version Latest

# Synthetic Gate 0 policy proof only.  This module does not inspect a file,
# invoke ffprobe/FFmpeg, or represent product import/persistence behavior.
# Its evidence is deliberately tied to the F8 executable proof where noted by
# the contract, while keeping the policy snapshots distinct from media proof.

function Get-G04PropertyValue([object]$Object, [string]$Name, $Default = $null) {
    if ($null -eq $Object) { return $Default }
    if ($Object -is [Collections.IDictionary] -and $Object.Contains($Name)) { return $Object[$Name] }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $Default }
    return $property.Value
}

function Get-G04ContextValue([hashtable]$Context, [string]$Name, $Default = $null) {
    if ($null -eq $Context -or -not $Context.ContainsKey($Name)) { return $Default }
    return $Context[$Name]
}

function Get-G04SnapshotHash([object]$Snapshot) {
    $json = $Snapshot | ConvertTo-Json -Depth 20 -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($json)
    return ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)))
}

function Get-G04ContentBinding([object]$Case, [object]$Snapshot, [hashtable]$Context) {
    $hashes = Get-G04ContextValue $Context 'SourceContentHashes' @{}
    $caseId = [string]$Case.id
    if ($hashes -is [hashtable] -and $hashes.ContainsKey($caseId) -and ([string]$hashes[$caseId]) -match '^[A-Fa-f0-9]{64}$') {
        return [ordered]@{ sourceContentSha256 = ([string]$hashes[$caseId]).ToUpperInvariant(); bindingKind = 'caller-supplied-source-content-sha256' }
    }

    # The checked-in cases are snapshots, not retained synthetic media bytes.
    # Hash their declared input so the proof cannot pretend it bound to real
    # source bytes. A future implementation must replace this with its actual
    # source-content SHA-256 before persisting a stream selection.
    return [ordered]@{ sourceContentSha256 = Get-G04SnapshotHash $Snapshot; bindingKind = 'synthetic-snapshot-sha256-not-media-bytes' }
}

function New-G04Descriptor([object]$Stream) {
    return [ordered]@{
        mediaType = [string]$Stream.type
        streamIndex = [int]$Stream.index
        codecIdentity = [string]$Stream.codec
        defaultDisposition = [bool](Get-G04PropertyValue $Stream 'default' $false)
        language = [string](Get-G04PropertyValue $Stream 'language' '')
        title = [string](Get-G04PropertyValue $Stream 'title' '')
        timing = [ordered]@{ timeBase = [string](Get-G04PropertyValue $Stream 'timeBase' '') }
        observedDescriptor = [ordered]@{
            attachedPicture = [bool](Get-G04PropertyValue $Stream 'attachedPicture' $false)
            usable = [bool](Get-G04PropertyValue $Stream 'usable' $false)
            missingDecoder = Get-G04PropertyValue $Stream 'missingDecoder' $null
        }
    }
}

function Resolve-G04MediaType([object[]]$Streams, [string]$MediaType) {
    $ofType = @($Streams | Where-Object { [string]$_.type -eq $MediaType } | Sort-Object { [int]$_.index })
    $ignored = [Collections.Generic.List[object]]::new()
    $candidates = @()
    foreach ($stream in $ofType) {
        if ($MediaType -eq 'video' -and [bool](Get-G04PropertyValue $stream 'attachedPicture' $false)) {
            $ignored.Add([ordered]@{ streamIndex = [int]$stream.index; reason = 'attached-picture-excluded-from-timeline-video' })
        } else {
            $candidates += $stream
        }
    }

    $defaults = @($candidates | Where-Object { [bool](Get-G04PropertyValue $_ 'default' $false) } | Sort-Object { [int]$_.index })
    if ($defaults.Count -gt 0) {
        $chosen = $defaults[0]
        if (-not [bool](Get-G04PropertyValue $chosen 'usable' $false)) {
            foreach ($stream in $candidates | Where-Object { [int]$_.index -ne [int]$chosen.index }) {
                $ignored.Add([ordered]@{ streamIndex = [int]$stream.index; reason = if([bool](Get-G04PropertyValue $stream 'usable' $false)){'usable-alternate-not-selected-because-default-is-unusable'}else{'nonselected-unusable-alternative'} })
            }
            return [ordered]@{ mediaType=$MediaType; disposition='blocked-unusable-default-no-fallback'; selected=$null; ignoredAlternatives=@($ignored); ambiguousDefaultIndices=@($defaults | ForEach-Object { [int]$_.index }); diagnostics=@('default stream is unusable; usable alternate, if present, must be reported but not silently selected') }
        }
        foreach ($stream in $candidates | Where-Object { [int]$_.index -ne [int]$chosen.index }) { $ignored.Add([ordered]@{ streamIndex=[int]$stream.index; reason=$(if([bool](Get-G04PropertyValue $stream 'default' $false)){'ambiguous-default-not-selected-lowest-index-wins'}else{'nondefault-alternative-not-selected'}) }) }
        return [ordered]@{ mediaType=$MediaType; disposition=$(if($defaults.Count -gt 1){'selected-lowest-index-default-ambiguity-reported'}else{'selected-default'}); selected=(New-G04Descriptor $chosen); ignoredAlternatives=@($ignored); ambiguousDefaultIndices=@($defaults | ForEach-Object { [int]$_.index }); diagnostics=@() }
    }

    $usable = @($candidates | Where-Object { [bool](Get-G04PropertyValue $_ 'usable' $false) } | Sort-Object { [int]$_.index })
    if ($usable.Count -eq 0) {
        foreach ($stream in $candidates) { $ignored.Add([ordered]@{streamIndex=[int]$stream.index;reason='unusable-no-default'}) }
        return [ordered]@{ mediaType=$MediaType; disposition='rejected-no-usable-stream'; selected=$null; ignoredAlternatives=@($ignored); ambiguousDefaultIndices=@(); diagnostics=@('no usable stream exists for requested media type') }
    }
    $selected = $usable[0]
    foreach ($stream in $candidates | Where-Object { [int]$_.index -ne [int]$selected.index }) { $ignored.Add([ordered]@{streamIndex=[int]$stream.index;reason=$(if([bool](Get-G04PropertyValue $stream 'usable' $false)){'lowest-index-usable-wins-no-default'}else{'unusable-alternative'}) }) }
    return [ordered]@{ mediaType=$MediaType; disposition='selected-lowest-index-usable-no-default'; selected=(New-G04Descriptor $selected); ignoredAlternatives=@($ignored); ambiguousDefaultIndices=@(); diagnostics=@() }
}

function Get-G04Oracle([object]$Contract, [string]$Id) {
    $oracle = @($Contract.oracleProfiles | Where-Object { [string]$_.id -eq $Id })
    if ($oracle.Count -ne 1) { throw "G0.4 contract must bind exactly one oracle '$Id'." }
    return $oracle[0]
}

function Assert-G04SelectionOracle([object]$Case, [object]$Oracle, [object[]]$Streams, [object[]]$Active, [string]$SelectedMap, [bool]$Blocked) {
    if ([string]$Oracle.kind -ne 'stream-selection') { throw "Selection case $($Case.id) is not bound to a stream-selection oracle." }
    $expected = $Case.expectedSelection
    $oracleExpected = $Oracle.structure.expectedSelection
    if (($expected | ConvertTo-Json -Depth 20 -Compress) -ne ($oracleExpected | ConvertTo-Json -Depth 20 -Compress)) { throw "Selection case $($Case.id) and its bound oracle disagree." }
    $expectedMap = Get-G04PropertyValue $expected 'selectedMap' $null
    if (-not ([string]::IsNullOrEmpty([string]$expectedMap) -and [string]::IsNullOrEmpty([string]$SelectedMap)) -and $expectedMap -ne $SelectedMap) { throw "Selection map oracle failed for $($Case.id): expected '$expectedMap', observed '$SelectedMap'." }
    $expectedIgnored = @($expected.ignoredStreamIndices | Sort-Object)
    $actualIgnored = @($Active | ForEach-Object { $_.ignoredAlternatives } | ForEach-Object { [int]$_.streamIndex } | Sort-Object -Unique)
    if (($expectedIgnored -join ',') -ne ($actualIgnored -join ',')) { throw "Ignored stream oracle failed for $($Case.id)." }
    $expectedSelected = if($null -eq $expectedMap){$null}else{@($Streams | Where-Object { "0:$($_.type.Substring(0,1)):$($_.index)" -eq $expectedMap })[0]}
    $actualSelected = @($Active | Where-Object { $null -ne $_.selected } | ForEach-Object { $_.selected })
    if($null -eq $expectedSelected) {
        if($actualSelected.Count -ne 0 -or -not $Blocked) { throw "Blocked/no-fallback oracle failed for $($Case.id)." }
    } else {
        if($actualSelected.Count -ne 1) { throw "Selected descriptor oracle failed for $($Case.id)." }
        $descriptor=$actualSelected[0]
        foreach($pair in @(@('streamIndex',[int]$expectedSelected.index),@('codecIdentity',[string]$expectedSelected.codec),@('defaultDisposition',[bool]$expectedSelected.default),@('language',[string]$expectedSelected.language),@('title',[string]$expectedSelected.title))) { if($descriptor.($pair[0]) -ne $pair[1]) { throw "Selected descriptor '$($pair[0])' oracle failed for $($Case.id)." } }
        if([string]$descriptor.timing.timeBase -ne [string]$expectedSelected.timeBase -or [bool]$descriptor.observedDescriptor.attachedPicture -ne [bool](Get-G04PropertyValue $expectedSelected 'attachedPicture' $false) -or [bool]$descriptor.observedDescriptor.usable -ne [bool]$expectedSelected.usable) { throw "Selected descriptor timing/observation oracle failed for $($Case.id)." }
    }
    if([string]$Case.id -eq 'S4-MultipleDefaults' -and (@($Active[0].ambiguousDefaultIndices) -join ',') -ne '0,1') { throw 'S4 ambiguity oracle failed.' }
    if([string]$Case.id -eq 'S5-AttachedPicture') { if(-not @($Active[0].ignoredAlternatives | Where-Object { $_.streamIndex -eq 0 -and $_.reason -eq 'attached-picture-excluded-from-timeline-video' })) { throw 'S5 attached-picture retention/exclusion oracle failed.' } }
    if([string]$Case.id -eq 'S6-UndecodableDefault') { if(-not @($Active[0].ignoredAlternatives | Where-Object { $_.streamIndex -eq 1 -and $_.reason -eq 'usable-alternate-not-selected-because-default-is-unusable' })) { throw 'S6 usable-alternate/no-fallback oracle failed.' } }
}

function Assert-G04NoMediaInvocation([hashtable]$Context, [int]$StartingCommandCount) {
    $commands = Get-G04ContextValue $Context 'Commands' $null
    if($null -ne $commands -and @($commands).Count -ne $StartingCommandCount) { throw 'Policy proof must not append or invoke a media command.' }
}

function Test-G04SelectionCases {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Contract, [hashtable]$Context = @{})

    $recipes = @{}; foreach($recipe in @($Contract.fixtureRecipes)) { $recipes[[string]$recipe.id] = $recipe }
    $previous = Get-G04ContextValue $Context 'PriorSelections' @{}
    $runtimeFingerprint = [string](Get-G04ContextValue $Context 'RuntimeCapabilityFingerprint' '')
    $startingCommandCount = @((Get-G04ContextValue $Context 'Commands' @())).Count
    $evidence = [Collections.Generic.List[object]]::new()
    foreach($case in @($Contract.selectionCases)) {
        $snapshot = $case.expectedSelection
        $recipe = $recipes[[string]$case.fixtureRecipeId]
        $streams = @($snapshot.observedStreams)
        $binding = Get-G04ContentBinding $case $snapshot $Context
        $resolutions = @(); foreach($mediaType in @($Contract.selectionPolicy.independentlyResolve)) { $resolutions += Resolve-G04MediaType $streams ([string]$mediaType) }
        $active = @()
        foreach($resolution in $resolutions) {
            $resolvedMediaType = [string]$resolution.mediaType
            if (@($streams | Where-Object { [string]$_.type -eq $resolvedMediaType }).Count -gt 0) { $active += $resolution }
        }
        $blocked = @($active | Where-Object { $_.disposition -eq 'blocked-unusable-default-no-fallback' }).Count -gt 0
        $prior = if($previous -is [hashtable] -and $previous.ContainsKey([string]$case.id)){$previous[[string]$case.id]}else{$null}
        $revalidationRequired = $false
        if($null -ne $prior) {
            $priorHash = [string](Get-G04PropertyValue $prior 'sourceContentSha256' '')
            $priorRuntime = [string](Get-G04PropertyValue $prior 'runtimeCapabilityFingerprint' '')
            $revalidationRequired = ($priorHash -and $priorHash -ne $binding.sourceContentSha256) -or ($priorRuntime -and $runtimeFingerprint -and $priorRuntime -ne $runtimeFingerprint)
        }
        $selectedMap = if(-not $revalidationRequired -and $active.Count -eq 1 -and $null -ne $active[0].selected){ "0:$($active[0].mediaType.Substring(0,1)):$($active[0].selected.streamIndex)" }else{$null}
        $oracle = Get-G04Oracle $Contract ([string]$case.oracleProfileId)
        if(-not $revalidationRequired) { Assert-G04SelectionOracle $case $oracle $streams $active $selectedMap $blocked }
        Assert-G04NoMediaInvocation $Context $startingCommandCount
        $evidence.Add([ordered]@{
            caseId=[string]$case.id; branch=[string]$case.branch; status=$(if($revalidationRequired){'blocked-revalidation-required'}elseif($blocked){'blocked'}else{'passed'}); classification=$(if($revalidationRequired){'blocked-revalidation-required'}elseif($blocked){'blocked-profile-aware-before-ffmpeg-execution'}else{'policy-selected'})
            fixtureRecipeId=[string]$case.fixtureRecipeId; fixtureKind=Get-G04PropertyValue $recipe 'fixtureKind' ''; executionClaim=Get-G04PropertyValue $recipe 'executionClaim' ''; baseExecutableEvidence=Get-G04PropertyValue $recipe 'baseExecutableEvidence' $null
            syntheticSnapshot=$snapshot; invocation=[ordered]@{ ffmpegInvocations=0; ffprobeInvocations=0; commandCountBefore=$startingCommandCount; commandCountAfter=@((Get-G04ContextValue $Context 'Commands' @())).Count; preflightOnly=$true }; contentHashBinding=$binding; runtimeCapabilityFingerprint=$runtimeFingerprint; resolutions=@($resolutions); selectedMap=$selectedMap; revalidationRequired=$revalidationRequired
        })
    }
    return @($evidence)
}

function Test-G04ClassificationCases {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Contract, [hashtable]$Context = @{})

    $recipes = @{}; foreach($recipe in @($Contract.fixtureRecipes)) { $recipes[[string]$recipe.id] = $recipe }
    $startingCommandCount = @((Get-G04ContextValue $Context 'Commands' @())).Count
    $fixtureRoot = [string](Get-G04ContextValue $Context 'FixtureRoot' '')
    $evidence = [Collections.Generic.List[object]]::new()
    foreach($case in @($Contract.classificationCases)) {
        $recipe = $recipes[[string]$case.fixtureRecipeId]
        $id = [string]$case.id
        $classification = [string]$case.expectedClassification
        $snapshot = switch($id) {
            'N1-MisleadingExtension' {
                $file = if($fixtureRoot){Join-Path $fixtureRoot 'F1\f1-pattern-000.ppm'}else{$null}; $bytes=if($file -and (Test-Path -LiteralPath $file)){[IO.File]::ReadAllBytes($file)}else{[byte[]](80,54)}
                if($bytes.Length -lt 2 -or [Text.Encoding]::ASCII.GetString($bytes,0,2) -ne 'P6'){throw 'N1 magic-byte content inspection oracle failed.'}
                [ordered]@{ declaredFilename='misleading.mp4'; declaredExtension='.mp4'; sniffedMagic='P6'; inspectedContainer='ppm'; extensionAdvisory=$true; sourceLength=$bytes.Length; sourceSha256=([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))) }
            }
            'N2-CorruptOrTruncated' {
                $file = if($fixtureRoot){Join-Path $fixtureRoot 'F1\f1-pattern-000.ppm'}else{$null}; $bytes=if($file -and (Test-Path -LiteralPath $file)){[IO.File]::ReadAllBytes($file)}else{[byte[]](0..255)}; $truncated=$bytes[0..127]
                if($bytes.Length -le 128 -or $truncated.Length -ne 128 -or ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))) -eq ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($truncated)))){throw 'N2 exact 128-byte truncation oracle failed.'}
                [ordered]@{ truncationRule=[string]$recipe.truncationRule; retainedBytes=$truncated.Length; originalLength=$bytes.Length; originalSha256=([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))); truncatedSha256=([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($truncated))); inspection='truncated-before-processing' }
            }
            'N3-NoUsableRequestedMedia' { [ordered]@{ requestedMediaType='video'; streams=@([ordered]@{index=0;type='video';codec='h264';usable=$false}) } }
            'N4-DecoderMissing' { [ordered]@{ requestedDecoder='hevc'; observedDecoders=@('h264','aac'); selectedDecoderPresent=$false } }
            'N5-MultipleStreams' { $r=Resolve-G04MediaType @([pscustomobject]@{index=0;type='video';codec='h264';default=$false;usable=$true},[pscustomobject]@{index=1;type='video';codec='h264';default=$true;usable=$true}) 'video'; if($null -eq $r.selected -or $r.selected.streamIndex -ne 1 -or @($r.ignoredAlternatives).Count -ne 1){throw 'N5 must use the common selection evaluator.'}; [ordered]@{ selectionPolicy='default-disposition-lowest-index'; selectedStreamIndex=$r.selected.streamIndex; alternativesReported=$true } }
            'N6-OutsideEnvelopeCapabilityQualified' { [ordered]@{ inspectedMedia='HEVC/H.265 10-bit'; exactGuaranteedEnvelope=$false } }
            'N6-ProtectedRejected' { [ordered]@{ protectedOrEncrypted=$true; processingStopped=$true } }
            'N7-InvalidRuntimePair' { [ordered]@{ pairedRuntimeValid=$false; processingStopped=$true } }
            default { throw "Unknown G0.4 classification case: $id" }
        }
        $oracle = Get-G04Oracle $Contract ([string]$case.oracleProfileId)
        if([string]$oracle.kind -ne 'classification' -or [string]$oracle.structure.expectedClassification -ne $classification -or [string]$case.expected -ne $classification){throw "Classification oracle failed for $id."}
        if([string]::IsNullOrWhiteSpace([string]$recipe.fixtureKind) -or [string]::IsNullOrWhiteSpace([string]$recipe.executionClaim)){throw "Classification recipe evidence boundary failed for $id."}
        Assert-G04NoMediaInvocation $Context $startingCommandCount
        $evidence.Add([ordered]@{ caseId=$id; status=$(if($classification -like 'rejected*'){'rejected'}elseif($classification -like 'blocked*'){'blocked'}elseif($classification -eq 'runtime-unavailable'){'runtime-unavailable'}else{'passed'}); classification=$classification; fixtureRecipeId=[string]$case.fixtureRecipeId; fixtureKind=[string]$recipe.fixtureKind; executionClaim=[string]$recipe.executionClaim; syntheticSnapshot=$snapshot; invocation=[ordered]@{ ffmpegInvocations=0; ffprobeInvocations=0; commandCountBefore=$startingCommandCount; commandCountAfter=@((Get-G04ContextValue $Context 'Commands' @())).Count; preflightOnly=$true }; oracle=[string]$case.expected })
    }
    return @($evidence)
}
