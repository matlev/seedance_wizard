using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ReelForge.Gate0.Artifacts;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0DurableArtifactRetentionTests
{
    [Fact]
    public void DurableManifestPinsPrivateContentAddressedR2WithoutCredentials()
    {
        var sourcePath = PathInRepo("eng", "gate0", "artifact-retention-manifest.json");
        var durablePath = PathInRepo("eng", "gate0", "artifact-manifest.json");
        var text = File.ReadAllText(durablePath);
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;

        Assert.Equal("Gate0.DurableR2Retention.V1", root.GetProperty("manifestId").GetString());
        var source = root.GetProperty("sourceInventory");
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourcePath))), source.GetProperty("sha256").GetString());
        Assert.Equal(3007, source.GetProperty("logicalArtifactCount").GetInt32());
        Assert.Equal(517_763_820, source.GetProperty("logicalArtifactBytes").GetInt64());

        var storage = root.GetProperty("storage");
        Assert.Equal("cloudflare-r2", storage.GetProperty("provider").GetString());
        Assert.Equal("reelforge-artifacts", storage.GetProperty("bucketName").GetString());
        Assert.True(storage.GetProperty("privateBucket").GetBoolean());
        Assert.False(storage.GetProperty("automaticDeletionLifecycle").GetBoolean());
        Assert.False(storage.GetProperty("ordinaryPullRequestWriteAccess").GetBoolean());
        Assert.False(storage.GetProperty("hostedCiCredentialRequired").GetBoolean());
        Assert.Equal("objects/sha256/<first-two-lowercase-hex>/<full-lowercase-sha256>", storage.GetProperty("objectKeyLayout").GetString());

        var credentials = root.GetProperty("credentialContract");
        Assert.Equal("Windows Credential Manager", credentials.GetProperty("provider").GetString());
        Assert.Equal("Generic", credentials.GetProperty("credentialType").GetString());
        Assert.Equal(
            [
                "ReelForge.Engineering.R2.AccountId",
                "ReelForge.Engineering.R2.AccessKeyId",
                "ReelForge.Engineering.R2.SecretAccessKey",
            ],
            credentials.GetProperty("secretNames").EnumerateArray().Select(value => value.GetString()));
        Assert.False(credentials.GetProperty("credentialsCommitted").GetBoolean());

        var artifact = Assert.Single(root.GetProperty("artifacts").EnumerateArray());
        Assert.Equal("Gate0.G04.P3.JpegInput.20260825/superseded-initial-harness/logs/inspect-orientation-6.stdout.txt", artifact.GetProperty("logicalArtifactId").GetString());
        Assert.Equal(8, artifact.GetProperty("byteSize").GetInt64());
        Assert.Equal("5E6510D6F9B52E78BE1A51958964211463800E000E3CE278DDEC2480E2A405DC", artifact.GetProperty("sha256").GetString());
        Assert.Equal("objects/sha256/5e/5e6510d6f9b52e78be1a51958964211463800e000e3ce278ddec2480e2a405dc", artifact.GetProperty("r2ObjectKey").GetString());
        Assert.Equal("remote-verified", artifact.GetProperty("retentionStatus").GetString());
        var status = root.GetProperty("status");
        Assert.Equal(1, status.GetProperty("verifiedLogicalArtifactCount").GetInt32());
        Assert.Equal(8, status.GetProperty("verifiedLogicalArtifactBytes").GetInt64());
        Assert.False(status.GetProperty("secondPrivateCopyVerified").GetBoolean());
        Assert.DoesNotMatch(@"[A-Za-z]:\\", text);
        Assert.DoesNotContain("X-Amz-Signature", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OfflineManifestValidationNeedsNeitherCredentialsNorNetwork()
    {
        var script = PathInRepo("eng", "gate0", "Test-Gate0ArtifactManifest.ps1");
        var result = RunPowerShell($"& '{Escape(script)}' | ConvertTo-Json -Compress");

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output.Trim());
        Assert.False(document.RootElement.GetProperty("localByteVerificationPerformed").GetBoolean());
        Assert.False(document.RootElement.GetProperty("remoteByteVerificationPerformed").GetBoolean());
        Assert.Equal(3007, document.RootElement.GetProperty("requiredLogicalArtifactCount").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("recordedRemoteVerifiedLogicalArtifacts").GetInt32());
        Assert.False(document.RootElement.GetProperty("secondPrivateCopyVerified").GetBoolean());
    }

    [Fact]
    public void ObjectKeysAreDeterministicLowercaseSha256Addresses()
    {
        var module = PathInRepo("eng", "gate0", "Gate0ArtifactTools.psm1");
        const string sha256 = "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789";
        var result = RunPowerShell($"Import-Module '{Escape(module)}' -Force; Get-Gate0ObjectKey '{sha256}'");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            "objects/sha256/ab/abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789",
            result.Output.Trim());
    }

    [Theory]
    [InlineData("false-completion")]
    [InlineData("unknown-storage-property")]
    public void OfflineValidationRejectsFalseCompletionAndSchemaExpansion(string mutation)
    {
        var durablePath = PathInRepo("eng", "gate0", "artifact-manifest.json");
        var script = PathInRepo("eng", "gate0", "Test-Gate0ArtifactManifest.ps1");
        var temporary = Path.Combine(Path.GetTempPath(), $"gate0-artifact-manifest-{Guid.NewGuid():N}.json");
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(durablePath))!.AsObject();
            if (mutation == "false-completion")
            {
                var status = root["status"]!.AsObject();
                status["retentionCondition"] = "complete";
                status["secondPrivateCopyVerified"] = true;
                status["verifiedLogicalArtifactCount"] = 3007;
                status["verifiedLogicalArtifactBytes"] = 517_763_820;
                status["blocker"] = null;
            }
            else
            {
                root["storage"]!.AsObject()["endpoint"] = "https://not-approved.invalid/";
            }
            File.WriteAllText(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            var result = RunPowerShell($"& '{Escape(script)}' -ManifestPath '{Escape(temporary)}'");

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(
                mutation == "false-completion" ? "inconsistent with its verified receipt set" : "closed manifest schema",
                result.Output,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    [ReelForge.Tests.WindowsReparsePointFact]
    public void ArtifactRootResolutionRejectsReparsePointsBeforeReadingBytes()
    {
        var module = PathInRepo("eng", "gate0", "Gate0ArtifactTools.psm1");
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "ReelForge-Gate0-R2-Reparse", Guid.NewGuid().ToString("N"));
        var physicalRoot = Path.Combine(temporaryRoot, "physical");
        var linkedRoot = Path.Combine(temporaryRoot, "linked");
        try
        {
            Directory.CreateDirectory(physicalRoot);
            Directory.CreateSymbolicLink(linkedRoot, physicalRoot);

            var result = RunPowerShell($"Import-Module '{Escape(module)}' -Force; Resolve-Gate0ArtifactRoot '{Escape(linkedRoot)}'");

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("reparse point", result.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(linkedRoot)) Directory.Delete(linkedRoot);
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public void PowerShellSurfacePinsDedicatedWindowsCredentialsAndNeverFallsBackToEnvironmentCredentials()
    {
        var module = File.ReadAllText(PathInRepo("eng", "gate0", "Gate0ArtifactTools.psm1"));
        var upload = File.ReadAllText(PathInRepo("eng", "gate0", "Upload-Gate0Artifact.ps1"));
        var download = File.ReadAllText(PathInRepo("eng", "gate0", "Get-Gate0Artifact.ps1"));
        var validate = File.ReadAllText(PathInRepo("eng", "gate0", "Test-Gate0ArtifactManifest.ps1"));

        Assert.Contains("Gate0WindowsCredentialReader]::ReadRequired($name)", module);
        Assert.Contains("CredReadW", File.ReadAllText(PathInRepo("eng", "gate0", "Gate0ArtifactR2Client.cs")));
        Assert.Contains("ReelForge.Engineering.R2.SecretAccessKey", module);
        Assert.DoesNotContain("AWS_ACCESS_KEY_ID", module, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AWS_SECRET_ACCESS_KEY", module, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Get-Secret", module, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("presign", module, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BucketName = $configuration.BucketName", module);
        Assert.DoesNotContain("Configuration = $configuration", module);
        Assert.Contains("Assert-Gate0NoReparsePointAncestors $path $ArtifactRoot", module);
        Assert.Contains("Invoke-Gate0LockedManifestMutation", module);
        Assert.Contains("Invoke-Gate0RemoteByteVerification", upload);
        Assert.Contains("Test-Gate0DownloadedArtifact", download);
        Assert.Contains("-UpdateManifest requires -Remote byte verification", validate);
    }

    [Fact]
    public async Task R2ClientSignsHeadPutAndGetWithoutLeakingSecrets()
    {
        var bytes = "durable gate zero bytes"u8.ToArray();
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes));
        var handler = new ArtifactHandler();
        using var http = new HttpClient(handler);
        var client = new Gate0ArtifactR2Client(
            http,
            new Uri("https://0123456789abcdef0123456789abcdef.r2.cloudflarestorage.com/"),
            "access-id",
            "super-secret-value",
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero)));
        const string key = "objects/sha256/aa/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var source = Path.GetTempFileName();
        var destination = Path.Combine(Path.GetTempPath(), $"gate0-r2-{Guid.NewGuid():N}.bin");
        try
        {
            await File.WriteAllBytesAsync(source, bytes);
            Assert.Null(await client.HeadObjectAsync("reelforge-artifacts", key));
            Assert.True(await client.PutObjectIfAbsentAsync("reelforge-artifacts", key, source, sha256));
            var metadata = await client.HeadObjectAsync("reelforge-artifacts", key);
            Assert.Equal(bytes.Length, metadata!.ContentLength);
            Assert.False(await client.PutObjectIfAbsentAsync("reelforge-artifacts", key, source, sha256));
            await client.DownloadObjectAsync("reelforge-artifacts", key, destination);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(destination));

            Assert.Equal([HttpMethod.Head, HttpMethod.Put, HttpMethod.Head, HttpMethod.Put, HttpMethod.Get], handler.Requests.Select(request => request.Method));
            Assert.All(handler.Requests, request =>
            {
                Assert.StartsWith("AWS4-HMAC-SHA256 Credential=access-id/", request.Authorization, StringComparison.Ordinal);
                Assert.DoesNotContain("super-secret-value", request.Authorization, StringComparison.Ordinal);
                Assert.DoesNotContain("super-secret-value", request.Uri, StringComparison.Ordinal);
                Assert.Equal($"/reelforge-artifacts/{key}", request.Uri);
            });
            Assert.Equal(sha256.ToLowerInvariant(), handler.Requests[1].ContentSha256);
            Assert.Equal("*", handler.Requests[1].IfNoneMatch);
            Assert.Equal("*", handler.Requests[3].IfNoneMatch);
            Assert.Equal(
                "AWS4-HMAC-SHA256 Credential=access-id/20260826/auto/s3/aws4_request, SignedHeaders=host;if-none-match;x-amz-content-sha256;x-amz-date, Signature=5c01a69668fed4ab935e9a95a7d84e3518a7d61f51dd96fea4ed99456c4b7136",
                handler.Requests[1].Authorization);
        }
        finally
        {
            File.Delete(source);
            File.Delete(destination);
        }
    }

    private sealed class ArtifactHandler : HttpMessageHandler
    {
        private byte[]? _bytes;
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri!.AbsolutePath,
                request.Headers.GetValues("Authorization").Single(),
                request.Headers.GetValues("x-amz-content-sha256").Single(),
                request.Headers.IfNoneMatch.SingleOrDefault()?.Tag));
            if (request.Method == HttpMethod.Put)
            {
                if (_bytes is not null && request.Headers.IfNoneMatch.Any(value => value.Tag == "*"))
                    return new HttpResponseMessage(HttpStatusCode.PreconditionFailed) { Content = new ByteArrayContent([]) };
                _bytes = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
            }
            if (_bytes is null) return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new ByteArrayContent([]) };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_bytes) };
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, string Uri, string Authorization, string ContentSha256, string? IfNoneMatch);
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static (int ExitCode, string Output) RunPowerShell(string command)
    {
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(command);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string PathInRepo(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. parts]);
    }
}
