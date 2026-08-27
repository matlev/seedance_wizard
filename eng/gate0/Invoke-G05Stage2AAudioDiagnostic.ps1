[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $FixtureRoot,
    [Parameter(Mandatory)] [string] $WorkloadContractPath,
    [Parameter(Mandatory)] [string] $OracleContractPath,
    [Parameter(Mandatory)] [string] $AmendmentPath,
    [Parameter(Mandatory)] [string] $FreezePath,
    [Parameter(Mandatory)] [string] $WebmDecodedPcmPath,
    [Parameter(Mandatory)] [string] $WebmAttemptSummaryPath,
    [Parameter(Mandatory)] [string] $WebmAttemptSummarySha256,
    [Parameter(Mandatory)] [string] $Mp4DecodedPcmPath,
    [Parameter(Mandatory)] [string] $Mp4AttemptSummaryPath,
    [Parameter(Mandatory)] [string] $Mp4AttemptSummarySha256,
    [Parameter(Mandatory)] [string] $MatrixRunnerPath,
    [Parameter(Mandatory)] [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'G05Stage2AAudioDiagnostic.psm1') -Force
Invoke-G05Stage2AAudioDiagnostic @PSBoundParameters
