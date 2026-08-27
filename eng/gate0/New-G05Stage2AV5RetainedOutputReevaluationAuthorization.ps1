[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $FinalFreezePath,
    [Parameter(Mandatory)] [string] $ControlReportPath,
    [Parameter(Mandatory)] [string] $FreezeCandidatePath,
    [Parameter(Mandatory)] [string] $V4AuthoritativeControlReportPath,
    [Parameter(Mandatory)] [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'G05Stage2AV5FreezeValidation.psm1') -Force
$closure = Assert-G05Stage2AV5FinalFreezeClosure $FinalFreezePath $ControlReportPath $FreezeCandidatePath $V4AuthoritativeControlReportPath $PSScriptRoot
$FinalFreezePath = $closure.FinalFreezePath
if (-not [IO.Path]::IsPathRooted($OutputPath) -or (Test-Path -LiteralPath $OutputPath)) { throw 'OutputPath must be an absolute new file.' }
$freeze = $closure.Freeze

$required = @{
    'eng/gate0/g0.5-lossy-audio-oracle-amendment-v5.json' = (Get-G05V5FreezeSha256 (Join-Path $PSScriptRoot 'g0.5-lossy-audio-oracle-amendment-v5.json'))
    'eng/gate0/G05Stage2AV5AudioOracle.psm1' = (Get-G05V5FreezeSha256 (Join-Path $PSScriptRoot 'G05Stage2AV5AudioOracle.psm1'))
    'eng/gate0/Invoke-G05Stage2AV5RetainedOutputReevaluation.ps1' = (Get-G05V5FreezeSha256 (Join-Path $PSScriptRoot 'Invoke-G05Stage2AV5RetainedOutputReevaluation.ps1'))
}
$actual = @{}
foreach ($entry in @($freeze.frozenInputs)) {
    if ($actual.ContainsKey([string]$entry.path)) { throw 'Final freeze contains a duplicate frozen input path.' }
    $actual[[string]$entry.path] = [string]$entry.sha256
}
foreach ($path in $required.Keys) {
    if (-not $actual.ContainsKey($path) -or $actual[$path] -ne $required[$path]) { throw "Final freeze does not bind the exact $path byte hash." }
}

$authorization = [ordered]@{
    schemaVersion = 1
    authorizationId = 'Gate0.G05.Stage2A.V5RetainedOutputReevaluation.20260827'
    status = 'owner-approved-after-final-v5-freeze'
    createdUtc = [DateTime]::UtcNow.ToString('O', [Globalization.CultureInfo]::InvariantCulture)
    finalFreeze = [ordered]@{ filename = [IO.Path]::GetFileName($FinalFreezePath); sha256 = Get-G05V5FreezeSha256 $FinalFreezePath; freezeId = $freeze.freezeId }
    evaluator = [ordered]@{ path = 'eng/gate0/Invoke-G05Stage2AV5RetainedOutputReevaluation.ps1'; sha256 = $required['eng/gate0/Invoke-G05Stage2AV5RetainedOutputReevaluation.ps1'] }
    v5 = [ordered]@{
        amendmentPath = 'eng/gate0/g0.5-lossy-audio-oracle-amendment-v5.json'
        amendmentSha256 = $required['eng/gate0/g0.5-lossy-audio-oracle-amendment-v5.json']
        modulePath = 'eng/gate0/G05Stage2AV5AudioOracle.psm1'
        moduleSha256 = $required['eng/gate0/G05Stage2AV5AudioOracle.psm1']
        referenceDescriptorId = 'stress-4v8a-30s'
    }
    inputs = [ordered]@{
        stressReferencePcmSha256 = '299846E21A0AF6F1416CCA7BF1BF8ACAC4A5EDDA78EFF9BEB392CC7B992B8CF5'
        webm = [ordered]@{ routeId = 'webm-vp9-opus'; pcmSha256 = 'B59110445A1A45F31E5DDAF117184F4F40F9AD67D036DE697BD98CECE512D7B6'; originalV3SummarySha256 = '1CF498BE47FFA394B9A5F6B0BFB2A4A9DEAE615A03F8F999B76B9375CFB96E9A' }
        mp4 = [ordered]@{ routeId = 'mp4-h264-aac'; pcmSha256 = 'C1177ED32A9E17CB118FFBAE16504A1BFCD08815041B6A18E0C59D9E7E6D36B9'; originalV3SummarySha256 = '4E4BACBC4BA0DB258215F93D41F411DE872DD69691B60949C5088824572DED97' }
    }
    executionBoundary = [ordered]@{ reencodeAuthorized = $false; ffmpegAuthorized = $false; ffprobeAuthorized = $false; mediaProcessAuthorized = $false; retainedPcmReadAuthorized = $true }
}
[IO.File]::WriteAllText($OutputPath, ($authorization | ConvertTo-Json -Depth 64), [Text.UTF8Encoding]::new($false))
$authorization | ConvertTo-Json -Depth 64
