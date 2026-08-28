[CmdletBinding()]
param([string] $RuntimeRoot, [switch] $Live)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$profilePath = Join-Path $PSScriptRoot 'baseline-profile.json'
$profile = Get-Content -LiteralPath $profilePath -Raw | ConvertFrom-Json -Depth 32
if ($profile.schemaVersion -ne 1 -or $profile.status -ne 'development-baseline-candidate-not-shipping' -or $profile.sourceProfile.licensePath -ne 'LGPLv3-path' -or [string]::IsNullOrWhiteSpace($profile.sourceProfile.configuration)) { throw 'Media runtime baseline profile identity is invalid.' }
foreach ($token in @('--enable-gpl','--enable-nonfree','libx264','libx265','libvidstab','librubberband','eq','hqdn3d')) { if ($token -notin @($profile.configurationPolicy.forbidden) + @($profile.configurationPolicy.forbiddenComponents)) { throw "Baseline policy does not forbid $token." } }
foreach ($font in @($profile.fonts)) {
  $fontPath = Join-Path $PSScriptRoot ([string]$font.relativePath).Replace('/','\')
  if (-not (Test-Path -LiteralPath $fontPath -PathType Leaf) -or (Get-FileHash -LiteralPath $fontPath -Algorithm SHA256).Hash -ne [string]$font.sha256) { throw "Pinned baseline font is absent or hash-drifted: $([IO.Path]::GetFileName($fontPath))." }
}
$result = [ordered]@{ profileId=$profile.profileId; status='static-policy-valid'; live=$false; networkAccess=$false; credentialsAccess=$false; shippingConclusion=$false }
if (-not $Live) { [pscustomobject]$result; return }
if ([string]::IsNullOrWhiteSpace($RuntimeRoot)) { throw 'Live validation requires RuntimeRoot.' }
$root = [IO.Path]::GetFullPath($RuntimeRoot)
$ffmpeg = Join-Path $root $profile.sourceProfile.ffmpeg.relativePath; $ffprobe = Join-Path $root $profile.sourceProfile.ffprobe.relativePath
foreach ($tool in @($ffmpeg,$ffprobe)) { if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) { throw 'Runtime pair is incomplete.' } }
if ((Get-FileHash -LiteralPath $ffmpeg -Algorithm SHA256).Hash -ne $profile.sourceProfile.ffmpeg.sha256 -or (Get-FileHash -LiteralPath $ffprobe -Algorithm SHA256).Hash -ne $profile.sourceProfile.ffprobe.sha256) { throw 'Runtime tool hash differs from the baseline candidate.' }
$ffmpegVersion = (& $ffmpeg -hide_banner -version 2>&1 | Out-String)
$ffprobeVersion = (& $ffprobe -hide_banner -version 2>&1 | Out-String)
foreach ($version in @($ffmpegVersion,$ffprobeVersion)) {
  if ($version -notmatch [regex]::Escape($profile.sourceProfile.version) -or $version -notmatch [regex]::Escape($profile.sourceProfile.configuration)) { throw 'Runtime tool version/configuration identity differs from the baseline candidate.' }
}
foreach ($forbidden in @($profile.configurationPolicy.forbidden)) { if ($ffmpegVersion -match [regex]::Escape($forbidden)) { throw "Runtime enables forbidden configuration token $forbidden." } }
foreach ($required in @($profile.configurationPolicy.required)) { if ($ffmpegVersion -notmatch [regex]::Escape($required)) { throw "Runtime lacks required configuration token $required." } }
foreach ($kind in @('encoder','decoder','muxer','demuxer','filter','protocol')) {
  $listingArgument = "-$($kind)s"
  $listing = (& $ffmpeg -hide_banner $listingArgument 2>&1 | Out-String)
  foreach ($component in @($profile.requiredComponents.$kind)) { if ($listing -notmatch "(?m)\s$([regex]::Escape([string]$component))(\s|$)") { throw "Runtime lacks required $kind $component." } }
  $prohibitedProperty = $profile.prohibitedComponents.PSObject.Properties[$kind]
  foreach ($component in @(if ($null -eq $prohibitedProperty) { @() } else { @($prohibitedProperty.Value) })) { if ($listing -match "(?m)\s$([regex]::Escape([string]$component))(\s|$)") { throw "Runtime exposes prohibited $kind $component." } }
}
$result.status='live-runtime-valid'; $result.live=$true; [pscustomobject]$result
