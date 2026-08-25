[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CorpusRoot,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateRange(1, 60)][int]$OpenTimeoutSeconds = 15,
    [ValidateRange(1, 60)][int]$EndedTimeoutSeconds = 20
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Gate 0 optional Windows evidence only. This never establishes a portable
# playback guarantee, a shipping runtime, codec/patent conclusion, or an
# audible/perceptual A/V-sync result.
function Require-OutsideRepositoryEmptyDirectory([string]$Path) {
    if (-not [IO.Path]::IsPathRooted($Path)) { throw 'OutputDirectory must be an explicit rooted path outside the repository.' }
    $repository = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path.TrimEnd('\', '/')
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    if ($full.Equals($repository, [StringComparison]::OrdinalIgnoreCase) -or $full.StartsWith("$repository\", [StringComparison]::OrdinalIgnoreCase)) { throw 'OutputDirectory must be outside the repository.' }
    $ancestor = $full
    while (-not (Test-Path -LiteralPath $ancestor)) { $parent = Split-Path -Parent $ancestor; if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $ancestor) { throw 'OutputDirectory has no existing ancestor.' }; $ancestor = $parent }
    while ($true) { $item = Get-Item -LiteralPath $ancestor -Force; if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "OutputDirectory ancestor is a reparse-point: $ancestor" }; $parent = Split-Path -Parent $ancestor; if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $ancestor) { break }; $ancestor = $parent }
    if (Test-Path -LiteralPath $full) { if (-not (Test-Path -LiteralPath $full -PathType Container) -or (Get-ChildItem -LiteralPath $full -Force | Select-Object -First 1)) { throw 'OutputDirectory must be new or empty so evidence cannot include stale files.' } } else { New-Item -ItemType Directory -Path $full | Out-Null }
    return $full
}
function Assert-RootedNonReparseDirectory([string]$Path, [string]$Name) {
    if (-not [IO.Path]::IsPathRooted($Path) -or -not (Test-Path -LiteralPath $Path -PathType Container)) { throw "$Name must be an existing explicit rooted directory." }
    $resolved = (Resolve-Path -LiteralPath $Path).Path.TrimEnd('\', '/')
    $current = $resolved
    while ($true) { $item = Get-Item -LiteralPath $current -Force; if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "$Name or an ancestor is a reparse-point: $current" }; $parent = Split-Path -Parent $current; if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $current) { break }; $current = $parent }
    return $resolved
}
function Assert-ContainedNonReparseFile([string]$Root, [string]$RelativePath, [string]$Name) {
    if ([string]::IsNullOrWhiteSpace($RelativePath) -or [IO.Path]::IsPathRooted($RelativePath)) { throw "$Name contains an unsafe path." }
    $normal = $RelativePath.Replace('\', '/')
    if (@($normal.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count) { throw "$Name contains an unsafe path." }
    $candidate = [IO.Path]::GetFullPath((Join-Path $Root $normal.Replace('/', [IO.Path]::DirectorySeparatorChar)))
    if (-not $candidate.StartsWith("$Root$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase)) { throw "$Name escapes CorpusRoot." }
    $current = $Root
    foreach ($part in $normal.Split('/')) { $current = Join-Path $current $part; if (-not (Test-Path -LiteralPath $current)) { throw "$Name is missing: $normal" }; if (((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "$Name contains a reparse-point: $normal" } }
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { throw "$Name is not a file: $normal" }
    return $candidate
}
function Assert-RootedNonReparseFile([string]$Path, [string]$Name) {
    if (-not [IO.Path]::IsPathRooted($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "$Name must be an existing explicit rooted file." }
    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $current = Split-Path -Parent $resolved
    while ($true) { $item = Get-Item -LiteralPath $current -Force; if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "$Name or an ancestor is a reparse-point: $current" }; $parent = Split-Path -Parent $current; if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $current) { break }; $current = $parent }
    return $resolved
}
function Write-AtomicJson([string]$Path, [object]$Value) {
    $partial = Join-Path (Split-Path -Parent $Path) ('.partial-' + [IO.Path]::GetFileName($Path))
    [IO.File]::WriteAllText($partial, ($Value | ConvertTo-Json -Depth 30), [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $partial -Destination $Path -Force
}
function Get-ExecutableIdentity([string]$Path) {
    if (-not $Path -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    $item = Get-Item -LiteralPath $Path
    return [ordered]@{ path = $item.FullName; length = $item.Length; sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToUpperInvariant(); fileVersion = $item.VersionInfo.FileVersion; productVersion = $item.VersionInfo.ProductVersion }
}
function Assert-ArtifactBinding([string]$Root, [object]$Binding, [string]$Name) {
    if ($null -eq $Binding -or [string]::IsNullOrWhiteSpace([string]$Binding.path) -or [string]$Binding.sha256 -notmatch '^[A-F0-9]{64}$' -or [int64]$Binding.length -lt 0) { throw "$Name binding is invalid." }
    $file = Assert-ContainedNonReparseFile $Root ([string]$Binding.path) $Name
    $item = Get-Item -LiteralPath $file
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($item.Length -ne [int64]$Binding.length -or $hash -ne ([string]$Binding.sha256).ToUpperInvariant()) { throw "$Name hash or length mismatch." }
    return $file
}
function Get-OsIdentity {
    $environmentVersion = [Environment]::OSVersion.Version
    $registry = $null; $registryFailure = $null; $wmi = $null; $wmiFailure = $null
    try { $registry = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -ErrorAction Stop } catch { $registryFailure = $_.Exception.Message }
    try { $wmi = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop | Select-Object Caption, Version, BuildNumber, OSArchitecture } catch { $wmiFailure = $_.Exception.Message }
    $product = if ($registry) { [string]$registry.ProductName } else { $null }
    $build = if ($registry) { [string]$registry.CurrentBuildNumber } else { $null }; if ([string]::IsNullOrWhiteSpace($build) -and $registry) { $build = [string]$registry.CurrentBuild }
    $ubr = if ($registry -and $null -ne $registry.UBR) { [string]$registry.UBR } else { $null }
    $exact = -not [string]::IsNullOrWhiteSpace($product) -and -not [string]::IsNullOrWhiteSpace($build) -and -not [string]::IsNullOrWhiteSpace($ubr)
    return [ordered]@{ exactIdentityAvailable=$exact; environmentVersion=$environmentVersion.ToString(); productName=$product; displayVersion=if($registry){[string]$registry.DisplayVersion}else{$null}; editionId=if($registry){[string]$registry.EditionID}else{$null}; currentBuild=$build; ubr=$ubr; buildLabEx=if($registry){[string]$registry.BuildLabEx}else{$null}; registryStatus=if($registry){'available'}else{'unavailable'}; registryFailure=$registryFailure; wmi=$wmi; wmiFailure=$wmiFailure }
}
function Assert-PlaybackCorpusProvenance([string]$Root, [object]$Manifest, [string]$ManifestFile) {
    if ($Manifest.schemaVersion -ne 2 -or $Manifest.kind -ne 'ReelForge Gate 0 independent-playback manual harness corpus' -or (@($Manifest.routes).Count + @($Manifest.blockedRoutes).Count) -ne 4) { throw 'Playback manifest must be the fresh schemaVersion 2 corpus with the exact approved kind and four route dispositions.' }
    $evidenceFile = Assert-ContainedNonReparseFile $Root 'g0.4-playback-corpus-evidence.json' 'Playback corpus preparation evidence'
    try { $evidence = Get-Content -LiteralPath $evidenceFile -Raw | ConvertFrom-Json } catch { throw 'Playback corpus preparation evidence is invalid JSON.' }
    if ($evidence.schemaVersion -ne 2 -or $evidence.profileId -ne 'P2.BtbnLgplShared.WindowsX64.20260820' -or $evidence.preflight.status -ne 'passed' -or @($evidence.routes).Count -ne 4) { throw 'Playback corpus preparation evidence does not bind the fresh approved v2 profile, passed preflight, and four route dispositions.' }
    $manifestBinding = $evidence.manifest
    if ((Assert-ArtifactBinding $Root $manifestBinding 'Corpus manifest') -ne $ManifestFile) { throw 'Corpus preparation evidence manifest binding does not resolve to manifest.json.' }
    $indexFile = Assert-ArtifactBinding $Root $evidence.indexHtml 'Corpus index HTML'
    if ([IO.Path]::GetFileName($indexFile) -ne 'index.html') { throw 'Corpus preparation evidence index binding does not resolve to index.html.' }
    $sourcePath = Assert-RootedNonReparseFile ([string]$evidence.sourceEvidence.path) 'Source G0.4 delivery evidence'
    if ((Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToUpperInvariant() -ne ([string]$evidence.sourceEvidence.sha256).ToUpperInvariant()) { throw 'Source G0.4 delivery evidence hash does not match corpus provenance.' }
    try { $sourceEvidence = Get-Content -LiteralPath $sourcePath -Raw | ConvertFrom-Json } catch { throw 'Source G0.4 delivery evidence is invalid JSON.' }
    if ($sourceEvidence.schemaVersion -ne 1 -or $sourceEvidence.profileId -ne $evidence.profileId -or $sourceEvidence.preflight.status -ne 'passed') { throw 'Source G0.4 delivery evidence does not bind the approved profile and passed preflight.' }
    $sourceRoot = Split-Path -Parent $sourcePath
    $sourceRuntimeFile = Assert-ArtifactBinding $sourceRoot $evidence.sourceEvidence.runtimeIdentity 'Source runtime identity'
    $sourceRuntime = Get-Content -LiteralPath $sourceRuntimeFile -Raw | ConvertFrom-Json
    $runtimeFile = Assert-ArtifactBinding $Root $evidence.runtimeIdentityEvidence 'Corpus runtime identity'
    $runtime = Get-Content -LiteralPath $runtimeFile -Raw | ConvertFrom-Json
    $sourceHash = ([string]$sourceRuntime.observation.PrimaryTool.Sha256).ToUpperInvariant()
    if ($sourceHash -notmatch '^[A-F0-9]{64}$' -or $sourceHash -ne ([string]$runtime.observation.PrimaryTool.Sha256).ToUpperInvariant() -or $sourceHash -ne ([string]$evidence.sourceRuntimePrimaryToolSha256).ToUpperInvariant()) { throw 'Corpus provenance does not bind the source and corpus runtime primary-tool hashes.' }
    $bound = @($evidence.boundArtifacts); $boundPaths = @($bound | ForEach-Object path)
    if ($bound.Count -eq 0 -or @($boundPaths | Sort-Object -Unique).Count -ne $bound.Count) { throw 'Corpus bound-artifact provenance is missing or duplicates a path.' }
    foreach ($binding in $bound) { Assert-ArtifactBinding $Root $binding 'Corpus bound artifact' | Out-Null }
    $actual = @(Get-ChildItem -LiteralPath $Root -File -Recurse -Force | ForEach-Object { $_.FullName.Substring($Root.Length).TrimStart('\','/').Replace('\','/') } | Where-Object { $_ -notin @('manifest.json','g0.4-playback-corpus-evidence.json') } | Sort-Object)
    if (@(Compare-Object $actual @($boundPaths | Sort-Object)).Count -ne 0) { throw 'Corpus bound-artifact provenance does not exactly cover the current corpus file set.' }
    return [ordered]@{ evidence=$evidenceFile; evidenceRecord=$evidence; evidenceSha256=(Get-FileHash -LiteralPath $evidenceFile -Algorithm SHA256).Hash.ToUpperInvariant(); sourceEvidence=$sourcePath; sourceRuntime=$sourceRuntimeFile; runtime=$runtimeFile; manifest=$ManifestFile; indexHtml=$indexFile }
}
function Wait-Until([scriptblock]$Condition, [int]$TimeoutSeconds) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do { if (& $Condition) { return $true }; Start-Sleep -Milliseconds 100 } while ([DateTimeOffset]::UtcNow -lt $deadline)
    return $false
}
function Get-WmpSnapshot([object]$Player) {
    $snapshot = [ordered]@{ atUtc = [DateTimeOffset]::UtcNow; playState = $null; openState = $null; position = $null; duration = $null; errorCount = $null; errorDescription = $null }
    try { $snapshot.playState = [int]$Player.playState } catch { $snapshot.playState = 'unavailable' }
    try { $snapshot.openState = [int]$Player.openState } catch { $snapshot.openState = 'unavailable' }
    try { $snapshot.position = [double]$Player.controls.currentPosition } catch { $snapshot.position = 'unavailable' }
    try { $snapshot.duration = [double]$Player.currentMedia.duration } catch { $snapshot.duration = 'unavailable' }
    try { $snapshot.errorCount = [int]$Player.Error.errorCount } catch { $snapshot.errorCount = 'unavailable' }
    try { if ($Player.Error.errorCount -gt 0) { $snapshot.errorDescription = [string]$Player.Error.item(0).errorDescription } } catch { $snapshot.errorDescription = 'unavailable' }
    return $snapshot
}
function Get-ControlAvailability([object]$Player) {
    $result = [ordered]@{}
    foreach ($control in 'play', 'pause', 'currentPosition') { try { $result[$control] = [bool]$Player.controls.isAvailable($control) } catch { $result[$control] = $false } }
    return $result
}
function Invoke-RouteObservation([object]$Player, [object]$Route) {
    $events = [Collections.Generic.List[object]]::new()
    $result = [ordered]@{ id = $Route.id; localFile = $Route.file; mime = $Route.mime; videoOnly = $Route.videoOnly; status = 'started'; capabilityAvailability = $null; events = $events; limitations = 'Muted automated local-file control observation only; no audible or perceptual A/V-sync conclusion.' }
    try {
        $Player.URL = $Route.file
        $events.Add([ordered]@{ action = 'url-assigned'; snapshot = Get-WmpSnapshot $Player })
        $opened = Wait-Until { $state = Get-WmpSnapshot $Player; ($state.duration -is [double] -and $state.duration -gt 0) -or $state.playState -eq 3 -or $state.playState -eq 8 -or ($state.errorCount -is [int] -and $state.errorCount -gt 0) } $OpenTimeoutSeconds
        $events.Add([ordered]@{ action = 'open-result'; completed = $opened; snapshot = Get-WmpSnapshot $Player })
        $result.capabilityAvailability = Get-ControlAvailability $Player
        if (-not $opened) { $result.status = 'blocked'; $result.reason = "Timed out waiting $OpenTimeoutSeconds seconds for WMP to open local media."; return $result }
        $open = $events[$events.Count - 1].snapshot
        if ($open.errorCount -is [int] -and $open.errorCount -gt 0) { $result.status = 'blocked'; $result.reason = 'WMP reported a local-media error before playback.'; return $result }
        try { $Player.controls.play() } catch { $result.status = 'blocked'; $result.reason = "WMP play control failed: $($_.Exception.Message)"; return $result }
        $playing = Wait-Until { $state = Get-WmpSnapshot $Player; $state.playState -eq 3 -or $state.playState -eq 8 -or ($state.errorCount -is [int] -and $state.errorCount -gt 0) } $OpenTimeoutSeconds
        $events.Add([ordered]@{ action = 'play-result'; completed = $playing; snapshot = Get-WmpSnapshot $Player })
        if (-not $playing -or $events[$events.Count - 1].snapshot.errorCount -gt 0) { $result.status = 'blocked'; $result.reason = 'WMP did not reach an observable playing or ended state.'; return $result }
        try { $Player.controls.pause() } catch { $result.status = 'observed-failure'; $result.reason = "WMP pause control failed after playback began: $($_.Exception.Message)"; return $result }
        $paused = Wait-Until { $state = Get-WmpSnapshot $Player; $state.playState -eq 2 -or ($state.errorCount -is [int] -and $state.errorCount -gt 0) } 5
        $events.Add([ordered]@{ action = 'pause-result'; completed = $paused; snapshot = Get-WmpSnapshot $Player })
        if (-not $paused -or $events[$events.Count - 1].snapshot.errorCount -gt 0) { $result.status = 'observed-failure'; $result.reason = 'WMP did not enter an observable paused state.'; return $result }
        $duration = $events[$events.Count - 1].snapshot.duration
        $requested = if ($duration -is [double] -and $duration -gt 1) { [Math]::Min($duration - 0.5, $duration / 2) } else { 0.5 }
        try { $Player.controls.currentPosition = $requested } catch { $result.status = 'observed-failure'; $result.reason = "WMP seek control failed: $($_.Exception.Message)"; return $result }
        $seeked = Wait-Until { $state = Get-WmpSnapshot $Player; $state.position -is [double] -and [Math]::Abs($state.position - $requested) -le 1.0 } 5
        $events.Add([ordered]@{ action = 'seek-result'; requestedPosition = $requested; completed = $seeked; snapshot = Get-WmpSnapshot $Player })
        if (-not $seeked) { $result.status = 'observed-failure'; $result.reason = 'WMP did not report the requested seek position within tolerance.'; return $result }
        try { $Player.controls.play() } catch { $result.status = 'observed-failure'; $result.reason = "WMP resume control failed: $($_.Exception.Message)"; return $result }
        $ended = Wait-Until { $state = Get-WmpSnapshot $Player; $state.playState -eq 8 -or ($state.errorCount -is [int] -and $state.errorCount -gt 0) } $EndedTimeoutSeconds
        $events.Add([ordered]@{ action = 'end-result'; completed = $ended; snapshot = Get-WmpSnapshot $Player })
        if (-not $ended -or $events[$events.Count - 1].snapshot.errorCount -gt 0) { $result.status = 'observed-failure'; $result.reason = "WMP did not reach ended state within $EndedTimeoutSeconds seconds after resume."; return $result }
        $result.status = 'passed'; $result.reason = 'WMP local-file open/play/pause/seek/resume/ended observations completed.'; return $result
    } catch { $result.status = 'observed-failure'; $result.reason = $_.Exception.Message; return $result }
}

$output = Require-OutsideRepositoryEmptyDirectory $OutputDirectory
$evidencePath = Join-Path $output 'g0.4-wmp-observation-evidence.json'
$record = [ordered]@{ schemaVersion = 2; kind = 'ReelForge Gate 0 optional Windows Media Player local-file observation'; status = 'started'; profile = 'WMP legacy optional Windows evidence'; timeouts = [ordered]@{ openSeconds=$OpenTimeoutSeconds; endedSeconds=$EndedTimeoutSeconds }; limitations = 'WMP is optional and nonportable. MP4 and WebM observations are local environment evidence only; WebM is capability-qualified. No audible/perceptual A/V-sync conclusion.'; routes = @() }
try {
    $corpus = Assert-RootedNonReparseDirectory $CorpusRoot 'CorpusRoot'
    $manifestFile = Assert-ContainedNonReparseFile $corpus 'manifest.json' 'Playback manifest'
    $manifest = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
    $expected = @(
        [ordered]@{ id='Video.Export.Compatibility.Mp4H264Aac.P2OpenH264'; url='media/h264-aac.mp4'; mime='video/mp4'; videoOnly=$false },
        [ordered]@{ id='Video.Export.Compatibility.Mp4H264VideoOnly.P2OpenH264'; url='media/h264-video-only.mp4'; mime='video/mp4'; videoOnly=$true },
        [ordered]@{ id='Video.Export.Open.WebmVp9Opus'; url='media/vp9-opus.webm'; mime='video/webm'; videoOnly=$false },
        [ordered]@{ id='Video.Export.Open.WebmVp9VideoOnly'; url='media/vp9-video-only.webm'; mime='video/webm'; videoOnly=$true }
    )
    $provenance = Assert-PlaybackCorpusProvenance $corpus $manifest $manifestFile
    $routes = [Collections.Generic.List[object]]::new()
    foreach ($wanted in $expected) {
        $available = @($manifest.routes | Where-Object { $_.id -eq $wanted.id }); $blocked = @($manifest.blockedRoutes | Where-Object { $_.id -eq $wanted.id })
        if (($available.Count + $blocked.Count) -ne 1) { throw "Playback manifest must contain exactly one disposition for approved route: $($wanted.id)" }
        if ($blocked.Count -eq 1) {
            if ([string]::IsNullOrWhiteSpace([string]$blocked[0].reason)) { throw "Playback manifest blocked route lacks a reason: $($wanted.id)" }
            Assert-ArtifactBinding $corpus $blocked[0].transformation "Playback blocked transformation $($wanted.id)" | Out-Null
            $record.routes += [ordered]@{ id=$wanted.id; mime=$wanted.mime; videoOnly=$wanted.videoOnly; status='inherited-blocked-not-executed'; reason=[string]$blocked[0].reason; source='Fresh v2 corpus preparation disposition; WMP did not execute an unavailable route.'; transformation=$blocked[0].transformation; limitations='This is not a WMP player failure.' }
            continue
        }
        $match = $available[0]
        if ($match.url -ne $wanted.url -or $match.mime -ne $wanted.mime -or [bool]$match.videoOnly -ne $wanted.videoOnly) { throw "Playback manifest route does not match approved corpus contract: $($wanted.id)" }
        if ([string]$match.sha256 -notmatch '^[A-F0-9]{64}$' -or [int64]$match.length -le 0) { throw "Playback manifest artifact identity is invalid: $($wanted.id)" }
        $file = Assert-ContainedNonReparseFile $corpus ([string]$match.url) "Playback route $($wanted.id)"
        $item = Get-Item -LiteralPath $file; $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToUpperInvariant()
        if ($item.Length -ne [int64]$match.length -or $hash -ne ([string]$match.sha256).ToUpperInvariant()) { throw "Playback manifest hash or length mismatch: $($wanted.id)" }
        $evidenceRoute = @($provenance.evidenceRecord.routes | Where-Object { $_.id -eq $wanted.id })
        if ($evidenceRoute.Count -ne 1 -or $evidenceRoute[0].status -ne 'passed' -or $evidenceRoute[0].artifact.sha256 -ne $hash) { throw "Corpus preparation route provenance does not bind available route: $($wanted.id)" }
        $routes.Add([ordered]@{ id=$wanted.id; file=$file; mime=$wanted.mime; videoOnly=$wanted.videoOnly; artifact=[ordered]@{ path=$wanted.url; length=$item.Length; sha256=$hash } })
    }
    $record.corpus = [ordered]@{ root=$corpus; manifest=[ordered]@{ path='manifest.json'; sha256=(Get-FileHash -LiteralPath $manifestFile -Algorithm SHA256).Hash.ToUpperInvariant() }; preparationEvidence=[ordered]@{ path='g0.4-playback-corpus-evidence.json'; sha256=$provenance.evidenceSha256 }; sourceEvidence=[ordered]@{ path=$provenance.sourceEvidence; runtimeIdentity=$provenance.sourceRuntime }; runtimeIdentity=$provenance.runtime; indexHtml=$provenance.indexHtml; routeValidation='fresh v2 provenance, exact four approved route dispositions, hashes, and bound-artifact closure validated before WMP use' }
    $record.environment = [ordered]@{ observedAtUtc=[DateTimeOffset]::UtcNow; os=(Get-OsIdentity); powershell=[ordered]@{ version=$PSVersionTable.PSVersion.ToString(); edition=$PSVersionTable.PSEdition; apartmentState=[Threading.Thread]::CurrentThread.ApartmentState.ToString() }; wmpExecutable=(Get-ExecutableIdentity 'C:\Program Files\Windows Media Player\wmplayer.exe'); wmpExecutableX86=(Get-ExecutableIdentity 'C:\Program Files (x86)\Windows Media Player\wmplayer.exe') }
    if (-not $record.environment.os.exactIdentityAvailable) { foreach ($route in $routes) { $record.routes += [ordered]@{ id=$route.id; mime=$route.mime; videoOnly=$route.videoOnly; status='blocked-not-executed'; reason='Exact Windows identity was unavailable; no WMP observation was attempted.'; limitations='This is insufficient environment evidence, not a player failure.' } }; $record.status='blocked'; $record.blockedReason='Exact Windows identity could not be established from Environment.OSVersion and Windows CurrentVersion registry values.'; Write-AtomicJson $evidencePath $record; exit 0 }
    if ([Threading.Thread]::CurrentThread.ApartmentState -ne [Threading.ApartmentState]::STA) { $record.status='blocked'; $record.blockedReason='The observation worker is not STA. Invoke through Windows PowerShell with -STA; no COM probe was attempted.'; Write-AtomicJson $evidencePath $record; exit 0 }
    $player = $null
    try { $player = New-Object -ComObject WMPlayer.OCX } catch { $record.status='blocked'; $record.blockedReason="WMPlayer.OCX could not be created: $($_.Exception.Message)"; Write-AtomicJson $evidencePath $record; exit 0 }
    try {
        try { $player.settings.mute = $true } catch { }
        try { $record.environment.com = [ordered]@{ progId='WMPlayer.OCX'; versionInfo=[string]$player.versionInfo; created=$true; muted=$true } } catch { $record.environment.com = [ordered]@{ progId='WMPlayer.OCX'; created=$true; versionInfo='unavailable'; muted=$true } }
        foreach ($route in $routes) { $record.routes += Invoke-RouteObservation $player $route }
    } finally {
        $cleanup = [ordered]@{ stopAttempted=$false; closeAttempted=$false; released=$false; failures=@() }
        try { $cleanup.stopAttempted=$true; $player.controls.stop() } catch { $cleanup.failures += "stop: $($_.Exception.Message)" }
        try { $cleanup.closeAttempted=$true; $player.close() } catch { $cleanup.failures += "close: $($_.Exception.Message)" }
        try { if ([Runtime.InteropServices.Marshal]::IsComObject($player)) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($player); $cleanup.released=$true } } catch { $cleanup.failures += "release: $($_.Exception.Message)" }
        $record.environment.comCleanup=$cleanup
        $player=$null
    }
    $record.status = if (@($record.routes | Where-Object status -eq 'passed').Count -eq $routes.Count -and $routes.Count -gt 0) { if (@($record.routes | Where-Object status -eq 'inherited-blocked-not-executed').Count -gt 0) { 'completed-with-inherited-blocked-routes' } else { 'completed' } } else { 'completed-with-blocked-or-observed-failure-routes' }
} catch {
    $record.status = 'invalid-input-or-execution-failure'; $record.failure = $_.Exception.Message
    Write-AtomicJson $evidencePath $record
    exit 1
}
Write-AtomicJson $evidencePath $record
