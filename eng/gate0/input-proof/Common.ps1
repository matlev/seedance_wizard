Set-StrictMode -Version Latest

# Shared G0.4 common-input proof safeguards.  This is proof infrastructure,
# never an application import, persistence, rendering, or shipping-runtime API.

function Get-G04NormalizedFullPath {
    param([Parameter(Mandatory)][string]$Path)

    if (-not [IO.Path]::IsPathRooted($Path)) {
        throw 'Path must be an explicit rooted path.'
    }

    return [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
}

function Test-G04PathWithinRoot {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Root)

    return $Path.Equals($Root, [StringComparison]::OrdinalIgnoreCase) -or
        $Path.StartsWith("$Root$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase)
}

function Assert-G04NoReparseAncestors {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Name)

    $current = $Path
    while ($true) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "$Name or an ancestor is a reparse-point: $current"
            }
        }

        $parent = [IO.Path]::GetDirectoryName($current)
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent.Equals($current, [StringComparison]::OrdinalIgnoreCase)) {
            return
        }
        $current = $parent
    }
}

function Assert-G04NewOutsideRepositoryDirectory {
    param([Parameter(Mandatory)][string]$Path)

    $full = Get-G04NormalizedFullPath $Path
    $repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path.TrimEnd('\', '/')
    if (Test-G04PathWithinRoot $full $repository) {
        throw 'OutputDirectory must be outside the repository.'
    }

    $ancestor = $full
    while (-not (Test-Path -LiteralPath $ancestor)) {
        $parent = [IO.Path]::GetDirectoryName($ancestor)
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent.Equals($ancestor, [StringComparison]::OrdinalIgnoreCase)) {
            throw "OutputDirectory has no resolvable existing ancestor: $full"
        }
        $ancestor = $parent
    }
    Assert-G04NoReparseAncestors $ancestor 'OutputDirectory'

    if (Test-Path -LiteralPath $full) {
        if (-not (Test-Path -LiteralPath $full -PathType Container) -or (Get-ChildItem -LiteralPath $full -Force | Select-Object -First 1)) {
            throw 'OutputDirectory must be new or empty so evidence cannot include stale files.'
        }
    }
    else {
        New-Item -ItemType Directory -Path $full | Out-Null
    }

    Assert-G04NoReparseAncestors $full 'OutputDirectory'
    return (Resolve-Path -LiteralPath $full).Path
}

function Assert-G04RootedNonReparseDirectory {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Name)

    if (-not [IO.Path]::IsPathRooted($Path) -or -not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "$Name must be an existing explicit rooted directory."
    }

    $resolved = (Resolve-Path -LiteralPath $Path).Path.TrimEnd('\', '/')
    Assert-G04NoReparseAncestors $resolved $Name
    return $resolved
}

function Resolve-G04RuntimeTool {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Root
    )

    $runtimeRoot = Assert-G04RootedNonReparseDirectory $Root 'RuntimeRoot'
    if (-not [IO.Path]::IsPathRooted($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Name must be an existing explicit rooted path. PATH fallback is prohibited."
    }

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    if (-not (Test-G04PathWithinRoot $resolved $runtimeRoot) -or $resolved.Equals($runtimeRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Name must resolve beneath RuntimeRoot. PATH fallback is prohibited."
    }
    $item = Get-Item -LiteralPath $resolved -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Name must not be a reparse-point. PATH fallback is prohibited."
    }
    return $resolved
}

function Get-G04PropertyValue {
    param([object]$Object, [string]$Name)
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Assert-G04UniqueNonEmptyIds {
    param([object[]]$Items, [string]$Label)
    $ids = @{}
    foreach ($item in $Items) {
        $id = [string](Get-G04PropertyValue $item 'id')
        if ([string]::IsNullOrWhiteSpace($id) -or $ids.ContainsKey($id)) {
            throw "$Label identifiers must be unique and non-empty."
        }
        $ids[$id] = $item
    }
    return $ids
}

function Read-G04InputContract {
    param([Parameter(Mandatory)][string]$ContractPath)

    if (-not [IO.Path]::IsPathRooted($ContractPath) -or -not (Test-Path -LiteralPath $ContractPath -PathType Leaf)) {
        throw 'G0.4 input proof contract must be an existing explicit rooted file.'
    }
    try { $contract = Get-Content -LiteralPath $ContractPath -Raw | ConvertFrom-Json -Depth 100 } catch { throw "G0.4 input proof contract is invalid JSON: $($_.Exception.Message)" }

    if ([string](Get-G04PropertyValue $contract 'profileId') -ne 'P2.BtbnLgplShared.WindowsX64.20260820') {
        throw 'Input proof contract must name the exact approved P2 profile.'
    }

    $expectedCounts = [ordered]@{ guaranteedCases = 256; fixtureRecipes = 163; oracleProfiles = 24; selectionCases = 7; classificationCases = 8 }
    foreach ($entry in $expectedCounts.GetEnumerator()) {
        $items = @(Get-G04PropertyValue $contract $entry.Key)
        if ($items.Count -ne $entry.Value) { throw "Input proof contract must contain exactly $($entry.Value) $($entry.Key); observed $($items.Count)." }
    }

    $recipes = Assert-G04UniqueNonEmptyIds @($contract.fixtureRecipes) 'Fixture recipe'
    $oracles = Assert-G04UniqueNonEmptyIds @($contract.oracleProfiles) 'Oracle profile'
    $cases = Assert-G04UniqueNonEmptyIds @($contract.guaranteedCases) 'Guaranteed case'
    $null = Assert-G04UniqueNonEmptyIds @($contract.selectionCases) 'Selection case'
    $null = Assert-G04UniqueNonEmptyIds @($contract.classificationCases) 'Classification case'

    foreach ($case in @($contract.guaranteedCases)) {
        if ([string](Get-G04PropertyValue $case 'expectedVerdict') -ne 'guaranteed-common') { throw "Guaranteed case $($case.id) is not explicitly guaranteed-common." }
        $production = Get-G04PropertyValue $case 'fixtureProduction'
        $recipeId = [string](Get-G04PropertyValue $production 'recipeId')
        if (-not $recipes.ContainsKey($recipeId)) { throw "Guaranteed case $($case.id) references unknown fixture recipe $recipeId." }
    }
    foreach ($recipe in @($contract.fixtureRecipes)) {
        $oracleId = [string](Get-G04PropertyValue $recipe 'oracleProfileId')
        if (-not $oracles.ContainsKey($oracleId)) { throw "Fixture recipe $($recipe.id) references unknown oracle profile $oracleId." }
    }
    foreach ($case in @($contract.selectionCases) + @($contract.classificationCases)) {
        $recipeId = [string](Get-G04PropertyValue $case 'fixtureRecipeId')
        $oracleId = [string](Get-G04PropertyValue $case 'oracleProfileId')
        if (-not $recipes.ContainsKey($recipeId)) { throw "Selection or classification case $($case.id) references unknown fixture recipe $recipeId." }
        if (-not $oracles.ContainsKey($oracleId)) { throw "Selection or classification case $($case.id) references unknown oracle profile $oracleId." }
    }

    $authority = Get-G04PropertyValue $contract 'fixtureRecipeAuthority'
    if ([string](Get-G04PropertyValue $authority 'sourcePrimitiveInventory') -ne 'eng/gate0/fixture-source-inventory.json') {
        throw 'Input proof contract must retain the approved fixture source inventory authority.'
    }

    $inventoryPath = Join-Path (Split-Path -Parent $ContractPath) 'fixture-source-inventory.json'
    try { $inventory = Get-Content -LiteralPath $inventoryPath -Raw | ConvertFrom-Json -Depth 100 } catch { throw 'Input proof contract fixture source inventory is unavailable or invalid JSON.' }
    $inventoryPaths = @{}
    foreach ($file in @($inventory.files)) { $inventoryPaths[(Get-G04SafeRelativePath ([string]$file.path) 'Fixture source inventory path')] = $true }
    foreach ($recipe in @($contract.fixtureRecipes)) {
        foreach ($artifact in @(Get-G04PropertyValue $recipe 'sourceArtifacts')) {
            foreach ($fileId in @(Get-G04PropertyValue $artifact 'fileIds')) {
                if ([string]$fileId -ne 'sourceCaseId' -and -not $inventoryPaths.ContainsKey([string]$fileId)) { throw "Fixture recipe $($recipe.id) references unknown source artifact $fileId." }
            }
        }
        $sourceCaseId = [string](Get-G04PropertyValue $recipe 'sourceCaseId')
        if (-not [string]::IsNullOrWhiteSpace($sourceCaseId) -and -not $cases.ContainsKey($sourceCaseId)) { throw "Fixture recipe $($recipe.id) references unknown source case $sourceCaseId." }
    }
    return $contract
}

function Get-G04SafeRelativePath {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Description)
    if ([string]::IsNullOrWhiteSpace($Path) -or [IO.Path]::IsPathRooted($Path)) { throw "$Description contains an unsafe path." }
    $normal = $Path.Replace('\', '/')
    if (@($normal.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count -ne 0) { throw "$Description contains an unsafe path." }
    return $normal
}

function Test-G04FixtureClosure {
    param([Parameter(Mandatory)][string]$FixtureRoot, [Parameter(Mandatory)][string]$InventoryPath)

    $root = Assert-G04RootedNonReparseDirectory $FixtureRoot 'FixtureRoot'
    if (-not [IO.Path]::IsPathRooted($InventoryPath) -or -not (Test-Path -LiteralPath $InventoryPath -PathType Leaf)) { throw 'Fixture inventory must be an existing explicit rooted file.' }
    $inventoryItem = Get-Item -LiteralPath $InventoryPath -Force
    if (($inventoryItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Fixture inventory must not be a reparse-point.' }
    $reportPath = Join-Path $root 'generated-fixture-report.json'
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) { throw 'Fixture report and checked-in fixture source inventory are required.' }

    try { $inventory = Get-Content -LiteralPath $InventoryPath -Raw | ConvertFrom-Json -Depth 100; $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json -Depth 100 } catch { throw 'Fixture report or inventory is truncated or invalid JSON.' }
    $inventoryHash = (Get-FileHash -LiteralPath $InventoryPath -Algorithm SHA256).Hash.ToUpperInvariant()
    if ([string]$report.profileId -ne 'P2.BtbnLgplShared.WindowsX64.20260820' -or $report.externalMediaCommandsExecuted -or [string]$report.approvedInventory.path -ne 'eng/gate0/fixture-source-inventory.json' -or [string]$report.approvedInventory.sha256 -ne $inventoryHash) {
        throw 'Fixture report approved inventory does not match the checked-in fixture source inventory.'
    }

    $expected = @{}; $reported = @{}; $actual = @{}
    foreach ($entry in @($inventory.files)) {
        $path = Get-G04SafeRelativePath ([string]$entry.path) 'Inventory path'
        if ($expected.ContainsKey($path) -or [int64]$entry.length -lt 0 -or ([string]$entry.sha256 -notmatch '^[A-Fa-f0-9]{64}$')) { throw "Fixture inventory entry is invalid: $path" }
        $expected[$path] = $entry
    }
    foreach ($entry in @($report.sourceFiles)) {
        $path = Get-G04SafeRelativePath ([string]$entry.path) 'Fixture report path'
        if ($reported.ContainsKey($path) -or [int64]$entry.length -lt 0 -or ([string]$entry.sha256 -notmatch '^[A-Fa-f0-9]{64}$')) { throw "Fixture report entry is invalid: $path" }
        $reported[$path] = $entry
    }
    foreach ($item in @(Get-ChildItem -LiteralPath $root -Force -Recurse)) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Fixture path contains a reparse-point: $($item.FullName)" }
        if (-not $item.PSIsContainer) {
            $relative = Get-G04SafeRelativePath ([IO.Path]::GetRelativePath($root, $item.FullName)) 'Fixture actual path'
            if ($relative -ne 'generated-fixture-report.json') { $actual[$relative] = [ordered]@{ length = [int64]$item.Length; sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToUpperInvariant() } }
        }
    }
    $allPaths = @($expected.Keys + $reported.Keys + $actual.Keys | Select-Object -Unique)
    foreach ($path in $allPaths) {
        if (-not $expected.ContainsKey($path) -or -not $reported.ContainsKey($path) -or -not $actual.ContainsKey($path)) { throw "Fixture report/inventory/actual-root file set differs: $path" }
        foreach ($candidate in @($reported[$path], $actual[$path])) {
            if ([int64]$candidate.length -ne [int64]$expected[$path].length -or ([string]$candidate.sha256).ToUpperInvariant() -ne ([string]$expected[$path].sha256).ToUpperInvariant()) { throw "Fixture report hash or length mismatch: $path" }
        }
    }

    return [ordered]@{
        fixtureRoot = $root
        reportPath = $reportPath
        reportSha256 = (Get-FileHash -LiteralPath $reportPath -Algorithm SHA256).Hash.ToUpperInvariant()
        inventoryPath = (Resolve-Path -LiteralPath $InventoryPath).Path
        inventorySha256 = $inventoryHash
        report = $report
        fileMap = $actual
    }
}

function Invoke-G04RecordedCommand {
    param(
        [Parameter(Mandatory)][object]$Context,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][object]$Components,
        [bool]$AllowFailure = $false
    )

    # Do not use Get-G04PropertyValue here: PowerShell pipeline enumeration turns
    # an empty List[object] into no output, even though it is a valid command log.
    # Both runner hashtables and PSCustomObject facades are approved context forms.
    $contextIsDictionary = $Context -is [Collections.IDictionary]
    foreach ($property in @('Output', 'Work', 'Logs', 'Commands')) {
        if ($contextIsDictionary) {
            if (-not $Context.Contains($property) -or $null -eq [object]$Context[$property]) { throw "Recorded-command context is missing $property." }
        } else {
            $contextProperty = $Context.PSObject.Properties[$property]
            if ($null -eq $contextProperty -or $null -eq [object]$contextProperty.Value) { throw "Recorded-command context is missing $property." }
        }
    }
    if (-not [IO.Path]::IsPathRooted($Executable) -or -not (Test-Path -LiteralPath $Executable -PathType Leaf)) { throw 'Command executable must be an existing explicit rooted path. PATH fallback is prohibited.' }
    $logs = Assert-G04RootedNonReparseDirectory ([string]$Context.Logs) 'Command log directory'
    $safeName = $Name -replace '[^A-Za-z0-9._-]', '_'
    $stdoutPath = Join-Path $logs "$safeName.stdout.txt"; $stderrPath = Join-Path $logs "$safeName.stderr.txt"
    & $Executable @Arguments 1>$stdoutPath 2>$stderrPath
    $exitCode = $LASTEXITCODE
    $record = [ordered]@{ name = $Name; executable = (Resolve-Path -LiteralPath $Executable).Path; arguments = @($Arguments); components = $Components; exitCode = $exitCode; stdoutPath = $stdoutPath; stderrPath = $stderrPath; stdout = (Get-Content -LiteralPath $stdoutPath -Raw); stderr = (Get-Content -LiteralPath $stderrPath -Raw) }
    if ($contextIsDictionary) { $Context['Commands'].Add($record) }
    else { $Context.PSObject.Properties['Commands'].Value.Add($record) }
    if ($exitCode -ne 0 -and -not $AllowFailure) { throw "Command '$Name' failed with exit code $exitCode." }
    return $record
}

function Get-G04ArtifactRecord {
    param([Parameter(Mandatory)][object]$Context, [Parameter(Mandatory)][string]$Path)
    $output = Assert-G04RootedNonReparseDirectory ([string]$Context.Output) 'Output directory'
    if (-not [IO.Path]::IsPathRooted($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw 'Artifact must be an existing explicit rooted file.' }
    $full = (Resolve-Path -LiteralPath $Path).Path
    if (-not (Test-G04PathWithinRoot $full $output) -or $full.Equals($output, [StringComparison]::OrdinalIgnoreCase)) { throw 'Artifact must be contained beneath Output directory.' }
    $item = Get-Item -LiteralPath $full -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Artifact must not be a reparse-point.' }
    return [ordered]@{ path = [IO.Path]::GetRelativePath($output, $full).Replace('\', '/'); length = [int64]$item.Length; sha256 = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToUpperInvariant() }
}

function Test-G04UndeclaredDiagnostics {
    param([AllowEmptyString()][string]$Stderr, [string[]]$AllowedPatterns = @())
    $danger = '(?im)\b(corrupt|conceal(?:ment|ed)?|repair(?:ed|ing)?|invalid data|timestamp(?:s)? (?:are |is )?(?:invalid|non[- ]monoton(?:ic|ous)|discontinuity|repair)|non[- ]monoton(?:ic|ous) dts|invalid nal|error while decoding)\b'
    foreach ($line in @($Stderr -split "`r?`n")) {
        if ($line -notmatch $danger) { continue }
        $allowed = $false
        foreach ($pattern in $AllowedPatterns) { if ($line -match $pattern) { $allowed = $true; break } }
        if (-not $allowed) { throw "Undeclared diagnostic indicates corrupt/concealed/repaired/invalid media: $line" }
    }
    return $true
}
