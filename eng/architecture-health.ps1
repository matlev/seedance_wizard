[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string] $GitHubStepSummary
)

$ErrorActionPreference = 'Stop'
$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path

$trackedFiles = & git -C $RepositoryRoot ls-files -- 'src/**/*.cs' 'src/**/*.xaml'
if ($LASTEXITCODE -ne 0) {
    throw "Could not enumerate tracked production source files."
}

$entries = @(foreach ($relativePath in $trackedFiles) {
    $fullPath = Join-Path $RepositoryRoot $relativePath
    $lineCount = (Get-Content -LiteralPath $fullPath).Count
    $band = if ($lineCount -ge 1200) {
        'Presumed design review (1200+)'
    }
    elseif ($lineCount -ge 800) {
        'Strong justification review (800+)'
    }
    elseif ($lineCount -ge 500) {
        'Architecture review (500+)'
    }
    else {
        $null
    }

    if ($null -ne $band) {
        [PSCustomObject]@{
            Path = $relativePath.Replace('\', '/')
            Lines = $lineCount
            Band = $band
        }
    }
})

$report = [System.Collections.Generic.List[string]]::new()
$report.Add('## Architecture health')
$report.Add('')
$report.Add('Advisory file-size review bands: 500 lines = architecture review; 800 = strong justification; 1200 = presumed design review. This report never fails a build solely because of size.')
$report.Add('')

if ($entries.Count -eq 0) {
    $report.Add('No tracked production `.cs` or `.xaml` files currently meet a review band.')
}
else {
    $report.Add('| File | Lines | Review band |')
    $report.Add('| --- | ---: | --- |')
    foreach ($entry in $entries | Sort-Object -Property @{ Expression = 'Lines'; Descending = $true }, Path) {
        $report.Add("| ``$($entry.Path)`` | $($entry.Lines) | $($entry.Band) |")
    }
}

$reportText = $report -join [Environment]::NewLine
Write-Output $reportText

if (-not [string]::IsNullOrWhiteSpace($GitHubStepSummary)) {
    Add-Content -LiteralPath $GitHubStepSummary -Value $reportText
}
