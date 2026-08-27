using System.Diagnostics;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0G05Stage2SemanticHelperTests
{
    [Fact]
    public void CombinedGraphExpandsEveryFrozenVariantPropertyAndRejectsUnknownPlaceholders()
    {
        var smoke = PsQuote(PathInRepo("eng", "gate0", "G05Stage2SmokeHelpers.psm1"));
        var module = PsQuote(PathInRepo("eng", "gate0", "G05Stage2ASemanticHelpers.psm1"));
        var contract = PsQuote(PathInRepo("eng", "gate0", "g0.5-stage2-workload-contract.json"));
        var script = $"Import-Module '{smoke}' -Force;Import-Module '{module}' -Force;$c=Get-Content '{contract}' -Raw|ConvertFrom-Json -Depth 64;" +
            "$cases=@(@('baseline-1v1a','720p'),@('typical-2v4a','720p'),@('stress-4v8a','720p'),@('stress-4v8a','1080p'));foreach($case in $cases){$w=@($c.workloads|? id -eq $case[0])[0];$v=@($w.resolutionVariants|? id -eq $case[1])[0];$g=Get-G05Stage2ACombinedGraph $w $v;if($g -match '\\{{variant\\.' -or $g -notmatch '\\[vout\\]' -or $g -notmatch '\\[aout\\]'){exit 10}};$bad=New-Object psobject;$bad|Add-Member NoteProperty videoFilterGraph 'scale={{variant.unknown}}';$bad|Add-Member NoteProperty audioFilterGraph 'anull';$v=New-Object psobject;$v|Add-Member NoteProperty width 1;try{{Get-G05Stage2ACombinedGraph $bad $v|Out-Null;exit 11}}catch{{if($_.Exception.Message -notmatch 'unresolved variant placeholder'){{exit 12}}}}";

        var result = Run("pwsh", ["-NoProfile", "-Command", script]);
        Assert.True(result.ExitCode == 0, result.Output);
    }

    [Fact]
    public void GenericAudioTruthAndVisualHelpersAreBoundToTheThreeFrozenWorkloads()
    {
        var helper = File.ReadAllText(PathInRepo("eng", "gate0", "G05Stage2ASemanticHelpers.psm1"));
        foreach (var value in new[]
        {
            "function New-G05Stage2AAudioTruth", "baseline-1v1a", "typical-2v4a", "stress-4v8a",
            "0C8C1E73ADACCB558CA563299A3FF238649A4995599AA49D2A6C37FE95AAC730",
            "81B41CD4DB85568930C15282A7268E2CED2610D27D48C6CB258E1D1C5C1B8C5A",
            "299846E21A0AF6F1416CCA7BF1BF8ACAC4A5EDDA78EFF9BEB392CC7B992B8CF5",
            "function Test-G05Stage2AVisual", "DecodedVideoIdentitySha256", "Unknown frozen Stage 2A visual workload"
        }) Assert.Contains(value, helper, StringComparison.Ordinal);
    }

    [Fact]
    public void NeighborMappingPreservesTheFrozenIntegerGeometry()
    {
        var module = PsQuote(PathInRepo("eng", "gate0", "G05Stage2ASemanticHelpers.psm1"));
        var script = $"Import-Module '{module}' -Force;Initialize-G05Stage2AVisualOracle;" +
            "if([ReelForge.Gate0.Stage2AVisualOracle]::MapNeighbor(6,0,80,480)-ne1){exit 20};" +
            "if([ReelForge.Gate0.Stage2AVisualOracle]::MapNeighbor(4,0,80,320)-ne1){exit 21};" +
            "if([ReelForge.Gate0.Stage2AVisualOracle]::MapNeighbor(0,80,160,480)-ne80){exit 22}";
        var result = Run("pwsh", ["-NoProfile", "-Command", script]);
        Assert.True(result.ExitCode == 0, result.Output);
    }

    private static (int ExitCode, string Output) Run(string fileName, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(fileName) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    private static string PsQuote(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string PathInRepo(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. parts]);
    }
}
