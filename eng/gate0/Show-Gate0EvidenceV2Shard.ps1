[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'evidence/Gate0EvidenceContainmentV2.psm1') -Force

# This is intentionally inspection-only: the reader validates the stored bytes,
# then the validated manifest is rendered to stdout for human review.
$shard = Read-Gate0EvidenceV2Shard $Path
$shard.Manifest | ConvertTo-Json -Depth 64
