[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$RuntimeRoot,
    [Parameter(Mandatory = $true)][string]$FixtureRoot,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Gate 0 proof infrastructure only. Nothing in this runner participates in
# product import, persistence, rendering, UI, packaging, or distribution.
$moduleRoot = Join-Path $PSScriptRoot 'input-proof'
foreach ($module in @('Common.ps1', 'Authoring.ps1', 'Oracles.ps1', 'Policy.ps1')) {
    . (Join-Path $moduleRoot $module)
}

function New-G04CommonContext {
    param([Parameter(Mandatory)][hashtable]$Context)
    return [pscustomobject]@{ Output=$Context.Output; Work=$Context.Work; Logs=$Context.Logs; Commands=$Context.Commands }
}

function Get-G04FileEvidence {
    param([Parameter(Mandatory)][hashtable]$Context, [Parameter(Mandatory)][string]$Path)
    return Get-G04ArtifactRecord -Context (New-G04CommonContext $Context) -Path $Path
}

function Write-G04JsonAtomic {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][object]$Value)
    $partial = "$Path.partial"
    try {
        [IO.File]::WriteAllText($partial, ($Value | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $partial -Destination $Path -Force
    }
    finally { if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Force } }
}

function Get-G04CommandEvidence {
    param([Parameter(Mandatory)][hashtable]$Context)
    $records = [Collections.Generic.List[object]]::new()
    foreach ($command in @($Context.Commands)) {
        $records.Add([ordered]@{
            name=[string]$command.name; executable=[string]$command.executable; arguments=@($command.arguments)
            components=$command.components; exitCode=[int]$command.exitCode
            stdout=Get-G04FileEvidence $Context ([string]$command.stdoutPath)
            stderr=Get-G04FileEvidence $Context ([string]$command.stderrPath)
        })
    }
    return @($records)
}

function Get-G04GeneratedArtifacts {
    param([Parameter(Mandatory)][hashtable]$Context)
    $records = [Collections.Generic.List[object]]::new()
    foreach ($root in @($Context.Media, $Context.Logs, $Context.Work)) {
        foreach ($file in @(Get-ChildItem -LiteralPath $root -File -Recurse | Sort-Object FullName)) {
            $records.Add((Get-G04FileEvidence $Context $file.FullName))
        }
    }
    return @($records)
}

function Get-G04RuntimeComponentEvidence {
    param([Parameter(Mandatory)][object]$Contract, [Parameter(Mandatory)][object]$RuntimeEvidence)
    if (-not [bool]$RuntimeEvidence.assessment.MatchesProfile -or @($RuntimeEvidence.assessment.Issues).Count -ne 0) {
        throw 'Approved paired runtime identity evidence does not match the exact P2 profile.'
    }
    $components = $RuntimeEvidence.observation.PrimaryTool.Components
    $required = [ordered]@{
        Decoder=@($Contract.componentRoles.nativeDecodersUnderTest + @('ppm', 'rawvideo') | Sort-Object -Unique)
        Encoder=@($Contract.componentRoles.fixtureProductionOnly.component | Sort-Object -Unique)
        Muxer=@($Contract.fixtureRecipes | Where-Object status -eq 'resolved' | ForEach-Object muxer | Where-Object { $_ -and $_ -ne 'preflight-only' } | Sort-Object -Unique)
        Demuxer=@($Contract.guaranteedCases.requiredComponents.demuxer | ForEach-Object { Get-G04ConcreteDemuxer ([string]$_) } | Sort-Object -Unique)
        Filter=@('aformat', 'aloop', 'apad', 'aresample', 'atrim', 'format', 'fps', 'scale', 'setpts')
    }
    $required.Demuxer = @($required.Demuxer + @('concat', 'image2', 'rawvideo', 's16le') | Sort-Object -Unique)
    $checks = [Collections.Generic.List[object]]::new()
    foreach ($kind in $required.Keys) {
        $observed = @($components.$kind)
        foreach ($name in @($required[$kind])) {
            # FFmpeg prints some demuxers as one comma-delimited alias group
            # (for example matroska,webm and mov,mp4,m4a,...), even though -f
            # takes the concrete member selected by the proof command.
            $listingToken = if ($kind -eq 'Demuxer') {
                @($observed | Where-Object { @(([string]$_).Split(',')) -contains [string]$name } | Select-Object -First 1)[0]
            } else {
                @($observed | Where-Object { [string]$_ -eq [string]$name } | Select-Object -First 1)[0]
            }
            $present = -not [string]::IsNullOrWhiteSpace([string]$listingToken)
            $checks.Add([ordered]@{ componentType=$kind.ToLowerInvariant(); name=[string]$name; observedListingToken=$(if($present){[string]$listingToken}else{$null}); present=$present; semanticCapabilityProven=$false })
            if (-not $present) { throw "Exact P2 component presence preflight failed: $kind '$name' is absent." }
        }
    }
    return [ordered]@{
        status='passed'
        statement='Observed component presence is necessary evidence only; it is not an executed ReelForge semantic capability proof.'
        checks=@($checks)
    }
}

function Get-G04FailureStatus {
    param([Parameter(Mandatory)][string]$Message)
    if ($Message -match 'blocked-fixture-provenance|unresolved-producer') { return 'blocked-fixture-provenance' }
    if ($Message -match 'unavailable|missing|not found|no capable device|unsupported') { return 'runtime-unavailable' }
    return 'failed'
}

function Write-G04Evidence {
    param([Parameter(Mandatory)][hashtable]$Context)
    $evidence = [ordered]@{
        schemaVersion=1; proofProfileId='P2.BtbnLgplShared.WindowsX64.20260820'; generatedAtUtc=[DateTimeOffset]::UtcNow
        statement='Third-party LGPLv3-path proof infrastructure only; not the selected shipping runtime or public-distribution approval.'
        contract=$Context.ContractEvidence; fixtureClosure=$Context.FixtureClosureEvidence; runtimeIdentity=$Context.RuntimeIdentityEvidence
        componentPresence=$Context.ComponentPresenceEvidence; run=$Context.RunEvidence
        commands=@(Get-G04CommandEvidence $Context); capabilities=@($Context.Capabilities)
        selectionPolicyEvidence=@($Context.SelectionEvidence); classificationEvidence=@($Context.ClassificationEvidence)
        generatedArtifactClosure=@(Get-G04GeneratedArtifacts $Context)
        limitations=@(
            'No shipping-runtime, bundling, redistribution, public-release, patent, legal, performance, independent-playback, or long-form conclusion is made.',
            'Fixture-producer availability and component presence do not establish an input capability.',
            'Only exact enumerated contract rows can pass; no family-wide support is inferred from extensions or broad container and codec names.',
            'Blocked and failed rows are retained without substitution, fallback, concealed recovery, or contract weakening.'
        )
    }
    Write-G04JsonAtomic -Path $Context.EvidencePath -Value $evidence
}

$output = Assert-G04NewOutsideRepositoryDirectory $OutputDirectory
$context = @{
    Output=$output; Work=Join-Path $output 'work'; Logs=Join-Path $output 'logs'; Media=Join-Path $output 'media'
    Commands=[Collections.Generic.List[object]]::new(); Capabilities=[Collections.Generic.List[object]]::new()
    SelectionEvidence=@(); ClassificationEvidence=@(); ContractEvidence=$null; FixtureClosureEvidence=$null
    RuntimeIdentityEvidence=$null; ComponentPresenceEvidence=[ordered]@{ status='not-run'; checks=@() }; RuntimeIdentity=$null
    RunEvidence=[ordered]@{ status='preflight'; startedAtUtc=[DateTimeOffset]::UtcNow; completedAtUtc=$null; error=$null; counts=$null }
    EvidencePath=Join-Path $output 'g0.4-input-proof-evidence.json'; Ffmpeg=$null; Ffprobe=$null; FixtureRoot=$null
    ArtifactsByCase=@{}; CaseById=@{}
}
foreach ($directory in @($context.Work, $context.Logs, $context.Media)) { New-Item -ItemType Directory -Path $directory | Out-Null }

$runFailure = $null
try {
    $runtime = Assert-G04RootedNonReparseDirectory -Path $RuntimeRoot -Name 'RuntimeRoot'
    $context.FixtureRoot = Assert-G04RootedNonReparseDirectory -Path $FixtureRoot -Name 'FixtureRoot'
    $context.Ffmpeg = Resolve-G04RuntimeTool -Path (Join-Path $runtime 'bin\ffmpeg.exe') -Name 'ffmpeg.exe' -Root $runtime
    $context.Ffprobe = Resolve-G04RuntimeTool -Path (Join-Path $runtime 'bin\ffprobe.exe') -Name 'ffprobe.exe' -Root $runtime

    $contractPath = Join-Path $PSScriptRoot 'g0.4-input-proof-contract.json'
    $contract = Read-G04InputContract -ContractPath $contractPath
    $context.ContractEvidence = [ordered]@{
        path='eng/gate0/g0.4-input-proof-contract.json'; sha256=(Get-FileHash -LiteralPath $contractPath -Algorithm SHA256).Hash.ToUpperInvariant()
        guaranteedCaseCount=@($contract.guaranteedCases).Count; fixtureRecipeCount=@($contract.fixtureRecipes).Count; oracleProfileCount=@($contract.oracleProfiles).Count
    }
    $closure = Test-G04FixtureClosure -FixtureRoot $context.FixtureRoot -InventoryPath (Join-Path $PSScriptRoot 'fixture-source-inventory.json')
    $context.FixtureClosureEvidence = [ordered]@{
        status='passed'; inventory=[ordered]@{ path='eng/gate0/fixture-source-inventory.json'; sha256=$closure.inventorySha256 }
        retainedReport=[ordered]@{ path=$closure.reportPath; sha256=$closure.reportSha256 }; sourceFileCount=@($closure.fileMap.Keys).Count
    }

    $identityPath = Join-Path $output 'runtime-identity.json'
    & (Join-Path $PSScriptRoot 'Validate-P2Runtime.ps1') -RuntimeRoot $runtime -EvidencePath $identityPath
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $identityPath -PathType Leaf)) { throw 'Approved paired runtime identity validation failed; no input proof was run.' }
    $runtimeEvidence = Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json -Depth 100
    $context.RuntimeIdentityEvidence = Get-G04FileEvidence $context $identityPath
    $context.RuntimeIdentity = [ordered]@{
        profileId=[string]$runtimeEvidence.profileId; evidenceSha256=$context.RuntimeIdentityEvidence.sha256
        primaryToolSha256=[string]$runtimeEvidence.observation.PrimaryTool.Sha256; inspectionToolSha256=[string]$runtimeEvidence.observation.InspectionTool.Sha256
        configuration=[string]$runtimeEvidence.observation.PrimaryTool.Configuration
    }
    $context.ComponentPresenceEvidence = Get-G04RuntimeComponentEvidence -Contract $contract -RuntimeEvidence $runtimeEvidence

    $recipes = @{}; foreach ($recipe in @($contract.fixtureRecipes)) { $recipes[[string]$recipe.id] = $recipe }
    $oracles = @{}; foreach ($oracle in @($contract.oracleProfiles)) { $oracles[[string]$oracle.id] = $oracle }
    foreach ($case in @($contract.guaranteedCases)) { $context.CaseById[[string]$case.id] = $case }
    $orderedCases = @($contract.guaranteedCases | Where-Object { [string](Get-G04PropertyValue $_.fixtureProduction 'remux' '') -ne 'stream-copy-only' }) + @($contract.guaranteedCases | Where-Object { [string](Get-G04PropertyValue $_.fixtureProduction 'remux' '') -eq 'stream-copy-only' })

    foreach ($case in $orderedCases) {
        $caseId = [string]$case.id; $recipe = $recipes[[string]$case.fixtureProduction.recipeId]; $started = [DateTimeOffset]::UtcNow
        try {
            $artifact = New-G04CaseArtifact -Case $case -Recipe $recipe -Contract $contract -Context $context -ArtifactsByCase $context.ArtifactsByCase
            if ([string]$artifact.status -ne 'authored') {
                $context.Capabilities.Add([ordered]@{ capabilityId=$caseId; classification='guaranteed-common'; status=[string]$artifact.status; reason=[string]$artifact.reason; executedSemanticProof=$false; fixtureRecipeId=[string]$recipe.id; producerEvidence=$artifact.producerEvidence; oracleEvidence=$null; elapsedMilliseconds=([DateTimeOffset]::UtcNow-$started).TotalMilliseconds })
                continue
            }
            $oracle = $oracles[[string]$recipe.oracleProfileId]
            $oracleEvidence = Test-G04CaseEvidence -Case $case -Recipe $recipe -Oracle $oracle -ArtifactPath $artifact.path -Context $context
            # New-G04CaseArtifact returns an OrderedDictionary and stores that
            # same instance in ArtifactsByCase. Index assignment is required
            # so dependent stream-copy rows observe the semantic pass.
            $artifact['semanticProofPassed'] = $true
            $context.Capabilities.Add([ordered]@{ capabilityId=$caseId; classification='guaranteed-common'; status='passed'; reason='Exact authored fixture passed fresh inspection, explicit native-decoder selection, strict complete decode, and its bound semantic oracle.'; executedSemanticProof=$true; fixtureRecipeId=[string]$recipe.id; artifact=(Get-G04FileEvidence $context $artifact.path); producerEvidence=$artifact.producerEvidence; oracleEvidence=$oracleEvidence; elapsedMilliseconds=([DateTimeOffset]::UtcNow-$started).TotalMilliseconds })
        }
        catch {
            $status = Get-G04FailureStatus -Message $_.Exception.Message
            $context.Capabilities.Add([ordered]@{ capabilityId=$caseId; classification='guaranteed-common'; status=$status; reason=$_.Exception.Message; executedSemanticProof=$false; fixtureRecipeId=[string]$recipe.id; producerEvidence=$null; oracleEvidence=$null; contractWasNotWeakened=$true; elapsedMilliseconds=([DateTimeOffset]::UtcNow-$started).TotalMilliseconds })
        }
    }

    $policyContext = @{ Commands=$context.Commands; FixtureRoot=$context.FixtureRoot; RuntimeCapabilityFingerprint=$context.RuntimeIdentityEvidence.sha256 }
    $context.SelectionEvidence = @(Test-G04SelectionCases -Contract $contract -Context $policyContext)
    $context.ClassificationEvidence = @(Test-G04ClassificationCases -Contract $contract -Context $policyContext)

    $passed=@($context.Capabilities | Where-Object status -eq 'passed').Count; $blocked=@($context.Capabilities | Where-Object status -like 'blocked*').Count
    $runtimeUnavailable=@($context.Capabilities | Where-Object status -eq 'runtime-unavailable').Count; $failed=@($context.Capabilities | Where-Object status -eq 'failed').Count
    $context.RunEvidence.counts = [ordered]@{ total=@($context.Capabilities).Count; passed=$passed; blocked=$blocked; runtimeUnavailable=$runtimeUnavailable; failed=$failed }
    $context.RunEvidence.status = if ($failed -gt 0 -or $runtimeUnavailable -gt 0) { 'completed-with-failures' } elseif ($blocked -gt 0) { 'completed-with-blockers' } else { 'completed' }
    if ($context.RunEvidence.status -ne 'completed') { $runFailure = "G0.4 common-input proof finished as '$($context.RunEvidence.status)'; inspect retained evidence before any owner disposition." }
}
catch {
    $context.RunEvidence.status = if (@($context.Capabilities).Count -gt 0) { 'completed-with-failures' } else { 'preflight-failed' }
    $context.RunEvidence.error = $_.Exception.Message
    $runFailure = $_.Exception.Message
}
finally {
    $context.RunEvidence.completedAtUtc = [DateTimeOffset]::UtcNow
    Write-G04Evidence -Context $context
}

Write-Output "G0.4 input proof evidence: $($context.EvidencePath)"
if ($null -ne $runFailure) { throw $runFailure }
