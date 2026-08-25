[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RuntimeRoot,
    [Parameter(Mandatory)][string]$FixtureRoot,
    [Parameter(Mandatory)][string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Require-OutsideRepositoryEmptyDirectory([string]$Path) {
    if (-not [IO.Path]::IsPathRooted($Path)) { throw 'OutputDirectory must be an explicit rooted path outside the repository.' }
    $repository = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path.TrimEnd('\', '/')
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    if ($full.Equals($repository, [StringComparison]::OrdinalIgnoreCase) -or $full.StartsWith("$repository\", [StringComparison]::OrdinalIgnoreCase)) { throw 'OutputDirectory must be outside the repository.' }
    if (Test-Path -LiteralPath $full) {
        if (-not (Test-Path -LiteralPath $full -PathType Container) -or (Get-ChildItem -LiteralPath $full -Force | Select-Object -First 1)) { throw 'OutputDirectory must be new or empty so evidence cannot include stale files.' }
    } else { New-Item -ItemType Directory -Path $full | Out-Null }
    return $full
}

function Require-Tool([string]$Path, [string]$Name, [string]$Root) {
    if (-not [IO.Path]::IsPathRooted($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "$Name must be an existing explicit rooted path." }
    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $resolvedRoot = (Resolve-Path -LiteralPath $Root).Path.TrimEnd('\', '/')
    if (-not $resolved.StartsWith("$resolvedRoot\", [StringComparison]::OrdinalIgnoreCase)) { throw "$Name must resolve beneath RuntimeRoot. PATH fallback is prohibited." }
    return $resolved
}

function Assert-FixtureInventory([string]$Root, [string]$InventoryPath, [string[]]$Consumed) {
    $inventory = Get-Content -LiteralPath $InventoryPath -Raw | ConvertFrom-Json
    $reportPath = Join-Path $Root 'generated-fixture-report.json'
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) { throw 'FixtureRoot must contain generated-fixture-report.json.' }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    $inventoryHash = (Get-FileHash -LiteralPath $InventoryPath -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($report.approvedInventory.sha256 -ne $inventoryHash -or $report.approvedInventory.testOnlyOverride) { throw 'Fixture report does not bind the checked-in approved inventory.' }
    $expected = @{}
    foreach ($entry in @($inventory.files)) { $expected[[string]$entry.path] = $entry }
    $actual = @{}
    foreach ($entry in @(Get-ChildItem -LiteralPath $Root -File -Recurse -Force)) {
        if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Fixture source must not be a reparse point.' }
        $relative = [IO.Path]::GetRelativePath($Root, $entry.FullName).Replace('\', '/')
        if ($relative -ne 'generated-fixture-report.json') { $actual[$relative] = $entry }
    }
    if ($actual.Count -ne $expected.Count -or @($expected.Keys | Where-Object { -not $actual.ContainsKey($_) }).Count -ne 0 -or @($actual.Keys | Where-Object { -not $expected.ContainsKey($_) }).Count -ne 0) { throw 'FixtureRoot file set does not exactly match the checked-in fixture source inventory.' }
    foreach ($path in $expected.Keys) {
        $entry = $expected[$path]; $file = $actual[$path]
        if ($file.Length -ne [long]$entry.length -or (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToUpperInvariant() -ne $entry.sha256) { throw "Fixture source hash/length mismatch against checked-in inventory: $path" }
    }
    foreach ($path in $Consumed) { if (-not $expected.ContainsKey($path)) { throw "Consumed fixture input is not independently inventoried: $path" } }
    return $inventory
}

function To-AssPath([string]$Path) { return $Path.Replace('\', '/').Replace(':', '\:') }
function Invoke-Tool([string]$Tool, [string[]]$Arguments, [string]$Step) {
    $stdout = Join-Path $logs "$Step.stdout.txt"; $stderr = Join-Path $logs "$Step.stderr.txt"
    & $Tool @Arguments 1> $stdout 2> $stderr
    $record = [ordered]@{ step=$Step; executable=$Tool; arguments=$Arguments; exitCode=$LASTEXITCODE; stdout=(Get-Content -LiteralPath $stdout -Raw); stderr=(Get-Content -LiteralPath $stderr -Raw) }
    $commands.Add($record)
    if ($record.exitCode -ne 0) { throw "Step '$Step' failed with exit code $($record.exitCode)." }
    return $record
}
function Render-Ass([string]$Name, [string]$Shaping, [string]$FontsDirectory, [string]$SubtitlePath = $assPath) {
    $outputFile = Join-Path $media "$Name.rgb"; $partial = "$outputFile.partial"
    $fontOption = if ([string]::IsNullOrWhiteSpace($FontsDirectory)) { '' } else { ":fontsdir='$((To-AssPath $FontsDirectory))'" }
    $filter = "[0:v:0]ass=filename='$((To-AssPath $SubtitlePath))'$fontOption`:shaping=$Shaping,format=rgb24[out]"
    $record = Invoke-Tool $ffmpeg @('-hide_banner','-loglevel','verbose','-loop','1','-framerate','25','-f','image2','-c:v','ppm','-i',$backgroundPath,'-filter_complex',$filter,'-map','[out]','-frames:v','1','-c:v','rawvideo','-pix_fmt','rgb24','-f','rawvideo','-y',$partial) "render-$Name"
    if (-not (Test-Path -LiteralPath $partial -PathType Leaf)) { throw "Text render '$Name' did not create rawvideo output." }
    Move-Item -LiteralPath $partial -Destination $outputFile
    return [PSCustomObject]@{ path=$outputFile; command=$record; filter=$filter }
}
function Assert-LoadedOnlyCleanFonts([object]$Record, [string]$CleanFontDirectory) {
    $matches = [regex]::Matches([string]$Record.stderr, "Loading font file '([^']+)'", [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    $loaded = @($matches | ForEach-Object { $_.Groups[1].Value.Replace('/', '\') })
    $expected = @('NotoSans-Regular.ttf','NotoSansArabic-Regular.ttf','NotoSansCJKsc-Regular.otf') | ForEach-Object { (Join-Path $CleanFontDirectory $_).Replace('/', '\') }
    $expectedSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase); foreach ($path in $expected) { [void]$expectedSet.Add([string]$path) }
    $loadedSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase); foreach ($path in $loaded) { [void]$loadedSet.Add([string]$path) }
    $unexpected = [Collections.Generic.List[string]]::new(); foreach ($path in $loaded) { if (-not $expectedSet.Contains([string]$path)) { $unexpected.Add([string]$path) } }
    $missing = [Collections.Generic.List[string]]::new(); foreach ($path in $expected) { if (-not $loadedSet.Contains([string]$path)) { $missing.Add([string]$path) } }
    if ($loaded.Count -lt $expected.Count -or $unexpected.Count -ne 0 -or $missing.Count -ne 0) { throw "Positive proof did not load only the exact clean hash-validated font-copy paths. Loaded: $($loaded -join '|'). Expected: $($expected -join '|')." }
}
function Get-FontSelections([object]$Record) {
    $matches = [regex]::Matches([string]$Record.stderr, 'fontselect:.*?->\s*([^,\r\n]+),\s*\d+,\s*([^\s\r\n]+)', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    return @($matches | ForEach-Object { [PSCustomObject]@{ target=$_.Groups[1].Value.Trim(); file=$_.Groups[2].Value.Trim() } })
}
function Assert-OnlyApprovedFontSelections([object]$Record, [string]$Label) {
    $selections = @(Get-FontSelections $Record)
    if ($selections.Count -lt 3) { throw "$Label did not record all explicit Latin, CJK, and Arabic font selections." }
    $allowed = @('NotoSans-Regular','NotoSansArabic-Regular','NotoSansCJKsc-Regular')
    foreach ($selection in $selections) {
        if ($allowed -notcontains $selection.target -or $selection.file -ne $selection.target) { throw "$Label selected prohibited ambient/system font '$($selection.target)' (file '$($selection.file)')." }
    }
    foreach ($required in $allowed) { if (-not ($selections.target -contains $required)) { throw "$Label did not select required approved font '$required'." } }
    return $selections
}
function Assert-NoApprovedTargets([object]$Record) {
    $approved = @('NotoSans-Regular','NotoSansArabic-Regular','NotoSansCJKsc-Regular')
    $targets = @(Get-FontSelections $Record | ForEach-Object target)
    if (@($targets | Where-Object { $_ -in $approved }).Count -ne 0) { throw 'Empty-fonts/no-fontsdir control selected an approved internal target from the ambient provider; binary origin is not established.' }
    return $targets
}
function Assert-F3LogicalLayoutAndAss([string]$FixtureRoot) {
    $truth = Get-Content -LiteralPath (Join-Path $FixtureRoot 'expected-truths.json') -Raw | ConvertFrom-Json
    $f3Truth = $truth.fixtures.F3
    $logical = Get-Content -LiteralPath (Join-Path $FixtureRoot 'F3\f3-unicode-text.json') -Raw | ConvertFrom-Json
    $layout = Get-Content -LiteralPath (Join-Path $FixtureRoot 'F3\f3-text-layout.json') -Raw | ConvertFrom-Json
    $ass = Get-Content -LiteralPath (Join-Path $FixtureRoot 'F3\f3-unicode-proof.ass') -Raw
    if ($logical.unicodeText -ne $f3Truth.unicodeText -or $logical.titleText -ne $f3Truth.titleText -or $logical.captionText -ne $f3Truth.captionText) { throw 'F3 logical text specification does not match expected truths.' }
    if ($layout.titleText -ne $logical.titleText -or $layout.captionText -ne $logical.captionText) { throw 'F3 layout text does not match the logical text specification.' }
    if ($layout.canvas.width -ne $f3Truth.textProof.canvas.width -or $layout.canvas.height -ne $f3Truth.textProof.canvas.height -or $layout.canvas.safeInsetPixels -ne $f3Truth.textProof.canvas.safeInsetPixels -or $layout.title.anchor -ne 'top-center' -or $layout.title.x -ne 160 -or $layout.title.y -ne 24 -or $layout.caption.anchor -ne 'bottom-center' -or $layout.caption.x -ne 160 -or $layout.caption.y -ne 156 -or $layout.title.expectedLineBands -ne 1 -or $layout.caption.expectedLineBands -ne 2) { throw 'F3 layout specification does not match the approved geometry/wrapping truth.' }
    $expectedRuns = @(
        [PSCustomObject]@{ family='Noto Sans'; role='latin-punctuation-diacritics' },
        [PSCustomObject]@{ family='Noto Sans CJK SC'; role='simplified-chinese' },
        [PSCustomObject]@{ family='Noto Sans'; role='latin-punctuation' },
        [PSCustomObject]@{ family='Noto Sans Arabic'; role='arabic' }
    )
    $runs = @($layout.textRuns)
    if ($runs.Count -ne $expectedRuns.Count -or ($runs.text -join '') -ne $layout.titleText) { throw 'F3 ordered text runs do not reconstruct the approved title text.' }
    for ($index = 0; $index -lt $expectedRuns.Count; $index++) {
        if ($runs[$index].family -ne $expectedRuns[$index].family -or $runs[$index].role -ne $expectedRuns[$index].role -or [string]::IsNullOrEmpty($runs[$index].text)) { throw 'F3 ordered text-run face/role mapping is not the approved mapping.' }
    }
    $titlePayload = ($runs | ForEach-Object { "{\fn$($_.family)}$($_.text)" }) -join ''
    $cjkOffset = $layout.captionText.IndexOf([string]$runs[1].text, [StringComparison]::Ordinal)
    if ($cjkOffset -lt 0 -or -not $layout.captionText.EndsWith("$($runs[1].text)$($runs[2].text)$($runs[3].text)", [StringComparison]::Ordinal)) { throw 'F3 caption text does not contain the approved CJK/Latin/Arabic fallback sequence.' }
    $captionPrefix = $layout.captionText.Substring(0, $cjkOffset)
    $captionPayload = "{\fn$($runs[0].family)}$captionPrefix{\fn$($runs[1].family)}$($runs[1].text){\fn$($runs[2].family)}$($runs[2].text){\fn$($runs[3].family)}$($runs[3].text)"
    $titleDialogue = "Dialogue: 0,0:00:00.00,0:00:01.00,Title,,0,0,0,,{\an8\pos($($layout.title.x),$($layout.title.y))\q0}$titlePayload"
    $captionDialogue = "Dialogue: 0,0:00:00.00,0:00:01.00,Caption,,0,0,0,,{\an2\pos($($layout.caption.x),$($layout.caption.y))\q0}$captionPayload"
    foreach ($line in @('WrapStyle: 0','Style: Title,Noto Sans,24','Style: Caption,Noto Sans,18',$titleDialogue,$captionDialogue)) { if ($ass -notlike "*$line*") { throw 'F3 ASS does not exactly implement the parsed logical text, ordered face runs, placement, or wrapping policy.' } }
    return [PSCustomObject]@{ title=$logical.titleText; caption=$logical.captionText; titleAnchor=$layout.title.anchor; captionAnchor=$layout.caption.anchor }
}
function Get-ArtifactBindings([string]$Root) {
    return @(Get-ChildItem -LiteralPath $Root -File -Recurse -Force | Where-Object { $_.Name -ne 'text-proof-evidence.json' } | ForEach-Object {
        if (($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Text proof artifact must not be a reparse point.' }
        [ordered]@{ path=[IO.Path]::GetRelativePath($Root, $_.FullName).Replace('\','/'); length=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToUpperInvariant() }
    })
}
function Get-LineBands([string]$RawPath) {
    $bytes = [IO.File]::ReadAllBytes($RawPath); if ($bytes.Length -ne 320 * 180 * 3) { throw 'Rendered text rawvideo geometry is not 320x180 rgb24.' }
    $bands = [Collections.Generic.List[object]]::new(); $open = -1
    for ($y = 0; $y -lt 180; $y++) {
        $changed = $false
        for ($x = 0; $x -lt 320; $x++) {
            $i = ($y * 320 + $x) * 3
            if ($bytes[$i] -gt 100 -or $bytes[$i + 1] -gt 100 -or $bytes[$i + 2] -gt 100) { $changed = $true; break }
        }
        if ($changed -and $open -lt 0) { $open = $y }
        if (-not $changed -and $open -ge 0) { $bands.Add([PSCustomObject]@{ start=$open; end=$y - 1 }); $open = -1 }
    }
    if ($open -ge 0) { $bands.Add([PSCustomObject]@{ start=$open; end=179 }) }
    return @($bands)
}

$output = Require-OutsideRepositoryEmptyDirectory $OutputDirectory
if (-not [IO.Path]::IsPathRooted($RuntimeRoot) -or -not (Test-Path -LiteralPath $RuntimeRoot -PathType Container)) { throw 'RuntimeRoot must be an existing explicit rooted directory.' }
if (-not [IO.Path]::IsPathRooted($FixtureRoot) -or -not (Test-Path -LiteralPath $FixtureRoot -PathType Container)) { throw 'FixtureRoot must be an existing explicit rooted directory from Generate-Fixtures.ps1.' }
$runtime = (Resolve-Path -LiteralPath $RuntimeRoot).Path; $fixtures = (Resolve-Path -LiteralPath $FixtureRoot).Path
$ffmpeg = Require-Tool (Join-Path $runtime 'bin\ffmpeg.exe') 'ffmpeg.exe' $runtime
$work = Join-Path $output 'work'; $logs = Join-Path $work 'logs'; $media = Join-Path $output 'media'; New-Item -ItemType Directory -Path $work, $logs, $media | Out-Null
$commands = [Collections.Generic.List[object]]::new()

$identityPath = Join-Path $output 'runtime-identity.json'; & (Join-Path $PSScriptRoot 'Validate-P2Runtime.ps1') -RuntimeRoot $runtime -EvidencePath $identityPath
if ($LASTEXITCODE -ne 0) { throw 'Approved paired runtime identity validation failed; no text proof was run.' }
$fontManifestPath = Join-Path $PSScriptRoot 'font-proof-artifacts.json'; $fontRoot = Join-Path $PSScriptRoot 'artifacts\fonts'
$fontValidation = & (Join-Path $PSScriptRoot 'Validate-FontProofArtifacts.ps1') -ArtifactRoot $fontRoot -ManifestPath $fontManifestPath | ConvertFrom-Json
if ($fontValidation.status -ne 'validated') { throw 'Checked-in font manifest did not validate.' }
$inventoryPath = Join-Path $PSScriptRoot 'fixture-source-inventory.json'
$inventory = Assert-FixtureInventory $fixtures $inventoryPath @('F3/f3-text-background.ppm','F3/f3-unicode-text.json','F3/f3-text-layout.json','F3/f3-unicode-proof.ass','F3/f3-arabic-shaping-oracle.ass')
$backgroundPath = Join-Path $fixtures 'F3\f3-text-background.ppm'; $assPath = Join-Path $fixtures 'F3\f3-unicode-proof.ass'; $arabicAssPath = Join-Path $fixtures 'F3\f3-arabic-shaping-oracle.ass'
$contract = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'semantic-proof-contract.json') -Raw | ConvertFrom-Json
$textContract = @($contract.capabilities | Where-Object id -eq 'Text.Render.UnicodeTitlesAndCaptions')[0]
if ($null -eq $textContract -or $textContract.status -ne 'approved-for-executable-proof') { throw 'Text capability must have the owner-approved executable-proof contract state.' }

try {
    $logicalLayout = Assert-F3LogicalLayoutAndAss $fixtures
    $cleanFonts = Join-Path $work 'approved-fonts'; New-Item -ItemType Directory -Path $cleanFonts | Out-Null
    foreach ($font in @($fontValidation.filesValidated | Where-Object { $_ -notlike 'licenses/*' -and $_ -ne 'README.md' })) {
        $source = Join-Path $fontRoot $font.Replace('/', '\\'); $destination = Join-Path $cleanFonts ([IO.Path]::GetFileName($source)); Copy-Item -LiteralPath $source -Destination $destination
        if ((Get-Item -LiteralPath $source).Length -ne (Get-Item -LiteralPath $destination).Length -or (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash) { throw 'Clean approved-font copy does not preserve manifest-validated bytes.' }
    }
    $complexA = Render-Ass 'unicode-complex-a' 'complex' $cleanFonts
    $complexB = Render-Ass 'unicode-complex-b' 'complex' $cleanFonts
    Assert-LoadedOnlyCleanFonts $complexA.command $cleanFonts; Assert-LoadedOnlyCleanFonts $complexB.command $cleanFonts
    $selections = Assert-OnlyApprovedFontSelections $complexA.command 'Positive complex render'
    [void](Assert-OnlyApprovedFontSelections $complexB.command 'Positive repeated complex render')
    $complexHashA = (Get-FileHash -LiteralPath $complexA.path -Algorithm SHA256).Hash.ToUpperInvariant(); $complexHashB = (Get-FileHash -LiteralPath $complexB.path -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($complexHashA -ne $complexHashB) { throw 'Repeated complex Unicode render SHA-256 values differ.' }
    $arabicComplex = Render-Ass 'arabic-complex' 'complex' $cleanFonts $arabicAssPath
    $arabicSimple = Render-Ass 'arabic-simple' 'simple' $cleanFonts $arabicAssPath
    Assert-LoadedOnlyCleanFonts $arabicComplex.command $cleanFonts; Assert-LoadedOnlyCleanFonts $arabicSimple.command $cleanFonts
    foreach ($record in @($arabicComplex.command, $arabicSimple.command)) { $arabicSelections = @(Get-FontSelections $record); if ($arabicSelections.Count -lt 1 -or @($arabicSelections | Where-Object { $_.target -ne 'NotoSansArabic-Regular' }).Count -ne 0) { throw 'Arabic shaping control did not select only the approved Arabic target.' } }
    $simpleHash = (Get-FileHash -LiteralPath $arabicSimple.path -Algorithm SHA256).Hash.ToUpperInvariant(); $arabicComplexHash = (Get-FileHash -LiteralPath $arabicComplex.path -Algorithm SHA256).Hash.ToUpperInvariant()
    $complexBytes = [IO.File]::ReadAllBytes($arabicComplex.path); $simpleBytes = [IO.File]::ReadAllBytes($arabicSimple.path); $insideDifferent=$false; $outsideDifferent=$false
    for ($y=0; $y -lt 180; $y++) { for ($x=0; $x -lt 320; $x++) { $i=($y*320+$x)*3; $different=$complexBytes[$i] -ne $simpleBytes[$i] -or $complexBytes[$i+1] -ne $simpleBytes[$i+1] -or $complexBytes[$i+2] -ne $simpleBytes[$i+2]; if ($different) { if ($x -ge 80 -and $x -lt 240 -and $y -ge 60 -and $y -lt 120) { $insideDifferent=$true } else { $outsideDifferent=$true } } } }
    if (-not $insideDifferent -or $outsideDifferent) { throw 'Arabic simple-versus-complex shaping difference was not localized to the approved Arabic bounding region.' }
    $bands = @(Get-LineBands $complexA.path)
    $unsafeBands = @($bands | Where-Object { $_.start -lt 18 -or $_.end -gt 161 })
    if ($bands.Count -ne 3 -or $bands[0].end -ge 70 -or $bands[1].start -le 70 -or $bands[2].start -le 70 -or $unsafeBands.Count -ne 0) { throw 'Text layout oracle did not produce one top-safe title band and two bottom-safe caption wrapping bands.' }
    $negativeFonts = Join-Path $work 'fonts-missing-cjk'; New-Item -ItemType Directory -Path $negativeFonts | Out-Null
    Copy-Item -LiteralPath (Join-Path $fontRoot 'NotoSans-Regular.ttf') -Destination $negativeFonts
    Copy-Item -LiteralPath (Join-Path $fontRoot 'NotoSansArabic-Regular.ttf') -Destination $negativeFonts
    $negativeRejected = $false; $negativeMessage = $null
    try { $negative = Render-Ass 'unicode-negative-missing-cjk' 'complex' $negativeFonts; [void](Assert-OnlyApprovedFontSelections $negative.command 'Negative missing-CJK control') } catch { $negativeRejected = $true; $negativeMessage = $_.Exception.Message }
    if (-not $negativeRejected) { throw 'Negative missing-CJK control did not reject ambient fallback.' }
    $ambient = Render-Ass 'unicode-empty-fonts-control' 'complex' $null
    $ambientTargets = Assert-NoApprovedTargets $ambient.command
    $golden = $textContract.oracle.complexRenderSha256
    if ([string]::IsNullOrWhiteSpace($golden) -or $golden -notmatch '^[0-9A-F]{64}$') { throw "Text proof needs a reviewed predeclared complex render SHA-256; observed $complexHashA." }
    if ($golden -ne $complexHashA) { throw "Complex Unicode render does not match reviewed golden SHA-256. Expected $golden, got $complexHashA." }
    $evidence = [ordered]@{ schemaVersion=1; profileId='P2.BtbnLgplShared.WindowsX64.20260820'; capabilityId='Text.Render.UnicodeTitlesAndCaptions'; status='passed'; providerLimitation='DirectWrite automatic fallback is not accepted for proof because it selected ambient YuGothicUI/Arial despite fontsdir. This is controlled explicit ASS run mapping with same-provider binary-origin attestation, not an app-level resolver.'; fontManifestValidation=$fontValidation; fixtureInventoryPath='eng/gate0/fixture-source-inventory.json'; approvedFontTargets=@('NotoSans-Regular','NotoSansArabic-Regular','NotoSansCJKsc-Regular'); componentSelection=@{inputDemuxer='image2';inputDecoder='ppm';inputStreamSelector='0:v:0';filter='ass';shaping='complex';outputEncoder='rawvideo';outputMuxer='rawvideo';fontFiles=@('NotoSans-Regular.ttf','NotoSansArabic-Regular.ttf','NotoSansCJKsc-Regular.otf');fontsdir='clean manifest-only explicit paths'}; commands=$commands; logicalLayout=$logicalLayout; positive=@{ complexRenderSha256=$complexHashA; arabicSimpleRenderSha256=$simpleHash; arabicComplexRenderSha256=$arabicComplexHash; selections=$selections; lineBands=$bands; titleRegion='top-safe';captionRegion='bottom-safe';arabicShaping='ass shaping=complex with simple-vs-complex localized region oracle'; arabicOracleRegion=@{ x=80; y=60; width=160; height=60; differenceInside=$true; differenceOutside=$false }; cleanFontCopyDirectory='work/approved-fonts' }; negativeMissingCjkControl=@{ rejected=$negativeRejected; message=$negativeMessage }; emptyFontsNoFontsdirControl=@{ approvedTargetsAbsent=$true; observedTargets=$ambientTargets }; colorEmoji='optional-blocked-not-rendered'; artifacts=(Get-ArtifactBindings $output) }
    [IO.File]::WriteAllText((Join-Path $output 'text-proof-evidence.json'), ($evidence | ConvertTo-Json -Depth 16), [Text.UTF8Encoding]::new($false))
} catch {
    $failure = [ordered]@{ schemaVersion=1; profileId='P2.BtbnLgplShared.WindowsX64.20260820'; capabilityId='Text.Render.UnicodeTitlesAndCaptions'; status='failed'; error=$_.Exception.Message; commands=$commands }
    [IO.File]::WriteAllText((Join-Path $output 'text-proof-evidence.json'), ($failure | ConvertTo-Json -Depth 16), [Text.UTF8Encoding]::new($false))
    throw
}
