namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0G04InputProofAuthoringTests
{
    [Fact]
    public void AuthoringPropertyReaderSupportsSemanticPassDictionaries()
    {
        var module = RepositoryPath("eng", "gate0", "input-proof", "Authoring.ps1");
        var command = $". '{module.Replace("'", "''", StringComparison.Ordinal)}'; $record=[ordered]@{{semanticProofPassed=$true}}; if(-not (Get-G04AuthoringValue $record 'semanticProofPassed')){{exit 1}}";
        var start = new System.Diagnostics.ProcessStartInfo("pwsh") { UseShellExecute = false };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(command);
        using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("Could not start PowerShell.");
        process.WaitForExit();

        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public void AuthoringModuleBindsDirectlyToSharedCommonHelpers()
    {
        var source = File.ReadAllText(RepositoryPath("eng", "gate0", "input-proof", "Authoring.ps1"));

        Assert.Contains("function New-G04CaseArtifact", source, StringComparison.Ordinal);
        Assert.Contains("Invoke-G04RecordedCommand -Context (Get-G04AuthoringCommonContext $Context) -Name", source, StringComparison.Ordinal);
        Assert.Contains("Get-G04ArtifactRecord -Context (Get-G04AuthoringCommonContext $Context) -Path", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Get-Command Invoke-G04RecordedCommand", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Get-Command Get-G04ArtifactRecord", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoringModuleBlocksUnresolvedRecipesBeforeAnyProducerCommand()
    {
        var source = File.ReadAllText(RepositoryPath("eng", "gate0", "input-proof", "Authoring.ps1"));
        var blocked = source.IndexOf("Recipe.status -eq 'unresolved-producer'", StringComparison.Ordinal);
        var invoke = source.IndexOf("Invoke-G04RecordedCommand -Context (Get-G04AuthoringCommonContext $Context) -Name \"author-", StringComparison.Ordinal);

        Assert.True(blocked >= 0);
        Assert.True(invoke > blocked);
        Assert.Contains("blocked-fixture-provenance", source, StringComparison.Ordinal);
        Assert.Contains("no execution or substitution occurred", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoringModuleUsesAtomicPartialsAndRequiresRemuxSourceOrdering()
    {
        var source = File.ReadAllText(RepositoryPath("eng", "gate0", "input-proof", "Authoring.ps1"));

        Assert.Contains("$partial = \"$final.partial\"", source, StringComparison.Ordinal);
        Assert.Contains("Move-Item -LiteralPath $partial -Destination $final -Force", source, StringComparison.Ordinal);
        Assert.Contains("Stream-copy source case '$sourceId' must be authored first", source, StringComparison.Ordinal);
        Assert.Contains("'-c','copy'", source, StringComparison.Ordinal);
        Assert.Contains("streamCopyOnly=$true", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoringModuleRetainsDistinctFixtureProducerAndNvencProvenanceRules()
    {
        var source = File.ReadAllText(RepositoryPath("eng", "gate0", "input-proof", "Authoring.ps1"));

        Assert.Contains("h264_nvenc", source, StringComparison.Ordinal);
        Assert.Contains("libvpx-vp9", source, StringComparison.Ordinal);
        Assert.Contains("libvpx", source, StringComparison.Ordinal);
        Assert.Contains("libvorbis", source, StringComparison.Ordinal);
        Assert.Contains("p2RuntimeIdentity", source, StringComparison.Ordinal);
        Assert.Contains("rawSourceHashes", source, StringComparison.Ordinal);
        Assert.Contains("timingMetadata", source, StringComparison.Ordinal);
        Assert.Contains("fixtureProductionOnly=$true", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoringModuleUsesDeclaredRawAudioFormatAndExactDurationRules()
    {
        var source = File.ReadAllText(RepositoryPath("eng", "gate0", "input-proof", "Authoring.ps1"));

        Assert.Contains("rawSampleRate", source, StringComparison.Ordinal);
        Assert.Contains("rawChannels", source, StringComparison.Ordinal);
        Assert.Contains("-ar',[string]$input.rawSampleRate", source, StringComparison.Ordinal);
        Assert.Contains("-ac',[string]$input.rawChannels", source, StringComparison.Ordinal);
        Assert.Contains("aloop=loop=-1:size=$loopSamples", source, StringComparison.Ordinal);
        Assert.Contains("apad=whole_dur=2,atrim=duration=2", source, StringComparison.Ordinal);
        Assert.Contains("if ($null -ne $video) { $args.AddRange([string[]]@('-t','2')) }", source, StringComparison.Ordinal);
        Assert.Contains("Recipe $($Recipe.id) must declare exact audioEncoderOptions", source, StringComparison.Ordinal);
        Assert.Contains("timing.kind -eq 'vfr-nonzero-pts'", source, StringComparison.Ordinal);
        Assert.Contains("required NVENC GPU and driver identity could not be recorded", source, StringComparison.Ordinal);
        Assert.Contains("System32\\nvidia-smi.exe", source, StringComparison.Ordinal);
        Assert.Contains("--query-gpu=name,driver_version,pci.bus_id", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoringModuleHashesFixtureSourcesWithoutTreatingThemAsOutputArtifacts()
    {
        var source = File.ReadAllText(RepositoryPath("eng", "gate0", "input-proof", "Authoring.ps1"));

        Assert.Contains("function Get-G04AuthoringSourceRecord", source, StringComparison.Ordinal);
        Assert.Contains("Source must be contained beneath FixtureRoot", source, StringComparison.Ordinal);
        Assert.Contains("rawSourceHashes = @($RawSourcePaths", source, StringComparison.Ordinal);
        Assert.Contains("Get-G04AuthoringSourceRecord -Context $Context -Path $_", source, StringComparison.Ordinal);
        Assert.DoesNotContain("rawSourceHashes = @($RawSourcePaths | ForEach-Object { Get-G04ArtifactRecord", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoringSourceRecordSupportsAnExternalFixtureRootAndKeepsRelativeProvenance()
    {
        var root = Path.Combine(Path.GetTempPath(), "ReelForge-G04Authoring", Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "F4");
        Directory.CreateDirectory(nested);
        var source = Path.Combine(nested, "tone.pcm");
        File.WriteAllBytes(source, [1, 2, 3, 4]);
        try
        {
            var module = RepositoryPath("eng", "gate0", "input-proof", "Authoring.ps1");
            var result = RunPowerShell($$""". '{{Escape(module)}}'; $r=Get-G04AuthoringSourceRecord -Context @{ FixtureRoot='{{Escape(root)}}' } -Path '{{Escape(source)}}'; $r | ConvertTo-Json -Compress""");
            Assert.Equal(0, result.ExitCode);
            using var json = System.Text.Json.JsonDocument.Parse(result.StandardOutput);
            Assert.Equal("F4/tone.pcm", json.RootElement.GetProperty("path").GetString());
            Assert.Equal(4, json.RootElement.GetProperty("length").GetInt32());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void AuthoringAudioInputUsesTheDeclaredRawFormatBeforeTheCaseTarget()
    {
        var module = RepositoryPath("eng", "gate0", "input-proof", "Authoring.ps1");
        var contract = RepositoryPath("eng", "gate0", "g0.4-input-proof-contract.json");
        var command = $$""". '{{Escape(module)}}'; $c=Get-Content -LiteralPath '{{Escape(contract)}}' -Raw | ConvertFrom-Json; $r=$c.fixtureRecipes | Where-Object id -eq 'R-A-FLAC-MONO-48000'; $a=$c.guaranteedCases | Where-Object { $_.fixtureProduction.recipeId -eq $r.id }; $stream=@($a.streams | Where-Object type -eq 'audio')[0]; Get-G04AuthoringAudioInput -Recipe $r -Audio $stream | ConvertTo-Json -Compress""";
        var result = RunPowerShell(command);
        Assert.Equal(0, result.ExitCode);
        using var json = System.Text.Json.JsonDocument.Parse(result.StandardOutput);
        Assert.Equal(44100, json.RootElement.GetProperty("rawSampleRate").GetInt32());
        Assert.Equal(48000, json.RootElement.GetProperty("targetSampleRate").GetInt32());
    }

    [Fact]
    public void EveryResolvedRecipeCanOnlyNameAnExplicitSupportedProducerToken()
    {
        using var contract = System.Text.Json.JsonDocument.Parse(File.ReadAllText(RepositoryPath("eng", "gate0", "g0.4-input-proof-contract.json")));
        var supported = new HashSet<string>(StringComparer.Ordinal)
        {
            "aac", "flac", "h264_nvenc", "libmp3lame", "libopenh264", "libopus", "libvorbis", "libvpx", "libvpx-vp9", "mjpeg", "pcm_s16le", "png"
        };

        foreach (var recipe in contract.RootElement.GetProperty("fixtureRecipes").EnumerateArray().Where(recipe => recipe.GetProperty("status").GetString() == "resolved"))
        foreach (var producer in recipe.GetProperty("producerEncoders").EnumerateArray().Select(value => value.GetString()))
        {
            Assert.NotNull(producer);
            Assert.Contains(producer!, supported);
        }
    }

    [Fact]
    public void VideoEncoderSelectionUsesAuthorizedFallbackOrExactDeclaredProducer()
    {
        var module = RepositoryPath("eng", "gate0", "input-proof", "Authoring.ps1");
        var command = $$""". '{{Escape(module)}}'; $fallback=[pscustomobject]@{id='fallback';producerEncoders=@('libopenh264')}; $explicit=[pscustomobject]@{id='explicit';producerEncoders=@('h264_nvenc');encoderOptions=@('-c:v','h264_nvenc','-profile:v','main')}; [ordered]@{fallback=@(Assert-G04AuthoringVideoEncoderOptions -Recipe $fallback -Required $false);explicit=@(Assert-G04AuthoringVideoEncoderOptions -Recipe $explicit -Required $false)} | ConvertTo-Json -Compress""";
        var result = RunPowerShell(command);

        Assert.Equal(0, result.ExitCode);
        using var json = System.Text.Json.JsonDocument.Parse(result.StandardOutput);
        Assert.Collection(json.RootElement.GetProperty("fallback").EnumerateArray(),
            value => Assert.Equal("-c:v", value.GetString()),
            value => Assert.Equal("libopenh264", value.GetString()));
        Assert.Equal("h264_nvenc", json.RootElement.GetProperty("explicit")[1].GetString());
    }

    private static string RepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent;
        if (directory is null) throw new DirectoryNotFoundException("Could not locate repository root.");
        return Path.Combine([directory.FullName, .. segments]);
    }

    private static ProcessResult RunPowerShell(string command)
    {
        var start = new System.Diagnostics.ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(command);
        using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
