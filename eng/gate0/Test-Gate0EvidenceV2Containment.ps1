[CmdletBinding()]
param([Parameter(Mandatory)][string] $ArtifactRoot, [switch] $Remote, [string] $OutputPath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'evidence/Gate0EvidenceContainmentV2.psm1') -Force
if ($Remote) { Import-Module (Join-Path $PSScriptRoot 'Gate0ArtifactTools.psm1') -Force }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')).TrimEnd([IO.Path]::DirectorySeparatorChar)
$artifact = [IO.Path]::GetFullPath($ArtifactRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
if ([IO.Path]::GetDirectoryName($repo) -ne [IO.Path]::GetDirectoryName($artifact)) { throw 'V2 artifact root must be a sibling of the repository.' }
Assert-Gate0EvidenceV2NoReparsePointAncestors $repo ([IO.Path]::GetDirectoryName($repo))
Assert-Gate0EvidenceV2NoReparsePointAncestors $artifact ([IO.Path]::GetDirectoryName($artifact))
$rootPath = Join-Path $PSScriptRoot 'evidence/v2/root-index.json'
$root = Read-Gate0EvidenceV2RootIndex $rootPath
$indexed = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$count = 0; $bytes = [int64]0; $remoteCount = 0; $bundle = $null
try {
 if ($Remote) { $bundle = New-Gate0R2ClientBundle }
 foreach($run in @($root.Index.runs)) {
  $shard = Read-Gate0EvidenceV2Shard (Join-Path (Split-Path -Parent $rootPath) (($run.shardPath).Replace('/','\')))
  foreach($entry in @($shard.Manifest.artifacts)) {
   [void]$indexed.Add([string]$entry.relativePath)
   $local = [IO.Path]::GetFullPath((Join-Path $artifact (($entry.relativePath).Replace('/','\'))))
   if(-not $local.StartsWith("$artifact$([IO.Path]::DirectorySeparatorChar)",[StringComparison]::OrdinalIgnoreCase) -or -not(Test-Path -LiteralPath $local -PathType Leaf)){throw 'V2 indexed artifact is missing or escaped.'}
   Assert-Gate0EvidenceV2NoReparsePointAncestors $local $artifact
   if((Get-Item -LiteralPath $local).Length -ne [int64]$entry.byteSize -or (Get-Gate0EvidenceV2Sha256 $local) -ne $entry.sha256){throw 'V2 local artifact byte verification failed.'}
   if($Remote){$remoteEntry=[pscustomobject]@{ArtifactId=$entry.artifactId;ObjectKey=$entry.r2ObjectKey;Size=$entry.byteSize;Sha256=$entry.sha256};Invoke-Gate0RemoteByteVerification $bundle $remoteEntry;$remoteCount++}
   $count++;$bytes += [int64]$entry.byteSize
  }
 }
} finally { if($bundle){$bundle.HttpClient.Dispose()} }
$future = Join-Path $artifact 'future/stage2/v2'
if(Test-Path -LiteralPath $future -PathType Container){
 $items = @(Get-ChildItem -LiteralPath $future -Force -Recurse)
 foreach($item in $items){if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint)-ne 0){throw 'V2 physical closure contains a reparse point.'}}
 foreach($file in @($items|Where-Object{-not $_.PSIsContainer})){$relative=[IO.Path]::GetRelativePath($artifact,$file.FullName).Replace('\','/');if(-not $indexed.Contains($relative)){throw 'V2 physical closure contains an unindexed file.'}}
}
$result=[ordered]@{schemaVersion=1;validationId='Gate0.Stage2Evidence.Validation.V2';v1RootIndexSha256=$root.Predecessor.Sha256;v2RootIndexSha256=$root.Sha256;runCount=@($root.Index.runs).Count;logicalArtifactCount=$count;logicalArtifactBytes=$bytes;localByteVerificationPerformed=$true;remoteByteVerificationPerformed=[bool]$Remote;remotelyVerifiedThisRun=$remoteCount;mediaProcessesInvoked=0;disposition='passed'}
if($OutputPath){$output=[IO.Path]::GetFullPath($OutputPath);$parent=Split-Path -Parent $output;Assert-Gate0EvidenceV2NoReparsePointAncestors $parent $parent;Write-Gate0EvidenceV2Utf8Atomic $output (($result|ConvertTo-Json -Depth 16)+"`n")}
[pscustomobject]$result
