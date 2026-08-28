[CmdletBinding()]
param(
 [Parameter(Mandatory)][string]$ArtifactRoot,[Parameter(Mandatory)][string]$SourceRoot,
 [Parameter(Mandatory)][string]$ProofRunId,[Parameter(Mandatory)][string]$EvidenceGroupId,[Parameter(Mandatory)][string]$CellId,
 [ValidateSet('containment-no-media','p2-runtime-route')][string]$EvidenceBoundary='containment-no-media',
 [Parameter(Mandatory)][string[]]$ContractIdentity,[Parameter(Mandatory)][string]$Provenance,[Parameter(Mandatory)][string[]]$ProducerRuntimeIdentity,
 [string[]]$LicenseRecords=@(),[string]$ContinuationAuthorizationPath='',[string]$AttemptsPath='',[string]$ApprovedSourceRoot='',
 [ValidateSet('None','BeforeRemoteVerification','AfterRemoteVerification','AfterPayloadMove','AfterShardMove','AfterRootReplacement')][string]$FaultInjection='None',[switch]$SkipRemoteForIsolatedTest)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'evidence/Gate0EvidenceContainmentV2.psm1') -Force
if (-not $SkipRemoteForIsolatedTest) { Import-Module (Join-Path $PSScriptRoot 'Gate0ArtifactTools.psm1') -Force }

function Assert-Isolated([string]$Repo,[string]$Artifact) {
 $tmp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
 if(-not $Repo.StartsWith($tmp,[StringComparison]::OrdinalIgnoreCase)-or -not $Artifact.StartsWith($tmp,[StringComparison]::OrdinalIgnoreCase)-or -not(Test-Path (Join-Path $Repo '.gate0-containment-test-sentinel'))-or(Test-Path (Join-Path $Repo '.git'))){throw 'Fault injection and remote bypass require an isolated sentinel test corpus.'}
}
function Assert-DirectoryTreeHasNoReparsePoint([string] $Root, [string] $Label) {
 foreach ($item in @((Get-Item -LiteralPath $Root -Force)) + @(Get-ChildItem -LiteralPath $Root -Force -Recurse)) {
  if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "$Label contains a reparse point: $($item.FullName)" }
 }
}
function Assert-SafeWriteTarget([string]$Path,[string]$ApprovedRoot) {
 $full=[IO.Path]::GetFullPath($Path);$root=[IO.Path]::GetFullPath($ApprovedRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
 if(-not($full.Equals($root,[StringComparison]::OrdinalIgnoreCase)-or$full.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase))){throw 'V2 write target escaped its approved root.'}
 $current=$full;while(-not(Test-Path -LiteralPath $current)){ $parent=[IO.Path]::GetDirectoryName($current);if([string]::IsNullOrEmpty($parent)-or$parent-eq$current){throw 'V2 write target has no existing approved ancestor.'};$current=$parent }
 Assert-Gate0EvidenceV2NoReparsePointAncestors $current $root
}
function Assert-StagingCleanupClosure([string]$StagingRoot,$Artifacts,[string]$PayloadRoot) {
 Assert-DirectoryTreeHasNoReparsePoint $StagingRoot 'V2 transaction staging directory'
 $expected=@{};foreach($artifact in @($Artifacts)){$relative=([string]$artifact.relativePath).Substring($PayloadRoot.Length).Replace('\','/').ToLowerInvariant();if($expected.ContainsKey($relative)){throw 'V2 transaction staging closure contains duplicate paths.'};$expected[$relative]=$artifact}
 $actual=@{};foreach($file in @(Get-ChildItem -LiteralPath $StagingRoot -File -Force -Recurse)){$relative=[IO.Path]::GetRelativePath($StagingRoot,$file.FullName).Replace('\','/').ToLowerInvariant();if($actual.ContainsKey($relative)){throw 'V2 transaction staging closure contains duplicate physical paths.'};$actual[$relative]=$file}
 if($expected.Count-ne$actual.Count){throw 'V2 transaction staging cleanup refuses an unindexed or missing file.'}
 foreach($relative in $expected.Keys){if(-not$actual.ContainsKey($relative)-or[long]$actual[$relative].Length-ne[long]$expected[$relative].byteSize-or(Get-Gate0EvidenceV2Sha256 $actual[$relative].FullName)-ne$expected[$relative].sha256){throw 'V2 transaction staging cleanup refuses divergent bytes.'}}
}
function New-Artifact([string]$File,[string]$Relative) {
 $hash=Get-Gate0EvidenceV2Sha256 $File
 $logicalHash=Get-Gate0EvidenceV2TextSha256 $Relative
 [pscustomobject][ordered]@{artifactId=('artifact-'+$logicalHash.ToLowerInvariant());relativePath=$Relative;byteSize=[int64](Get-Item -LiteralPath $File).Length;sha256=$hash;r2ObjectKey="objects/sha256/$($hash.Substring(0,2).ToLowerInvariant())/$($hash.ToLowerInvariant())";purpose='Gate 0 V2 evidence.';retentionStatus='pending';transferDisposition=$null;remotelyVerifiedUtc=$null}
}
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')).TrimEnd([IO.Path]::DirectorySeparatorChar)
$artifact=[IO.Path]::GetFullPath($ArtifactRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
$source=[IO.Path]::GetFullPath($SourceRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
$sourceBoundary=if([string]::IsNullOrWhiteSpace($ApprovedSourceRoot)){$source}else{[IO.Path]::GetFullPath($ApprovedSourceRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)}
if([IO.Path]::GetDirectoryName($repo)-ne[IO.Path]::GetDirectoryName($artifact)){throw 'V2 ArtifactRoot must be a non-reparse sibling of the repository.'}
if([IO.Path]::GetDirectoryName($repo)-ne[IO.Path]::GetDirectoryName($sourceBoundary)){throw 'V2 approved source root must be a non-reparse sibling of the repository.'}
if($sourceBoundary.Equals($repo,[StringComparison]::OrdinalIgnoreCase)-or$sourceBoundary.Equals($artifact,[StringComparison]::OrdinalIgnoreCase)){throw 'V2 approved source root must be distinct from the repository and artifact root.'}
if(-not($source.Equals($sourceBoundary,[StringComparison]::OrdinalIgnoreCase)-or$source.StartsWith($sourceBoundary+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase))){throw 'V2 SourceRoot escaped the approved source root.'}
Assert-Gate0EvidenceV2NoReparsePointAncestors $repo ([IO.Path]::GetDirectoryName($repo));Assert-Gate0EvidenceV2NoReparsePointAncestors $artifact ([IO.Path]::GetDirectoryName($artifact))
if($FaultInjection-ne'None'-or$SkipRemoteForIsolatedTest){Assert-Isolated $repo $artifact}
foreach($value in @($ProofRunId,$EvidenceGroupId,$CellId)){Assert-Gate0EvidenceV2Identifier $value 'V2 identifier'}
foreach($identity in @($ContractIdentity)+@($ProducerRuntimeIdentity)+@($LicenseRecords)) { Assert-Gate0EvidenceV2MetadataText ([string]$identity) 'V2 append identity' }
Assert-Gate0EvidenceV2MetadataText $Provenance 'V2 append provenance'
if ([string]::IsNullOrWhiteSpace($Provenance) -or @($ContractIdentity).Count -eq 0 -or @($ProducerRuntimeIdentity).Count -eq 0) { throw 'V2 append metadata is required.' }
 $attempts=@();$liveScheduledRows=$null
 if($EvidenceBoundary -eq 'p2-runtime-route') {
   $fixedWriterAuthorizationPath=Join-Path $PSScriptRoot 'g0.5-stage2a-continuation-v2-writer-authorization.json'
   $fixedContinuationAuthorizationPath=Join-Path $PSScriptRoot 'g0.5-stage2a-continuation-authorization.json'
   $fixedContinuationSchedulePath=Join-Path $PSScriptRoot 'g0.5-stage2a-continuation-schedule.json'
   if($SkipRemoteForIsolatedTest) {
     if(-not $ContinuationAuthorizationPath -or -not(Test-Path -LiteralPath $ContinuationAuthorizationPath -PathType Leaf)){throw 'V2 runtime-route append is blocked until an exact future continuation authorization exists.'}
     $authText=Get-Content -LiteralPath $ContinuationAuthorizationPath -Raw;Assert-Gate0EvidenceV2MetadataText $authText 'V2 continuation authorization';$auth=$authText|ConvertFrom-Json -Depth 32
     Assert-Gate0EvidenceV2ExactProperties $auth @('schemaVersion','authorizationId','authorizationScope','continuationProofRunIds','limitations') 'V2 continuation authorization'
     if($auth.schemaVersion -ne 1 -or $auth.authorizationId -ne 'Gate0.Stage2Evidence.V2.ContinuationAuthorization.V1' -or $auth.authorizationScope -ne 'owner-authorized-v2-continuation' -or @($auth.continuationProofRunIds|Where-Object {$_ -eq $ProofRunId}).Count -ne 1){throw 'V2 continuation authorization is not exact for this proof run.'}
   } else {
     if(-not (Test-Path -LiteralPath $fixedWriterAuthorizationPath -PathType Leaf) -or -not (Test-Path -LiteralPath $fixedContinuationAuthorizationPath -PathType Leaf) -or -not (Test-Path -LiteralPath $fixedContinuationSchedulePath -PathType Leaf)){throw 'V2 runtime-route append is blocked until both fixed tracked continuation authorizations and the exact schedule exist.'}
     if($ContinuationAuthorizationPath -and -not([IO.Path]::GetFullPath($ContinuationAuthorizationPath).Equals([IO.Path]::GetFullPath($fixedWriterAuthorizationPath),[StringComparison]::OrdinalIgnoreCase))){throw 'Live V2 runtime-route append cannot override the fixed tracked writer authorization.'}
     Import-Module (Join-Path $PSScriptRoot 'G05Stage2AContinuationHelpers.psm1') -Force
     $fullAuthorization=Read-G05Stage2AContinuationAuthorization $fixedContinuationAuthorizationPath $repo $fixedContinuationSchedulePath
     $writerAuthorizationText=Get-Content -LiteralPath $fixedWriterAuthorizationPath -Raw;Assert-Gate0EvidenceV2MetadataText $writerAuthorizationText 'V2 continuation writer authorization';$auth=$writerAuthorizationText|ConvertFrom-Json -Depth 32
     Assert-Gate0EvidenceV2ExactProperties $auth @('schemaVersion','authorizationId','authorizationScope','continuationProofRunIds','limitations') 'V2 continuation writer authorization'
     $writerProofIds=@($auth.continuationProofRunIds|ForEach-Object{[string]$_})
     $scheduledProofIds=@($fullAuthorization.Schedule.ProofRunIds|ForEach-Object{[string]$_}|Sort-Object)
     if($auth.schemaVersion -ne 1 -or $auth.authorizationId -ne 'Gate0.Stage2Evidence.V2.ContinuationAuthorization.V1' -or $auth.authorizationScope -ne 'owner-authorized-v2-continuation' -or $writerProofIds.Count -ne 12 -or @($writerProofIds|Where-Object {[string]::IsNullOrWhiteSpace($_)}).Count -ne 0 -or $writerProofIds.Count -ne @($writerProofIds|Sort-Object -Unique).Count -or (@($writerProofIds|Sort-Object)-join'|') -ne ($scheduledProofIds-join'|') -or @($writerProofIds|Where-Object {$_ -eq $ProofRunId}).Count -ne 1){throw 'V2 continuation writer authorization is not the exact approved twelve-proof set for this proof run.'}
     $writerBinding=@($fullAuthorization.Authorization.bindings|Where-Object { $_.role -eq 'v2-writer-authorization' })
     if($writerBinding.Count -ne 1 -or $writerBinding[0].path -ne 'eng/gate0/g0.5-stage2a-continuation-v2-writer-authorization.json' -or $writerBinding[0].sha256 -ne (Get-G05Stage2AContinuationSha256 $fixedWriterAuthorizationPath)){throw 'The full continuation authorization does not bind the exact writer authorization bytes.'}
     $liveScheduledRows=@($fullAuthorization.Schedule.Schedule.attempts|Where-Object { $_.proofRunId -eq $ProofRunId }|Sort-Object continuationOrdinal)
     if($EvidenceGroupId -ne 'g05-stage2a-continuation-20260827'-or$liveScheduledRows.Count -ne 6-or@($liveScheduledRows|Where-Object { $_.cellId -ne $CellId }).Count -ne 0){throw 'V2 runtime-route append does not match the exact authorized continuation schedule cell.'}
   }
   if(-not $AttemptsPath -or -not(Test-Path -LiteralPath $AttemptsPath -PathType Leaf)){throw 'V2 continuation requires an exact attempt-binding document.'};$attemptText=Get-Content -LiteralPath $AttemptsPath -Raw;Assert-Gate0EvidenceV2MetadataText $attemptText 'V2 attempt bindings';$attempts=@($attemptText|ConvertFrom-Json -Depth 32)
   if(-not $SkipRemoteForIsolatedTest) {
     if($attempts.Count -ne 6){throw 'Live V2 continuation attempt bindings must contain the exact six scheduled rows.'}
     for($attemptIndex=0;$attemptIndex-lt6;$attemptIndex++) {
       $binding=$attempts[$attemptIndex];$scheduled=$liveScheduledRows[$attemptIndex]
       Assert-Gate0EvidenceV2ExactProperties $binding @('attemptId','originalAttemptId','phase','ordinal','retentionClass','recordPath','recordSha256','disposition','completeClosureReference') 'Live V2 continuation attempt binding'
       if([string]$binding.attemptId -ne "stage2a-continuation-$($scheduled.globalOrdinal)" -or [string]$binding.originalAttemptId -ne "stage2a-$($scheduled.originalScheduleOrdinal)" -or [string]$binding.phase -ne [string]$scheduled.phase -or [int]$binding.ordinal -ne [int]$scheduled.globalOrdinal){throw 'Live V2 continuation attempt bindings are missing, reordered, duplicated, or do not match the exact frozen schedule identities.'}
     }
     $completePassedAttempts=@($attempts|Where-Object { [string]$_.retentionClass -eq 'complete' -and [string]$_.disposition -eq 'passed' })
     foreach($binding in $attempts) {
       if([string]$binding.retentionClass -eq 'compact') {
         $target=@($completePassedAttempts|Where-Object { [string]$_.attemptId -eq [string]$binding.completeClosureReference })
         if([string]$binding.disposition -ne 'passed' -or [string]::IsNullOrWhiteSpace([string]$binding.completeClosureReference) -or $target.Count -ne 1){throw 'Live V2 continuation attempt bindings contain a compact closure reference that does not resolve to exactly one complete passed attempt.'}
       } elseif([string]$binding.retentionClass -eq 'complete') {
         if(-not [string]::IsNullOrWhiteSpace([string]$binding.completeClosureReference)){throw 'Live V2 continuation attempt bindings contain a complete attempt with a closure reference.'}
       } else { throw 'Live V2 continuation attempt bindings contain an unknown retention class.' }
     }
   }
 } elseif($AttemptsPath){throw 'V2 infrastructure append cannot carry media attempt bindings.'}
if(-not(Test-Path -LiteralPath $source -PathType Container)){throw 'V2 SourceRoot does not exist.'}
Assert-DirectoryTreeHasNoReparsePoint $source 'V2 SourceRoot'
$sourceFiles = @(Get-ChildItem -LiteralPath $source -File -Recurse | Sort-Object FullName)
if($sourceFiles.Count -eq 0){throw 'V2 SourceRoot contains no evidence files.'}
$sourceBytes = [int64](($sourceFiles | Measure-Object -Property Length -Sum).Sum)
$rootPath=Join-Path $PSScriptRoot 'evidence/v2/root-index.json';$stage=Join-Path $PSScriptRoot 'evidence/v2/stage2';$destName="future/stage2/v2/$ProofRunId";$dest=Join-Path $artifact ($destName.Replace('/','\'));$shardPath=Join-Path $stage "$ProofRunId.manifest.json";$journal="$artifact.stage2-v2-append-journal.json";$lockPath="$artifact.stage2-v2-append-lock";$lock=$null;$staging="$artifact.stage2-v2-staging-$([guid]::NewGuid().ToString('N'))";$tmpShard='';$rootCommitted=$false;$preserveRecovery=$false;$journalWritten=$false;$stagingCreated=$false;$records=@()
Assert-Gate0EvidenceV2NoReparsePointAncestors $source $sourceBoundary
Assert-Gate0EvidenceV2NoReparsePointAncestors $sourceBoundary ([IO.Path]::GetDirectoryName($repo))
try {
 if((Test-Path -LiteralPath $lockPath)-and((Get-Item -LiteralPath $lockPath -Force).Attributes-band[IO.FileAttributes]::ReparsePoint)){throw 'V2 append lock is a reparse point.'}
 $lock=[IO.File]::Open($lockPath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
 if(Test-Path -LiteralPath $journal){throw 'A prior V2 append journal requires recovery.'};$root=Read-Gate0EvidenceV2RootIndex $rootPath
 $kind=if($EvidenceBoundary -eq 'containment-no-media'){'infrastructure'}else{'stage2a-continuation-cell'};$limit=if($kind -eq'infrastructure'){2}else{12}
 if(@($root.Index.runs|Where-Object {$_.runKind-eq$kind}).Count-ge$limit-or(Test-Path -LiteralPath $dest)-or(Test-Path -LiteralPath $shardPath)){throw 'V2 candidate exceeds exact capacity or reuses an immutable path.'}
 if([int64]78538843+[int64]$root.Index.totals.logicalArtifactBytes+$sourceBytes-gt[int64]805306368){throw 'V2 candidate exceeds the global retention ceiling.'}
 Assert-Gate0EvidenceV2NoReparsePointAncestors (Split-Path -Parent $staging) ([IO.Path]::GetDirectoryName($artifact))
 Assert-Gate0EvidenceV2NoReparsePointAncestors (Split-Path -Parent $stage) $repo
 Assert-Gate0EvidenceV2NoReparsePointAncestors $artifact ([IO.Path]::GetDirectoryName($artifact))
 Assert-SafeWriteTarget $staging ([IO.Path]::GetDirectoryName($artifact));[IO.Directory]::CreateDirectory($staging)|Out-Null;$stagingCreated=$true;Assert-Gate0EvidenceV2NoReparsePointAncestors $staging ([IO.Path]::GetDirectoryName($artifact))
 foreach($file in $sourceFiles){$rel=[IO.Path]::GetRelativePath($source,$file.FullName).Replace('\','/');Assert-Gate0EvidenceV2RelativePath $rel 'source evidence path';$copy=Join-Path $staging ($rel.Replace('/','\'));[IO.Directory]::CreateDirectory((Split-Path -Parent $copy))|Out-Null;Copy-Item -LiteralPath $file.FullName -Destination $copy;if((Get-Gate0EvidenceV2Sha256 $copy)-ne(Get-Gate0EvidenceV2Sha256 $file.FullName)){throw 'V2 staging copy failed byte verification.'};$records+=New-Artifact $copy "$destName/$rel"}
 $bytes=[int64](($records|Measure-Object byteSize -Sum).Sum)
 if([int64]78538843+[int64]$root.Index.totals.logicalArtifactBytes+$bytes-gt[int64]805306368){throw 'V2 staged evidence exceeds the global retention ceiling.'}
 $currentSourceFiles=@(Get-ChildItem -LiteralPath $source -File -Recurse|ForEach-Object{[IO.Path]::GetRelativePath($source,$_.FullName).Replace('\','/')}|Sort-Object)
 $initialSourceFiles=@($sourceFiles|ForEach-Object{[IO.Path]::GetRelativePath($source,$_.FullName).Replace('\','/')}|Sort-Object)
 if(@(Compare-Object $initialSourceFiles $currentSourceFiles).Count){throw 'V2 source file set changed during staging copy.'}
 foreach($file in $sourceFiles){$relative=[IO.Path]::GetRelativePath($source,$file.FullName).Replace('\','/');$staged=Join-Path $staging ($relative.Replace('/','\'));if(-not(Test-Path -LiteralPath $staged -PathType Leaf)-or(Get-Item -LiteralPath $staged).Length-ne$file.Length-or(Get-Gate0EvidenceV2Sha256 $staged)-ne(Get-Gate0EvidenceV2Sha256 $file.FullName)){throw 'V2 source changed during staging copy.'}}
 Assert-SafeWriteTarget $stage $repo;[IO.Directory]::CreateDirectory($stage) | Out-Null
 Assert-Gate0EvidenceV2NoReparsePointAncestors $stage $repo
 $stagedRecords = @($records | ForEach-Object { [ordered]@{ artifactId=$_.artifactId;relativePath=$_.relativePath;byteSize=$_.byteSize;sha256=$_.sha256;r2ObjectKey=$_.r2ObjectKey;purpose=$_.purpose } })
 $preflightRecords=@($records|ForEach-Object{[ordered]@{artifactId=$_.artifactId;relativePath=$_.relativePath;byteSize=$_.byteSize;sha256=$_.sha256;r2ObjectKey=$_.r2ObjectKey;purpose=$_.purpose;retentionStatus='remote-verified';transferDisposition='independently-retrieved-and-verified';remotelyVerifiedUtc='2099-12-31T23:59:59.9999999+00:00'}})
 $preflightShard=[ordered]@{schemaVersion=1;shardId='Gate0.Stage2Evidence.Shard.V2';proofRunId=$ProofRunId;evidenceGroupId=$EvidenceGroupId;cellId=$CellId;evidenceBoundary=$EvidenceBoundary;createdUtc='2099-12-31T23:59:59.9999999+00:00';contractIdentity=@($ContractIdentity);provenance=$Provenance;producerRuntimeIdentity=@($ProducerRuntimeIdentity);licenseRecords=@($LicenseRecords);artifacts=$preflightRecords;attempts=@($attempts);disposition='authoritative';localRetention='verified';r2Retention='independently-retrieved-and-verified';totals=[ordered]@{logicalArtifactCount=$records.Count;logicalArtifactBytes=$bytes};limitations=@('V2 proof infrastructure only.')}
 $preflightText=(ConvertTo-Json $preflightShard -Depth 64)+"`n";$preflightBytes=[Text.Encoding]::UTF8.GetByteCount($preflightText);$preflightLines=($preflightText -split "`n").Count-1
 if($preflightBytes-gt65536-or$preflightLines-gt300){throw 'V2 candidate evidence shard exceeds its cap before journal or remote work.'}
 $record=[ordered]@{schemaVersion=1;journalId='Gate0.Stage2Evidence.V2.AppendJournal.V1';proofRunId=$ProofRunId;phase='prepared';oldRootIndexSha256=$root.Sha256;payloadRoot="$destName/";stagingDirectoryName=(Split-Path -Leaf $staging);artifactCount=$records.Count;artifactBytes=$bytes;stagedArtifacts=$stagedRecords};Write-Gate0EvidenceV2Utf8Atomic $journal (($record|ConvertTo-Json -Depth 32)+"`n");$journalWritten=$true
 if($FaultInjection-eq'BeforeRemoteVerification'){$preserveRecovery=$true;throw 'Injected V2 fault.'}
 if(-not $SkipRemoteForIsolatedTest) {
   $bundle=New-Gate0R2ClientBundle
   try { foreach($entry in $records) { $local=Join-Path $staging (($entry.relativePath.Substring($destName.Length+1)).Replace('/','\'));$head=$bundle.Client.HeadObjectAsync($bundle.BucketName,$entry.r2ObjectKey).GetAwaiter().GetResult();if($null -eq $head){$created=$bundle.Client.PutObjectIfAbsentAsync($bundle.BucketName,$entry.r2ObjectKey,$local,$entry.sha256).GetAwaiter().GetResult();$entry.transferDisposition=if($created){'uploaded-and-verified'}else{'concurrent-create-verified'}}else{$entry.transferDisposition='existing-object-verified'};Invoke-Gate0RemoteByteVerification $bundle ([pscustomobject]@{ArtifactId=$entry.artifactId;ObjectKey=$entry.r2ObjectKey;Size=$entry.byteSize;Sha256=$entry.sha256});$entry.retentionStatus='remote-verified';$entry.remotelyVerifiedUtc=[DateTimeOffset]::UtcNow.ToString('O')} } finally { $bundle.HttpClient.Dispose() }
 } else {
   foreach($entry in $records){$entry.transferDisposition='independently-retrieved-and-verified';$entry.retentionStatus='remote-verified';$entry.remotelyVerifiedUtc='2099-12-31T23:59:59.9999999+00:00'}
 }
 $shard=[ordered]@{schemaVersion=1;shardId='Gate0.Stage2Evidence.Shard.V2';proofRunId=$ProofRunId;evidenceGroupId=$EvidenceGroupId;cellId=$CellId;evidenceBoundary=$EvidenceBoundary;createdUtc=[DateTimeOffset]::UtcNow.ToString('O');contractIdentity=@($ContractIdentity);provenance=$Provenance;producerRuntimeIdentity=@($ProducerRuntimeIdentity);licenseRecords=@($LicenseRecords);artifacts=@($records);attempts=@($attempts);disposition='authoritative';localRetention='verified';r2Retention='independently-retrieved-and-verified';totals=[ordered]@{logicalArtifactCount=$records.Count;logicalArtifactBytes=$bytes};limitations=@('V2 proof infrastructure only.')}
 Assert-Gate0EvidenceV2NoReparsePointAncestors $stage $repo;$tmpShard="$shardPath.tmp-$([guid]::NewGuid().ToString('N'))";[IO.File]::WriteAllText($tmpShard,(ConvertTo-Json $shard -Depth 64)+"`n",[Text.UTF8Encoding]::new($false));$s=Read-Gate0EvidenceV2Shard $tmpShard;$last=if(@($root.Index.runs).Count){@($root.Index.runs)[-1]}else{$null}
 $entry=[ordered]@{ordinal=@($root.Index.runs).Count+1;runKind=$kind;proofRunId=$ProofRunId;evidenceGroupId=$EvidenceGroupId;cellId=$CellId;shardPath="stage2/$ProofRunId.manifest.json";shardSha256=$s.Sha256;entrySha256='';previousRunId=if($last){$last.proofRunId}else{$null};previousRunEntrySha256=if($last){$last.entrySha256}else{$null};disposition='authoritative';logicalArtifactCount=$records.Count;logicalArtifactBytes=$bytes;localRetention='verified';r2Retention='independently-retrieved-and-verified'};$entry.entrySha256=Get-Gate0EvidenceV2EntryHash([pscustomobject]$entry);$candidate=$root.Index.PSObject.Copy();$candidate.runs=@($root.Index.runs)+@([pscustomobject]$entry);$candidate.totals=[ordered]@{runCount=$candidate.runs.Count;logicalArtifactCount=[int]$root.Index.totals.logicalArtifactCount+$records.Count;logicalArtifactBytes=[int64]$root.Index.totals.logicalArtifactBytes+$bytes};$candidateText=(ConvertTo-Json $candidate -Depth 64)+"`n";$candidateHash=Get-Gate0EvidenceV2TextSha256 $candidateText
 $candidateTemporary="$rootPath.tmp-$([Guid]::NewGuid().ToString('N'))";[IO.File]::WriteAllText($candidateTemporary,$candidateText,[Text.UTF8Encoding]::new($false));try{$candidateShape=Get-Gate0EvidenceV2Shape $candidateTemporary;if($candidateShape.Bytes -gt 131072 -or $candidateShape.Lines -gt 400){throw 'V2 candidate root index exceeds its cap.'}}finally{if(Test-Path -LiteralPath $candidateTemporary -PathType Leaf){Remove-Item -LiteralPath $candidateTemporary -Force}}
 $record=[ordered]@{schemaVersion=1;journalId='Gate0.Stage2Evidence.V2.AppendJournal.V1';proofRunId=$ProofRunId;phase='remote-verified';oldRootIndexSha256=$root.Sha256;candidateRootIndexSha256=$candidateHash;shardPath=$entry.shardPath;shardSha256=$s.Sha256;payloadRoot="$destName/";stagingDirectoryName=(Split-Path -Leaf $staging);artifactCount=$records.Count;artifactBytes=$bytes;stagedArtifacts=$stagedRecords;artifacts=@($records)};Write-Gate0EvidenceV2Utf8Atomic $journal (($record|ConvertTo-Json -Depth 32)+"`n");if($FaultInjection-eq'AfterRemoteVerification'){$preserveRecovery=$true;throw 'Injected V2 fault.'};Assert-SafeWriteTarget (Split-Path -Parent $dest) $artifact;[IO.Directory]::CreateDirectory((Split-Path -Parent $dest))|Out-Null;Assert-Gate0EvidenceV2NoReparsePointAncestors (Split-Path -Parent $dest) $artifact;[IO.Directory]::Move($staging,$dest);if($FaultInjection-eq'AfterPayloadMove'){$preserveRecovery=$true;throw 'Injected V2 fault.'};Assert-Gate0EvidenceV2NoReparsePointAncestors (Split-Path -Parent $shardPath) $repo;[IO.File]::Move($tmpShard,$shardPath);if($FaultInjection-eq'AfterShardMove'){$preserveRecovery=$true;throw 'Injected V2 fault.'};Assert-Gate0EvidenceV2NoReparsePointAncestors $rootPath $repo;Write-Gate0EvidenceV2Utf8Atomic $rootPath $candidateText;$rootCommitted=$true;if($FaultInjection-eq'AfterRootReplacement'){$preserveRecovery=$true;throw 'Injected V2 fault.'};Assert-Gate0EvidenceV2NoReparsePointAncestors $journal ([IO.Path]::GetDirectoryName($artifact));Remove-Item -LiteralPath $journal -Force
} catch {
 if(-not $rootCommitted -and -not $preserveRecovery) {
  if(Test-Path -LiteralPath $tmpShard -PathType Leaf){Assert-Gate0EvidenceV2NoReparsePointAncestors $tmpShard $repo;Remove-Item -LiteralPath $tmpShard -Force}
  if($stagingCreated-and(Test-Path -LiteralPath $staging -PathType Container)){Assert-Gate0EvidenceV2NoReparsePointAncestors $staging ([IO.Path]::GetDirectoryName($artifact));Assert-StagingCleanupClosure $staging $records "$destName/";Remove-Item -LiteralPath $staging -Recurse -Force}
  if($journalWritten-and(Test-Path -LiteralPath $journal -PathType Leaf)){Assert-Gate0EvidenceV2NoReparsePointAncestors $journal ([IO.Path]::GetDirectoryName($artifact));Remove-Item -LiteralPath $journal -Force}
 }
 throw
} finally {if($lock){$lock.Dispose()}}
