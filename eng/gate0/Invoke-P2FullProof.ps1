[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RuntimeRoot,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$IncludeLongFormFixture
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-RequiredJson([string]$Path, [string]$Description) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description was not produced at '$Path'."
    }

    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function Add-Verdict([Collections.Generic.List[object]]$Verdicts, [string]$CapabilityId, [string]$Status, [string]$Source, [object]$Details) {
    if (@($Verdicts | Where-Object { $_.capabilityId -eq $CapabilityId }).Count -ne 0) {
        throw "Capability '$CapabilityId' received more than one verdict."
    }

    $Verdicts.Add([ordered]@{
        capabilityId = $CapabilityId
        status = $Status
        source = $Source
        details = $Details
    })
}

if (-not [IO.Path]::IsPathRooted($RuntimeRoot) -or -not (Test-Path -LiteralPath $RuntimeRoot -PathType Container)) {
    throw 'RuntimeRoot must be an existing explicit rooted directory. PATH fallback is prohibited.'
}
if (-not [IO.Path]::IsPathRooted($OutputDirectory)) {
    throw 'OutputDirectory must be an explicit rooted path outside the repository.'
}

$runtime = (Resolve-Path -LiteralPath $RuntimeRoot).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
$contractPath = Join-Path $PSScriptRoot 'semantic-proof-contract.json'
$contract = Read-RequiredJson $contractPath 'Semantic proof contract'
$contractIds = @($contract.capabilities | ForEach-Object id)
if ($contractIds.Count -ne 15 -or @($contractIds | Sort-Object -Unique).Count -ne 15) {
    throw 'The full proof orchestrator requires exactly 15 unique reviewed capability IDs.'
}

$fixtureProofScript = Join-Path $PSScriptRoot 'Invoke-P2SemanticProof.ps1'
if ($IncludeLongFormFixture) {
    & $fixtureProofScript -RuntimeRoot $runtime -OutputDirectory $output -IncludeLongForm
}
else {
    & $fixtureProofScript -RuntimeRoot $runtime -OutputDirectory $output
}

$fixtureRoot = Join-Path $output 'fixtures'
$editOutput = Join-Path $output 'edit-timing'
$visualOutput = Join-Path $output 'visual'
$deliveryOutput = Join-Path $output 'delivery'

& (Join-Path $PSScriptRoot 'Invoke-P2EditTimingProof.ps1') -RuntimeRoot $runtime -FixtureRoot $fixtureRoot -OutputDirectory $editOutput
& (Join-Path $PSScriptRoot 'Invoke-P2VisualProof.ps1') -RuntimeRoot $runtime -FixtureRoot $fixtureRoot -OutputDirectory $visualOutput
& (Join-Path $PSScriptRoot 'Invoke-P2DeliveryProof.ps1') -RuntimeRoot $runtime -FixtureRoot $fixtureRoot -OutputDirectory $deliveryOutput

$fixtureEvidencePath = Join-Path $output 'semantic-proof-evidence.json'
$editEvidencePath = Join-Path $editOutput 'p2-edit-timing-proof.json'
$visualEvidencePath = Join-Path $visualOutput 'visual-proof-evidence.json'
$deliveryEvidencePath = Join-Path $deliveryOutput 'delivery-proof-evidence.json'
$fixtureEvidence = Read-RequiredJson $fixtureEvidencePath 'Fixture proof evidence'
$editEvidence = Read-RequiredJson $editEvidencePath 'Edit/timing proof evidence'
$visualEvidence = Read-RequiredJson $visualEvidencePath 'Visual proof evidence'
$deliveryEvidence = Read-RequiredJson $deliveryEvidencePath 'Delivery proof evidence'

if (@($fixtureEvidence.capabilityVerdicts).Count -ne 0) {
    throw 'Fixture proof evidence must not contain semantic capability verdicts.'
}

$verdicts = [Collections.Generic.List[object]]::new()
$inspection = $fixtureEvidence.inspectionReadiness
if ($null -eq $inspection -or $inspection.readinessId -ne 'Media.Inspect.StructureAndTiming') {
    throw 'Fixture evidence did not contain the dedicated reviewed inspection-readiness record.'
}
$inspectionStatus = if ($inspection.status -eq 'passed' -and $inspection.executedInspectionProof -eq $true) { 'passed' } else { 'failed' }
Add-Verdict $verdicts 'Media.Inspect.StructureAndTiming' $inspectionStatus 'dedicated-inspection-proof' $inspection

foreach ($proof in @($editEvidence.capabilities)) {
    $status = if ($proof.status -eq 'pass') { 'passed' } else { [string]$proof.status }
    Add-Verdict $verdicts ([string]$proof.id) $status 'edit-timing' $proof
}
foreach ($proof in @($visualEvidence.semanticProofs)) {
    Add-Verdict $verdicts ([string]$proof.capabilityId) ([string]$proof.status) 'visual' $proof
}
foreach ($proof in @($deliveryEvidence.semanticProofs)) {
    Add-Verdict $verdicts ([string]$proof.capabilityId) ([string]$proof.status) 'delivery' $proof
}

foreach ($capability in @($contract.capabilities | Where-Object id -in @('Text.Render.UnicodeTitlesAndCaptions', 'Delivery.Validate.IndependentPlayback', 'Project.LongForm.Integrity'))) {
    Add-Verdict $verdicts ([string]$capability.id) ([string]$capability.status) 'contract-pending' $capability
}

$verdictIds = @($verdicts | ForEach-Object capabilityId)
$missing = @($contractIds | Where-Object { $_ -notin $verdictIds })
$unexpected = @($verdictIds | Where-Object { $_ -notin $contractIds })
if ($verdicts.Count -ne 15 -or $missing.Count -ne 0 -or $unexpected.Count -ne 0) {
    throw "Capability aggregation is incomplete. Missing: $($missing -join ', '). Unexpected: $($unexpected -join ', ')."
}

$nonPassing = @($verdicts | Where-Object status -ne 'passed')
$aggregateStatus = if ($nonPassing.Count -eq 0) { 'complete' } else { 'incomplete-with-explicit-blockers' }
$evidenceFiles = @(@($fixtureEvidencePath, $editEvidencePath, $visualEvidencePath, $deliveryEvidencePath) | ForEach-Object {
    [ordered]@{
        path = [IO.Path]::GetRelativePath($output, $_).Replace('\', '/')
        length = (Get-Item -LiteralPath $_).Length
        sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToUpperInvariant()
    }
})

$aggregate = [ordered]@{
    schemaVersion = 1
    contractProfileId = $contract.profileId
    runtimeProfileId = $contract.runtimeProfileId
    runtimeScope = $contract.runtimeScope
    aggregateStatus = $aggregateStatus
    statement = 'G0.3 executable proof aggregation only. Presence is not proof; this is not a shipping-runtime, public-distribution, or legal approval.'
    capabilityVerdicts = $verdicts
    nonPassingCapabilityIds = @($nonPassing | ForEach-Object capabilityId)
    evidenceFiles = $evidenceFiles
}

$temporaryAggregate = Join-Path $output 'p2-full-proof-evidence.partial.json'
$aggregatePath = Join-Path $output 'p2-full-proof-evidence.json'
[IO.File]::WriteAllText($temporaryAggregate, ($aggregate | ConvertTo-Json -Depth 30), [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $temporaryAggregate -Destination $aggregatePath
Write-Output "P2 full proof status: $aggregateStatus"
Write-Output "P2 full proof evidence: $aggregatePath"
