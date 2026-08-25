namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0G04InputProofContractTests
{
    private static readonly string[] ExpectedFixtureProducerComponents = ["h264_nvenc", "libvorbis", "libvpx"];
    private static readonly string[] ExpectedResolvedMediaTypes = ["audio", "video"];
    private static readonly string[] ExpectedSelectionDescriptorFields = ["codecIdentity", "defaultDisposition", "language", "mediaType", "observedDescriptor", "streamIndex", "timing", "title"];
    private static readonly string[] ExpectedReselectionBlockers = ["runtime-capability-change", "source-byte-change"];
    private static readonly string[] ExpectedSelectionCaseIds = ["S1-OneUsable", "S2-OneDefault", "S3-NoDefault", "S4-MultipleDefaults", "S5-AttachedPicture", "S6-UndecodableDefault", "S7-Descriptors"];
    private static readonly string[] ExpectedClassificationCaseIds = ["N1-MisleadingExtension", "N2-CorruptOrTruncated", "N3-NoUsableRequestedMedia", "N4-DecoderMissing", "N5-MultipleStreams", "N6-OutsideEnvelopeCapabilityQualified", "N6-ProtectedRejected", "N7-InvalidRuntimePair"];
    private static readonly string[] ExpectedFixtureRecipeStatuses = ["resolved", "unresolved-producer"];
    private static readonly string[] ExpectedFixtureProductionRoles = ["approved-fixture-producer", "fixture-production-only"];
    private static readonly string[] ExpectedNvencEvidenceFields = ["p2RuntimeIdentity", "osIdentity", "gpuIdentity", "driverIdentity", "exactCommand", "rawSourceHashes", "outputHash", "profile", "level", "pixelFormat", "timingMetadata"];
    private static readonly int[] ExpectedVideoDecodedFrameCounts = [5, 48, 50, 60, 120];
    private static readonly int[] ExpectedVfrPairedAudioSampleCounts = [88200, 96000];
    private static readonly Dictionary<string, string[]> ExpectedAudioEncoderOptions = new()
    {
        ["aac|mono"] = ["-c:a", "aac", "-profile:a", "aac_low", "-b:a", "96k"],
        ["aac|stereo"] = ["-c:a", "aac", "-profile:a", "aac_low", "-b:a", "192k"],
        ["mp3|mono"] = ["-c:a", "libmp3lame", "-b:a", "96k"],
        ["mp3|stereo"] = ["-c:a", "libmp3lame", "-b:a", "192k"],
        ["opus|mono"] = ["-c:a", "libopus", "-b:a", "64k", "-vbr", "off", "-compression_level", "10", "-frame_duration", "20"],
        ["opus|stereo"] = ["-c:a", "libopus", "-b:a", "128k", "-vbr", "off", "-compression_level", "10", "-frame_duration", "20"],
        ["vorbis|mono"] = ["-c:a", "libvorbis", "-q:a", "4"],
        ["vorbis|stereo"] = ["-c:a", "libvorbis", "-q:a", "4"],
        ["pcm_s16le|mono"] = ["-c:a", "pcm_s16le", "-rf64", "never"],
        ["pcm_s16le|stereo"] = ["-c:a", "pcm_s16le", "-rf64", "never"],
        ["flac|mono"] = ["-c:a", "flac", "-compression_level", "5"],
        ["flac|stereo"] = ["-c:a", "flac", "-compression_level", "5"]
    };

    [Fact]
    public void ContractEnumeratesEveryApprovedGuaranteedCommonCaseWithoutExpansionSyntax()
    {
        using var contract = ReadContract();
        var cases = contract.RootElement.GetProperty("guaranteedCases").EnumerateArray().ToArray();

        Assert.Equal(256, cases.Length);
        var ids = cases.Select(@case => @case.GetProperty("id").GetString()).ToArray();
        Assert.DoesNotContain(ids, id => string.IsNullOrWhiteSpace(id) || id.Contains('*') || id.Contains('{') || id.Contains("-set", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(cases, @case =>
        {
            Assert.Equal("guaranteed-common", @case.GetProperty("expectedVerdict").GetString());
            Assert.True(@case.GetProperty("streams").GetArrayLength() > 0);
            Assert.True(@case.GetProperty("requiredComponents").GetProperty("decoders").GetArrayLength() > 0);
            Assert.Contains(@case.GetProperty("fixtureProduction").GetProperty("role").GetString(), ExpectedFixtureProductionRoles);
        });

        Assert.Equal(218, cases.Count(@case => @case.GetProperty("family").GetString() == "video"));
        Assert.Equal(30, cases.Count(@case => @case.GetProperty("family").GetString() == "audio"));
        Assert.Equal(8, cases.Count(@case => @case.GetProperty("family").GetString() == "image"));
    }

    [Fact]
    public void ContractRetainsTheExactVideoContainerPairAndVariantBoundaries()
    {
        using var contract = ReadContract();
        var cases = contract.RootElement.GetProperty("guaranteedCases").EnumerateArray().ToArray();
        var video = cases.Where(@case => @case.GetProperty("family").GetString() == "video").ToArray();

        Assert.Equal(64, video.Count(@case => @case.GetProperty("container").GetString() == "MP4"));
        Assert.Equal(11, video.Count(@case => @case.GetProperty("container").GetString() == "MOV"));
        Assert.Equal(34, video.Count(@case => @case.GetProperty("container").GetString() == "WEBM"));
        Assert.Equal(109, video.Count(@case => @case.GetProperty("container").GetString() == "MATROSKA"));

        Assert.All(video, @case =>
        {
            var streams = @case.GetProperty("streams").EnumerateArray().ToArray();
            var videoStream = streams.Single(stream => stream.GetProperty("type").GetString() == "video");
            Assert.Equal("yuv420p", videoStream.GetProperty("pixelFormat").GetString());
            Assert.True(videoStream.GetProperty("width").GetInt32() <= 1920);
            Assert.True(videoStream.GetProperty("height").GetInt32() <= 1080);
            Assert.Equal(8, @case.GetProperty("bounds").GetProperty("bitDepth").GetInt32());
            Assert.Equal("60/1", @case.GetProperty("bounds").GetProperty("maximumFrameRate").GetString());
        });

        var vp9Cases = video.Where(@case => @case.GetProperty("streams").EnumerateArray().Any(stream => stream.GetProperty("codec").GetString() == "vp9")).ToArray();
        Assert.NotEmpty(vp9Cases);
        Assert.All(vp9Cases, @case =>
        {
            var vp9 = @case.GetProperty("streams").EnumerateArray().Single(stream => stream.GetProperty("codec").GetString() == "vp9");
            Assert.Equal("profile0", vp9.GetProperty("profile").GetString());
            Assert.Equal("4.1", vp9.GetProperty("maximumLevel").GetString());
        });

        Assert.DoesNotContain(video, @case => @case.GetProperty("container").GetString() == "MP4" && @case.GetProperty("streams").EnumerateArray().Any(stream => stream.GetProperty("type").GetString() == "audio" && stream.GetProperty("codec").GetString() == "pcm_s16le"));
        Assert.All(video.Where(@case => @case.GetProperty("container").GetString() == "MATROSKA"), @case =>
        {
            var sourceCaseId = @case.GetProperty("fixtureProduction").GetProperty("sourceCaseId").GetString();
            Assert.False(string.IsNullOrWhiteSpace(sourceCaseId));
            Assert.Contains(cases, source => source.GetProperty("id").GetString() == sourceCaseId);
            Assert.Equal("stream-copy-only", @case.GetProperty("fixtureProduction").GetProperty("remux").GetString());
        });
    }

    [Fact]
    public void ContractSeparatesAuthorizedFixtureProducersFromNativeDecodersUnderTest()
    {
        using var contract = ReadContract();
        var roles = contract.RootElement.GetProperty("componentRoles");
        var recipes = contract.RootElement.GetProperty("fixtureRecipes").EnumerateArray().ToArray();

        Assert.True(roles.GetProperty("decoderUnderTestMustBeNativeP2").GetBoolean());
        var authorized = roles.GetProperty("fixtureProductionOnly").EnumerateArray().ToArray();
        Assert.All(ExpectedFixtureProducerComponents, expected => Assert.Contains(expected, authorized.Select(component => component.GetProperty("component").GetString())));
        var nvenc = authorized.Single(component => component.GetProperty("component").GetString() == "h264_nvenc");
        Assert.Equal("RTX 3070 Ti", nvenc.GetProperty("referenceHardware").GetString());
        Assert.Contains("shipping", nvenc.GetProperty("notApprovedFor").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("h264", roles.GetProperty("nativeDecodersUnderTest").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("vp8", roles.GetProperty("nativeDecodersUnderTest").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("vorbis", roles.GetProperty("nativeDecodersUnderTest").EnumerateArray().Select(value => value.GetString()));
        Assert.All(roles.GetProperty("fixtureProductionOnly").EnumerateArray(), role => Assert.False(string.IsNullOrWhiteSpace(role.GetProperty("decisionProvenance").GetString())));
        var registeredProducers = roles.GetProperty("fixtureProductionOnly").EnumerateArray().Select(component => component.GetProperty("component").GetString()).ToHashSet(StringComparer.Ordinal);
        var recipeProducerTokens = contract.RootElement.GetProperty("fixtureRecipes").EnumerateArray().SelectMany(recipe => recipe.GetProperty("producerEncoders").EnumerateArray().Select(encoder => encoder.GetString())).Where(token => !string.IsNullOrWhiteSpace(token)).ToHashSet(StringComparer.Ordinal);
        Assert.True(recipeProducerTokens.IsSubsetOf(registeredProducers));
        Assert.Equal(ExpectedNvencEvidenceFields, nvenc.GetProperty("requiredEvidenceFields").EnumerateArray().Select(value => value.GetString()));

        var cases = contract.RootElement.GetProperty("guaranteedCases").EnumerateArray().ToArray();
        Assert.All(cases.Where(@case => @case.GetProperty("container").GetString() != "MATROSKA" && (@case.GetProperty("id").GetString()!.Contains("H264-MAIN") || @case.GetProperty("id").GetString()!.Contains("H264-HIGH"))), @case =>
            Assert.Contains("h264_nvenc", @case.GetProperty("fixtureProduction").GetProperty("authorizedProducer").GetString(), StringComparison.Ordinal));
        foreach (var vp8Case in cases.Where(@case => @case.GetProperty("id").GetString()!.Contains("VP8") && @case.GetProperty("family").GetString() == "video"))
        {
            var sourceCase = vp8Case.GetProperty("container").GetString() == "MATROSKA"
                ? cases.Single(candidate => candidate.GetProperty("id").GetString() == vp8Case.GetProperty("fixtureProduction").GetProperty("sourceCaseId").GetString())
                : vp8Case;
            var sourceRecipeId = sourceCase.GetProperty("fixtureProduction").GetProperty("recipeId").GetString();
            var sourceRecipe = recipes.Single(recipe => recipe.GetProperty("id").GetString() == sourceRecipeId);
            Assert.Contains("libvpx", sourceRecipe.GetProperty("producerEncoders").EnumerateArray().Select(value => value.GetString()));
        }
        Assert.All(cases.Where(@case => @case.GetProperty("id").GetString()!.StartsWith("A-OGG-VORBIS", StringComparison.Ordinal)), @case =>
            Assert.Contains("libvorbis", @case.GetProperty("fixtureProduction").GetProperty("authorizedProducer").GetString(), StringComparison.Ordinal));
    }

    [Fact]
    public void ContractPreservesAllSelectionAndDiagnosticClassificationBranches()
    {
        using var contract = ReadContract();
        var policy = contract.RootElement.GetProperty("selectionPolicy");
        Assert.Equal("default-disposition-lowest-index-fail-on-unusable-default", policy.GetProperty("name").GetString());
        Assert.True(policy.GetProperty("excludeAttachedPicturesFromTimelineVideo").GetBoolean());
        Assert.True(policy.GetProperty("revalidateBeforeEveryDependentOperation").GetBoolean());
        Assert.Equal(ExpectedResolvedMediaTypes, policy.GetProperty("independentlyResolve").EnumerateArray().Select(value => value.GetString()).Order());
        Assert.Contains("source-content-sha256", policy.GetProperty("persistAgainst").GetString());
        Assert.Equal(ExpectedSelectionDescriptorFields, policy.GetProperty("requiredDescriptor").EnumerateArray().Select(value => value.GetString()).Order());
        Assert.Equal(ExpectedReselectionBlockers, policy.GetProperty("neverSilentlyReselectAfter").EnumerateArray().Select(value => value.GetString()).Order());

        var selection = contract.RootElement.GetProperty("selectionCases").EnumerateArray().ToArray();
        Assert.Equal(ExpectedSelectionCaseIds, selection.Select(@case => @case.GetProperty("id").GetString()));
        var undecodableDefault = selection.Single(@case => @case.GetProperty("id").GetString() == "S6-UndecodableDefault").GetProperty("expected").GetString();
        Assert.Contains("blocked", undecodableDefault, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no fallback", undecodableDefault, StringComparison.OrdinalIgnoreCase);

        var classification = contract.RootElement.GetProperty("classificationCases").EnumerateArray().ToArray();
        Assert.Equal(ExpectedClassificationCaseIds, classification.Select(@case => @case.GetProperty("id").GetString()));
        Assert.Contains("blocked", classification.Single(@case => @case.GetProperty("id").GetString() == "N4-DecoderMissing").GetProperty("expected").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("runtime-unavailable", classification.Single(@case => @case.GetProperty("id").GetString() == "N7-InvalidRuntimePair").GetProperty("expected").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContractClosesEveryCaseToAnExactRecipeOrAnHonestBlockedFixtureProvenance()
    {
        using var contract = ReadContract();
        var cases = contract.RootElement.GetProperty("guaranteedCases").EnumerateArray().ToArray();
        var recipes = contract.RootElement.GetProperty("fixtureRecipes").EnumerateArray().ToArray();

        Assert.Equal(163, recipes.Length);
        Assert.Equal(recipes.Length, recipes.Select(recipe => recipe.GetProperty("id").GetString()).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(contract.RootElement.GetProperty("fixtureRecipeRules").EnumerateArray().Select(value => value.GetString()), rule => rule!.Contains("unresolved-producer", StringComparison.Ordinal));
        foreach (var @case in cases)
        {
            var recipeId = @case.GetProperty("fixtureProduction").GetProperty("recipeId").GetString();
            var recipe = recipes.Single(candidate => candidate.GetProperty("id").GetString() == recipeId);
            Assert.True(recipe.GetProperty("sourcePrimitiveIds").GetArrayLength() > 0);
            Assert.True(recipe.GetProperty("sourceArtifacts").GetArrayLength() > 0);
            Assert.True(recipe.GetProperty("transforms").GetArrayLength() > 0);
            Assert.False(string.IsNullOrWhiteSpace(recipe.GetProperty("artifactExtension").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(recipe.GetProperty("oracleProfileId").GetString()));
            var status = recipe.GetProperty("status").GetString();
            Assert.Contains(status, ExpectedFixtureRecipeStatuses);
            if (status == "resolved")
            {
                Assert.False(string.IsNullOrWhiteSpace(recipe.GetProperty("muxer").GetString()));
                Assert.True(recipe.GetProperty("producerEncoders").GetArrayLength() > 0 || recipeId == "R-MATROSKA-STREAM-COPY");
            }
            else
            {
                Assert.Contains("blocked", recipe.GetProperty("disposition").GetString(), StringComparison.OrdinalIgnoreCase);
                Assert.Empty(recipe.GetProperty("producerEncoders").EnumerateArray());
            }

            if (@case.GetProperty("container").GetString() == "MATROSKA")
            {
                Assert.Equal("R-MATROSKA-STREAM-COPY", recipeId);
                Assert.Equal("stream-copy-only", @case.GetProperty("fixtureProduction").GetProperty("remux").GetString());
                Assert.False(string.IsNullOrWhiteSpace(@case.GetProperty("fixtureProduction").GetProperty("sourceCaseId").GetString()));
            }
        }

        var authority = contract.RootElement.GetProperty("fixtureRecipeAuthority");
        Assert.Equal("eng/gate0/fixture-source-inventory.json", authority.GetProperty("sourcePrimitiveInventory").GetString());
        Assert.Contains("source hashes", authority.GetProperty("closureRule").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContractClosesRecipeOraclesTransformsAndSelectionClassificationEvidence()
    {
        using var contract = ReadContract();
        var recipes = contract.RootElement.GetProperty("fixtureRecipes").EnumerateArray().ToArray();
        var cases = contract.RootElement.GetProperty("guaranteedCases").EnumerateArray().ToArray();
        var oracleIds = contract.RootElement.GetProperty("oracleProfiles").EnumerateArray().Select(profile => profile.GetProperty("id").GetString()).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(24, oracleIds.Count);
        Assert.All(recipes, recipe =>
        {
            Assert.Contains(recipe.GetProperty("oracleProfileId").GetString(), oracleIds);
            Assert.True(recipe.GetProperty("sourceArtifacts").GetArrayLength() > 0);
            Assert.True(recipe.GetProperty("transforms").GetArrayLength() > 0);
        });

        var sharedCfrOracles = contract.RootElement.GetProperty("oracleProfiles").EnumerateArray().Where(profile => profile.GetProperty("id").GetString() is "O-VIDEO-AUDIO-IDENTITY" or "O-VIDEO-ONLY-IDENTITY").ToArray();
        Assert.Equal(2, sharedCfrOracles.Length);
        Assert.All(sharedCfrOracles, oracle =>
        {
            Assert.Equal(3, oracle.GetProperty("structure").GetProperty("distinctFrameIdentityCount").GetInt32());
            Assert.False(oracle.GetProperty("structure").TryGetProperty("videoFrameCount", out _));
        });
        Assert.All(recipes.Where(recipe => recipe.TryGetProperty("expectedDecodedFrameCount", out _)), recipe =>
        {
            var count = recipe.GetProperty("expectedDecodedFrameCount").GetProperty("exact").GetInt32();
            Assert.Contains(count, ExpectedVideoDecodedFrameCounts);
        });
        Assert.All(recipes.Where(recipe => recipe.GetProperty("status").GetString() == "resolved" && recipe.GetProperty("id").GetString()!.StartsWith("R-V-", StringComparison.Ordinal) && !recipe.GetProperty("id").GetString()!.Contains("VFR_OFFSET", StringComparison.Ordinal)), recipe =>
            Assert.True(recipe.TryGetProperty("expectedDecodedFrameCount", out _)));
        Assert.All(recipes.Where(recipe => recipe.TryGetProperty("presentationTiming", out _)), recipe =>
        {
            Assert.Equal(2, recipe.GetProperty("presentationTiming").GetProperty("containerDurationSeconds").GetInt32());
            Assert.Equal(2000, recipe.GetProperty("expectedAudioDecode").GetProperty("contentDurationMilliseconds").GetInt32());
            var audio = recipe.GetProperty("expectedAudioDecode");
            var nominalSamples = audio.GetProperty("postTransformExpectedSampleCount").GetInt32();
            var sourceCase = cases.Single(@case => @case.GetProperty("fixtureProduction").GetProperty("recipeId").GetString() == recipe.GetProperty("id").GetString());
            var sourceAudio = sourceCase.GetProperty("streams").EnumerateArray().Single(stream => stream.GetProperty("type").GetString() == "audio");
            Assert.Equal(sourceAudio.GetProperty("sampleRate").GetInt32() * 2, nominalSamples);
            Assert.Contains(nominalSamples, ExpectedVfrPairedAudioSampleCounts);
            Assert.Equal(nominalSamples, audio.GetProperty("sampleEnvelope").GetProperty("expected").GetInt32());
            Assert.Equal(3072, audio.GetProperty("codecDelayToleranceSamples").GetInt32());
        });

        Assert.All(recipes.Where(recipe => recipe.GetProperty("status").GetString() == "resolved" && (recipe.GetProperty("id").GetString()!.Contains("H264-MAIN") || recipe.GetProperty("id").GetString()!.Contains("H264-HIGH")) && recipe.GetProperty("id").GetString() != "R-MATROSKA-STREAM-COPY"), recipe =>
        {
            if (recipe.GetProperty("status").GetString() == "resolved") Assert.Contains("h264_nvenc", recipe.GetProperty("encoderOptions").EnumerateArray().Select(value => value.GetString()));
        });
        foreach (var sourceCase in cases.Where(@case => @case.GetProperty("container").GetString() != "MATROSKA" && @case.GetProperty("streams").EnumerateArray().Any(stream => stream.GetProperty("type").GetString() == "audio")))
        {
            var audioStream = sourceCase.GetProperty("streams").EnumerateArray().Single(stream => stream.GetProperty("type").GetString() == "audio");
            var key = audioStream.GetProperty("codec").GetString() + "|" + audioStream.GetProperty("channels").GetString();
            var recipe = recipes.Single(candidate => candidate.GetProperty("id").GetString() == sourceCase.GetProperty("fixtureProduction").GetProperty("recipeId").GetString());
            Assert.Equal(ExpectedAudioEncoderOptions[key], recipe.GetProperty("audioEncoderOptions").EnumerateArray().Select(option => option.GetString()));
            if (audioStream.GetProperty("codec").GetString() == "vorbis") Assert.Contains("pinned fixture identity", recipe.GetProperty("audioEncoderOptionsPurpose").GetString(), StringComparison.OrdinalIgnoreCase);
        }
        foreach (var pairedVideoCase in cases.Where(@case => @case.GetProperty("family").GetString() == "video" && @case.GetProperty("container").GetString() != "MATROSKA" && @case.GetProperty("streams").EnumerateArray().Any(stream => stream.GetProperty("type").GetString() == "audio")))
        {
            var audioStream = pairedVideoCase.GetProperty("streams").EnumerateArray().Single(stream => stream.GetProperty("type").GetString() == "audio");
            var recipe = recipes.Single(candidate => candidate.GetProperty("id").GetString() == pairedVideoCase.GetProperty("fixtureProduction").GetProperty("recipeId").GetString());
            var audio = recipe.GetProperty("expectedAudioDecode");
            var samples = audioStream.GetProperty("sampleRate").GetInt32() * 2;
            Assert.Equal(2000, audio.GetProperty("contentDurationMilliseconds").GetInt32());
            Assert.Equal(samples, audio.GetProperty("postTransformExpectedSampleCount").GetInt32());
            if (audioStream.GetProperty("codec").GetString() == "pcm_s16le")
            {
                Assert.Equal(samples, audio.GetProperty("exactSampleCount").GetInt32());
                Assert.Equal(0, audio.GetProperty("durationToleranceMilliseconds").GetInt32());
            }
            else
            {
                Assert.Equal(samples, audio.GetProperty("sampleEnvelope").GetProperty("expected").GetInt32());
                Assert.Equal(3072, audio.GetProperty("codecDelayToleranceSamples").GetInt32());
                Assert.Equal(60, audio.GetProperty("durationToleranceMilliseconds").GetInt32());
            }
        }
        Assert.All(recipes.Where(recipe => recipe.GetProperty("status").GetString() == "unresolved-producer"), recipe =>
        {
            Assert.Equal("blocked-fixture-provenance", recipe.GetProperty("proofDisposition").GetString());
            Assert.Contains("cannot be promoted", recipe.GetProperty("expectedResult").GetString(), StringComparison.OrdinalIgnoreCase);
        });

        Assert.DoesNotContain(recipes, recipe => recipe.GetProperty("producerEncoders").EnumerateArray().Any(encoder => encoder.GetString() == "vorbis"));
        Assert.All(recipes.Where(recipe => recipe.GetProperty("id").GetString()!.Contains("VP8-VORBIS") || recipe.GetProperty("id").GetString()!.Contains("OGG_VORBIS")), recipe =>
            Assert.Contains("libvorbis", recipe.GetProperty("producerEncoders").EnumerateArray().Select(encoder => encoder.GetString())));

        foreach (var matroskaVp8Vorbis in cases.Where(@case => @case.GetProperty("container").GetString() == "MATROSKA" && @case.GetProperty("id").GetString()!.Contains("VP8-VORBIS")))
        {
            var sourceCase = cases.Single(candidate => candidate.GetProperty("id").GetString() == matroskaVp8Vorbis.GetProperty("fixtureProduction").GetProperty("sourceCaseId").GetString());
            var sourceRecipe = recipes.Single(recipe => recipe.GetProperty("id").GetString() == sourceCase.GetProperty("fixtureProduction").GetProperty("recipeId").GetString());
            Assert.Contains("libvpx", sourceRecipe.GetProperty("producerEncoders").EnumerateArray().Select(encoder => encoder.GetString()));
            Assert.Contains("libvorbis", sourceRecipe.GetProperty("producerEncoders").EnumerateArray().Select(encoder => encoder.GetString()));
        }

        var selection = contract.RootElement.GetProperty("selectionCases").EnumerateArray().ToArray();
        var classification = contract.RootElement.GetProperty("classificationCases").EnumerateArray().ToArray();
        foreach (var @case in selection.Concat(classification))
        {
            Assert.Contains(@case.GetProperty("fixtureRecipeId").GetString(), recipes.Select(recipe => recipe.GetProperty("id").GetString()));
            Assert.Contains(@case.GetProperty("oracleProfileId").GetString(), oracleIds);
        }
        Assert.All(selection, @case =>
        {
            var expected = @case.GetProperty("expectedSelection");
            Assert.True(expected.GetProperty("observedStreams").GetArrayLength() > 0);
            Assert.True(expected.TryGetProperty("ignoredStreamIndices", out _));
        });
        Assert.All(classification, @case => Assert.False(string.IsNullOrWhiteSpace(@case.GetProperty("expectedClassification").GetString())));
        Assert.All(recipes.Where(recipe => recipe.GetProperty("status").GetString() == "preflight-only"), recipe =>
        {
            Assert.False(string.IsNullOrWhiteSpace(recipe.GetProperty("fixtureKind").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(recipe.GetProperty("executionClaim").GetString()));
        });
        Assert.All(recipes.Where(recipe => recipe.GetProperty("id").GetString()!.StartsWith("R-S", StringComparison.Ordinal)), recipe =>
        {
            Assert.Equal("synthetic-selection-snapshot-derived-from-F8-identities", recipe.GetProperty("fixtureKind").GetString());
            Assert.Contains("no ffprobe observation", recipe.GetProperty("executionClaim").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal("F8 explicit multi-stream semantic proof", recipe.GetProperty("baseExecutableEvidence").GetString());
        });

        using var inventory = System.Text.Json.JsonDocument.Parse(File.ReadAllText(RepositoryPath("eng", "gate0", "fixture-source-inventory.json")));
        var inventoryPaths = inventory.RootElement.GetProperty("files").EnumerateArray().Select(file => file.GetProperty("path").GetString()).ToHashSet(StringComparer.Ordinal);
        foreach (var fileId in recipes.SelectMany(recipe => recipe.GetProperty("sourceArtifacts").EnumerateArray()).SelectMany(artifact => artifact.GetProperty("fileIds").EnumerateArray()).Select(file => file.GetString()))
        {
            if (fileId == "sourceCaseId") continue;
            Assert.Contains(fileId, inventoryPaths);
        }
    }

    [Fact]
    public void ContractRecordsNonGeneralizationAndDeferredBoundaryExclusions()
    {
        using var contract = ReadContract();
        Assert.Equal("P2.BtbnLgplShared.WindowsX64.20260820", contract.RootElement.GetProperty("profileId").GetString());
        Assert.Contains("not a shipping-runtime selection", contract.RootElement.GetProperty("purpose").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(contract.RootElement.GetProperty("scopeBoundaries").EnumerateArray().Select(value => value.GetString()), value => value!.Contains("do not authorize a range", StringComparison.OrdinalIgnoreCase));

        var exclusions = contract.RootElement.GetProperty("excludedBoundaries").EnumerateArray().Select(value => value.GetString()).ToArray();
        foreach (var excluded in new[] { "HEVC/H.265", "HEIC/HEIF", "HDR", "greater-than-8-bit media", "multichannel audio", "real-time editing", "long-form integrity" }) Assert.Contains(excluded, exclusions);
    }

    private static System.Text.Json.JsonDocument ReadContract() => System.Text.Json.JsonDocument.Parse(File.ReadAllText(RepositoryPath("eng", "gate0", "g0.4-input-proof-contract.json")));

    private static string RepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent;
        if (directory is null) throw new DirectoryNotFoundException("Could not locate repository root.");
        return Path.Combine([directory.FullName, .. segments]);
    }
}
