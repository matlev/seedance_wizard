[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $RuntimeRoot,

    [string] $ManifestPath = (Join-Path $PSScriptRoot 'manifests\p2-btbn-lgplv3-shared-windows-x64-20260820.json'),

    [string] $EvidencePath = (Join-Path ([System.IO.Path]::GetTempPath()) 'ReelForge-Gate0\p2-runtime-evidence.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not [System.IO.Path]::IsPathRooted($RuntimeRoot) -or -not (Test-Path -LiteralPath $RuntimeRoot -PathType Container)) {
    throw 'RuntimeRoot must be an existing explicit rooted directory. PATH fallback is prohibited.'
}

$runtimeRootFull = (Resolve-Path -LiteralPath $RuntimeRoot).Path
$manifestFull = (Resolve-Path -LiteralPath $ManifestPath).Path
$manifest = Get-Content -LiteralPath $manifestFull -Raw | ConvertFrom-Json
foreach ($relativeToolPath in @($manifest.primaryTool.relativePath, $manifest.inspectionTool.relativePath)) {
    $toolPath = Join-Path $runtimeRootFull ([string] $relativeToolPath)
    if (-not (Test-Path -LiteralPath $toolPath -PathType Leaf)) {
        throw "Approved runtime tool is missing: $relativeToolPath"
    }
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$testProject = Join-Path $repositoryRoot 'tests\ReelForge.Infrastructure.Tests\ReelForge.Infrastructure.Tests.csproj'
$previousRuntimeRoot = $env:REELFORGE_GATE0_P2_RUNTIME_ROOT
$previousManifest = $env:REELFORGE_GATE0_P2_MANIFEST
$previousEvidence = $env:REELFORGE_GATE0_P2_EVIDENCE_PATH

try {
    $env:REELFORGE_GATE0_P2_RUNTIME_ROOT = $runtimeRootFull
    $env:REELFORGE_GATE0_P2_MANIFEST = $manifestFull
    $env:REELFORGE_GATE0_P2_EVIDENCE_PATH = [System.IO.Path]::GetFullPath($EvidencePath)

    & dotnet test $testProject `
        --configuration Release `
        --no-restore `
        --filter 'FullyQualifiedName~Gate0P2RuntimeIntegrationTests' `
        --verbosity normal
    if ($LASTEXITCODE -ne 0) {
        throw "P2 paired-runtime validation failed with exit code $LASTEXITCODE."
    }
}
finally {
    $env:REELFORGE_GATE0_P2_RUNTIME_ROOT = $previousRuntimeRoot
    $env:REELFORGE_GATE0_P2_MANIFEST = $previousManifest
    $env:REELFORGE_GATE0_P2_EVIDENCE_PATH = $previousEvidence
}

Write-Output "P2 paired-runtime evidence: $([System.IO.Path]::GetFullPath($EvidencePath))"
