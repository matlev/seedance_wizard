[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $FinalFreezePath,
    [Parameter(Mandatory)] [string] $ControlReportPath,
    [Parameter(Mandatory)] [string] $FreezeCandidatePath,
    [Parameter(Mandatory)] [string] $V4AuthoritativeControlReportPath,
    [Parameter(Mandatory)] [string] $AuthorizationPath,
    [Parameter(Mandatory)] [string] $StressReferencePcmPath,
    [Parameter(Mandatory)] [string] $WebmPcmPath,
    [Parameter(Mandatory)] [string] $WebmOriginalSummaryPath,
    [Parameter(Mandatory)] [string] $Mp4PcmPath,
    [Parameter(Mandatory)] [string] $Mp4OriginalSummaryPath,
    [Parameter(Mandatory)] [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-Sha256([string] $Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant() }
function Get-ExistingAbsoluteFile([string] $Path, [string] $Label) {
    if (-not [IO.Path]::IsPathRooted($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "$Label must be an existing absolute file." }
    [IO.Path]::GetFullPath($Path)
}
function New-OutputDirectory([string] $Path) {
    if (-not [IO.Path]::IsPathRooted($Path) -or (Test-Path -LiteralPath $Path)) { throw 'OutputDirectory must be an absolute new directory.' }
    [IO.Directory]::CreateDirectory($Path).FullName
}
function Assert-PortableJsonValue([object] $Value, [string] $Context = 'root') {
    if ($null -eq $Value) { return }
    if ($Value -is [string]) {
        if ([IO.Path]::IsPathRooted($Value) -or $Value -match '(?i)(?:https?://|[a-z]:\\|\\\\|credential|secret|accesskey|signed)') { throw "Non-portable or sensitive value at $Context." }
        return
    }
    if ($Value -is [ValueType]) { return }
    if ($Value -is [Collections.IEnumerable] -and -not ($Value -is [Collections.IDictionary])) {
        $index = 0; foreach ($item in $Value) { Assert-PortableJsonValue $item "$Context[$index]"; $index++ }; return
    }
    foreach ($property in $Value.PSObject.Properties) { Assert-PortableJsonValue $property.Value "$Context.$($property.Name)" }
}
function Assert-AuthorizationExactKeys([object] $Value, [string[]] $Expected, [string] $Label) {
    $actual = @($Value.PSObject.Properties.Name | Sort-Object); $wanted = @($Expected | Sort-Object)
    if ($actual.Count -ne $wanted.Count -or @($actual | Where-Object { $_ -notin $wanted }).Count -or @($wanted | Where-Object { $_ -notin $actual }).Count) { throw "$Label schema is not closed." }
}
function Assert-AuthorizationHash([object] $Value, [string] $Label) { if ($Value -isnot [string] -or $Value -notmatch '^[A-F0-9]{64}$') { throw "$Label must be an uppercase SHA-256." } }
function Assert-AuthorizationBoolean([object] $Value, [string] $Label) { if ($Value -isnot [bool]) { throw "$Label must be Boolean." } }
function Assert-ExactAuthorization([object] $Authorization, [string] $FreezeHash) {
    $expectedKeys = @('schemaVersion','authorizationId','status','createdUtc','finalFreeze','evaluator','v5','inputs','executionBoundary')
    Assert-AuthorizationExactKeys $Authorization $expectedKeys 'Reevaluation authorization'
    if ($Authorization.schemaVersion -ne 1 -or $Authorization.authorizationId -ne 'Gate0.G05.Stage2A.V5RetainedOutputReevaluation.20260827' -or $Authorization.status -ne 'owner-approved-after-final-v5-freeze' -or $Authorization.createdUtc -isnot [string] -or $Authorization.createdUtc -notmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?Z$') { throw 'Reevaluation authorization identity is invalid.' }
    Assert-AuthorizationExactKeys $Authorization.finalFreeze @('filename','sha256','freezeId') 'Reevaluation authorization finalFreeze'; Assert-AuthorizationHash $Authorization.finalFreeze.sha256 'Reevaluation authorization finalFreeze hash'
    if ($Authorization.finalFreeze.filename -isnot [string] -or $Authorization.finalFreeze.sha256 -ne $FreezeHash -or $Authorization.finalFreeze.freezeId -ne 'Gate0.G05.LossyAudioOracle.V5.ReferenceRelativeStress.Frozen.20260827') { throw 'Reevaluation authorization does not bind this exact final freeze.' }
    $required = @{
        'eng/gate0/Invoke-G05Stage2AV5RetainedOutputReevaluation.ps1' = Get-Sha256 $PSCommandPath
        'eng/gate0/G05Stage2AV5AudioOracle.psm1' = Get-Sha256 (Join-Path $PSScriptRoot 'G05Stage2AV5AudioOracle.psm1')
        'eng/gate0/g0.5-lossy-audio-oracle-amendment-v5.json' = Get-Sha256 (Join-Path $PSScriptRoot 'g0.5-lossy-audio-oracle-amendment-v5.json')
    }
    Assert-AuthorizationExactKeys $Authorization.evaluator @('path','sha256') 'Reevaluation authorization evaluator'; Assert-AuthorizationHash $Authorization.evaluator.sha256 'Reevaluation authorization evaluator hash'
    if ($Authorization.evaluator.path -ne 'eng/gate0/Invoke-G05Stage2AV5RetainedOutputReevaluation.ps1' -or $Authorization.evaluator.sha256 -ne $required['eng/gate0/Invoke-G05Stage2AV5RetainedOutputReevaluation.ps1']) { throw 'Reevaluation authorization evaluator binding is invalid.' }
    Assert-AuthorizationExactKeys $Authorization.v5 @('amendmentPath','amendmentSha256','modulePath','moduleSha256','referenceDescriptorId') 'Reevaluation authorization V5'; Assert-AuthorizationHash $Authorization.v5.amendmentSha256 'Reevaluation authorization amendment hash'; Assert-AuthorizationHash $Authorization.v5.moduleSha256 'Reevaluation authorization module hash'
    if ($Authorization.v5.modulePath -ne 'eng/gate0/G05Stage2AV5AudioOracle.psm1' -or $Authorization.v5.moduleSha256 -ne $required['eng/gate0/G05Stage2AV5AudioOracle.psm1'] -or $Authorization.v5.amendmentPath -ne 'eng/gate0/g0.5-lossy-audio-oracle-amendment-v5.json' -or $Authorization.v5.amendmentSha256 -ne $required['eng/gate0/g0.5-lossy-audio-oracle-amendment-v5.json'] -or $Authorization.v5.referenceDescriptorId -ne 'stress-4v8a-30s') { throw 'Reevaluation authorization V5 binding is invalid.' }
    Assert-AuthorizationExactKeys $Authorization.inputs @('stressReferencePcmSha256','webm','mp4') 'Reevaluation authorization inputs'; Assert-AuthorizationHash $Authorization.inputs.stressReferencePcmSha256 'Reevaluation authorization stress hash'; Assert-AuthorizationExactKeys $Authorization.inputs.webm @('routeId','pcmSha256','originalV3SummarySha256') 'Reevaluation authorization WebM input'; Assert-AuthorizationExactKeys $Authorization.inputs.mp4 @('routeId','pcmSha256','originalV3SummarySha256') 'Reevaluation authorization MP4 input'; foreach($hash in @($Authorization.inputs.webm.pcmSha256,$Authorization.inputs.webm.originalV3SummarySha256,$Authorization.inputs.mp4.pcmSha256,$Authorization.inputs.mp4.originalV3SummarySha256)){Assert-AuthorizationHash $hash 'Reevaluation authorization route hash'}
    if ($Authorization.inputs.stressReferencePcmSha256 -ne '299846E21A0AF6F1416CCA7BF1BF8ACAC4A5EDDA78EFF9BEB392CC7B992B8CF5' -or $Authorization.inputs.webm.routeId -ne 'webm-vp9-opus' -or $Authorization.inputs.mp4.routeId -ne 'mp4-h264-aac' -or $Authorization.inputs.webm.pcmSha256 -ne 'B59110445A1A45F31E5DDAF117184F4F40F9AD67D036DE697BD98CECE512D7B6' -or $Authorization.inputs.webm.originalV3SummarySha256 -ne '1CF498BE47FFA394B9A5F6B0BFB2A4A9DEAE615A03F8F999B76B9375CFB96E9A' -or $Authorization.inputs.mp4.pcmSha256 -ne 'C1177ED32A9E17CB118FFBAE16504A1BFCD08815041B6A18E0C59D9E7E6D36B9' -or $Authorization.inputs.mp4.originalV3SummarySha256 -ne '4E4BACBC4BA0DB258215F93D41F411DE872DD69691B60949C5088824572DED97') { throw 'Reevaluation authorization input binding is invalid.' }
    Assert-AuthorizationExactKeys $Authorization.executionBoundary @('reencodeAuthorized','ffmpegAuthorized','ffprobeAuthorized','mediaProcessAuthorized','retainedPcmReadAuthorized') 'Reevaluation authorization execution boundary'; foreach($property in $Authorization.executionBoundary.PSObject.Properties){Assert-AuthorizationBoolean $property.Value "Reevaluation authorization execution boundary $($property.Name)"}
    if ($Authorization.executionBoundary.reencodeAuthorized -or $Authorization.executionBoundary.ffmpegAuthorized -or $Authorization.executionBoundary.ffprobeAuthorized -or $Authorization.executionBoundary.mediaProcessAuthorized -or -not $Authorization.executionBoundary.retainedPcmReadAuthorized) { throw 'Reevaluation authorization execution boundary is widened.' }
}

Import-Module (Join-Path $PSScriptRoot 'G05Stage2AV5FreezeValidation.psm1') -Force
$closure = Assert-G05Stage2AV5FinalFreezeClosure $FinalFreezePath $ControlReportPath $FreezeCandidatePath $V4AuthoritativeControlReportPath $PSScriptRoot
$FinalFreezePath = $closure.FinalFreezePath
$freeze = $closure.Freeze
$AuthorizationPath = Get-ExistingAbsoluteFile $AuthorizationPath 'AuthorizationPath'
$authorization = Get-Content -LiteralPath $AuthorizationPath -Raw | ConvertFrom-Json -Depth 64 -DateKind String
Assert-PortableJsonValue $authorization
Assert-ExactAuthorization $authorization (Get-Sha256 $FinalFreezePath)
$StressReferencePcmPath = Get-ExistingAbsoluteFile $StressReferencePcmPath 'StressReferencePcmPath'
$WebmPcmPath = Get-ExistingAbsoluteFile $WebmPcmPath 'WebmPcmPath'
$WebmOriginalSummaryPath = Get-ExistingAbsoluteFile $WebmOriginalSummaryPath 'WebmOriginalSummaryPath'
$Mp4PcmPath = Get-ExistingAbsoluteFile $Mp4PcmPath 'Mp4PcmPath'
$Mp4OriginalSummaryPath = Get-ExistingAbsoluteFile $Mp4OriginalSummaryPath 'Mp4OriginalSummaryPath'
if ((Get-Sha256 $StressReferencePcmPath) -ne $authorization.inputs.stressReferencePcmSha256 -or (Get-Sha256 $WebmPcmPath) -ne $authorization.inputs.webm.pcmSha256 -or (Get-Sha256 $WebmOriginalSummaryPath) -ne $authorization.inputs.webm.originalV3SummarySha256 -or (Get-Sha256 $Mp4PcmPath) -ne $authorization.inputs.mp4.pcmSha256 -or (Get-Sha256 $Mp4OriginalSummaryPath) -ne $authorization.inputs.mp4.originalV3SummarySha256) { throw 'Reevaluation input bytes do not match the exact authorization.' }
$v3 = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'g0.5-lossy-audio-oracle-contract.json') -Raw | ConvertFrom-Json -Depth 64
$v5 = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'g0.5-lossy-audio-oracle-amendment-v5.json') -Raw | ConvertFrom-Json -Depth 64
$descriptor = @($v3.referenceDescriptors | Where-Object { $_.id -eq 'stress-4v8a-30s' }); $overlay = @($v5.descriptorOverlays | Where-Object { $_.referenceDescriptorId -eq 'stress-4v8a-30s' })
if ($descriptor.Count -ne 1 -or $overlay.Count -ne 1) { throw 'Exact V5 stress descriptor/overlay is unavailable.' }
Import-Module (Join-Path $PSScriptRoot 'G05Stage2SmokeHelpers.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'G05Stage2AV5AudioOracle.psm1') -Force
$routes = [ordered]@{}
foreach ($route in @([ordered]@{ key='webm'; pcm=$WebmPcmPath; summary=$WebmOriginalSummaryPath; metadata=$authorization.inputs.webm }, [ordered]@{ key='mp4'; pcm=$Mp4PcmPath; summary=$Mp4OriginalSummaryPath; metadata=$authorization.inputs.mp4 })) {
    $original = Get-Content -LiteralPath $route.summary -Raw | ConvertFrom-Json -Depth 128 -DateKind String
    Assert-PortableJsonValue $original "originalV3Summary.$($route.key)"
    $evaluation = Test-G05Stage2AV5StressAudio $StressReferencePcmPath $route.pcm $v3.qualityThresholds $descriptor[0] $overlay[0] 0
    $routes[$route.key] = [ordered]@{ routeId=$route.metadata.routeId; pcm=[ordered]@{ filename=[IO.Path]::GetFileName($route.pcm); sha256=Get-Sha256 $route.pcm; size=(Get-Item -LiteralPath $route.pcm).Length }; originalV3Summary=$original; originalV3SummarySha256=Get-Sha256 $route.summary; v5Evaluation=$evaluation }
}
$passed = @($routes.Values | ForEach-Object { [bool]$_.v5Evaluation.passed }) -notcontains $false
$output = New-OutputDirectory $OutputDirectory
$proofId = 'g05-stage2a-v5-retained-output-reevaluation-' + [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
$report = [ordered]@{ schemaVersion=1; proofId=$proofId; status=if($passed){'passed-no-media-continuation-prerequisite'}else{'stop-before-media-v5-route-failure'}; finalFreeze=[ordered]@{ filename=[IO.Path]::GetFileName($FinalFreezePath); sha256=Get-Sha256 $FinalFreezePath }; authorization=[ordered]@{ filename=[IO.Path]::GetFileName($AuthorizationPath); sha256=Get-Sha256 $AuthorizationPath; authorizationId=$authorization.authorizationId }; reference=[ordered]@{ filename=[IO.Path]::GetFileName($StressReferencePcmPath); sha256=Get-Sha256 $StressReferencePcmPath; size=(Get-Item -LiteralPath $StressReferencePcmPath).Length; descriptorId='stress-4v8a-30s' }; routes=$routes; executionBoundary=[ordered]@{ retainedPcmRead=$true; reencodePerformed=$false; ffmpegInvoked=$false; ffprobeInvoked=$false; mediaProcessesStarted=$false; originalV3RecordsModified=$false }; nextAction=if($passed){'continuation-remains-subject-to-separate-authorizations-and-preflight'}else{'stop-before-media-and-return-exact-route-evidence-for-owner-disposition'} }
$reportPath = Join-Path $output 'g0.5-stage2a-v5-retained-output-reevaluation-result.json'
[IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 128), [Text.UTF8Encoding]::new($false))
$report | ConvertTo-Json -Depth 128
