using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0G04PlaybackServerIntegrationTests
{
    private static readonly string[] RouteIds = ["Video.Export.Compatibility.Mp4H264Aac.P2OpenH264", "Video.Export.Compatibility.Mp4H264VideoOnly.P2OpenH264", "Video.Export.Open.WebmVp9Opus", "Video.Export.Open.WebmVp9VideoOnly"];
    private static readonly string[] PassedEvents = ["loadedmetadata", "ended"];
    [Fact]
    public async Task ServerServesBoundedCorpusAndRetainsOnlyValidObservations()
    {
        var root = Path.Combine(Path.GetTempPath(), "ReelForge-G04-server-" + Guid.NewGuid().ToString("N"));
        var corpus = Path.Combine(root, "corpus");
        var results = Path.Combine(root, "results");
        Directory.CreateDirectory(corpus);
        Directory.CreateDirectory(Path.Combine(corpus, "transformations"));
        try
        {
            CreateCorpus(corpus);
            var before = Snapshot(corpus);
            await using var server = await Server.StartAsync(corpus, results);
            using var client = new HttpClient { BaseAddress = server.BaseAddress, Timeout = TimeSpan.FromSeconds(10) };

            using var index = await client.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, index.StatusCode);
            Assert.Equal("text/html; charset=utf-8", index.Content.Headers.ContentType?.ToString());

            using var head = new HttpRequestMessage(HttpMethod.Head, "/manifest.json");
            using var headResponse = await client.SendAsync(head);
            Assert.Equal(HttpStatusCode.OK, headResponse.StatusCode);
            Assert.Contains("bytes", headResponse.Headers.AcceptRanges);
            Assert.True(headResponse.Content.Headers.ContentLength > 0);

            using var range = new HttpRequestMessage(HttpMethod.Get, "/manifest.json");
            range.Headers.Range = new RangeHeaderValue(0, 9);
            using var rangeResponse = await client.SendAsync(range);
            Assert.Equal(HttpStatusCode.PartialContent, rangeResponse.StatusCode);
            Assert.Equal("bytes 0-9/" + before["manifest.json"].Length, rangeResponse.Content.Headers.ContentRange?.ToString());
            Assert.Equal(10, (await rangeResponse.Content.ReadAsByteArrayAsync()).Length);

            using var suffix = new HttpRequestMessage(HttpMethod.Get, "/manifest.json");
            suffix.Headers.Range = new RangeHeaderValue(null, 5);
            using var suffixResponse = await client.SendAsync(suffix);
            Assert.Equal(HttpStatusCode.PartialContent, suffixResponse.StatusCode);
            Assert.Equal(5, (await suffixResponse.Content.ReadAsByteArrayAsync()).Length);

            using var invalidRange = new HttpRequestMessage(HttpMethod.Get, "/manifest.json");
            invalidRange.Headers.TryAddWithoutValidation("Range", "bytes=999999-");
            Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, (await client.SendAsync(invalidRange)).StatusCode);
            using var multiRange = new HttpRequestMessage(HttpMethod.Get, "/manifest.json");
            multiRange.Headers.TryAddWithoutValidation("Range", "bytes=0-1,4-5");
            Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, (await client.SendAsync(multiRange)).StatusCode);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync("/", new StringContent("{}"))).StatusCode);
            Assert.StartsWith("HTTP/1.1 404", await RawRequestAsync(server.BaseAddress, "GET /../manifest.json HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n"));

            var valid = Observation("completed-with-inherited-blocked-routes");
            using var validResponse = await client.PostAsync("/results", JsonContent(valid));
            Assert.True(validResponse.IsSuccessStatusCode, await validResponse.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Created, validResponse.StatusCode);
            Assert.Single(Directory.EnumerateFiles(results));
            var envelope = JsonDocument.Parse(await File.ReadAllTextAsync(Directory.EnumerateFiles(results).Single()));
            Assert.Equal("ReelForge Gate 0 retained native playback observation envelope", envelope.RootElement.GetProperty("kind").GetString());
            Assert.Equal("completed-with-inherited-blocked-routes", envelope.RootElement.GetProperty("observation").GetProperty("status").GetString());

            var mismatched = Observation("passed");
            using var mismatchResponse = await client.PostAsync("/results", JsonContent(mismatched));
            Assert.Equal(HttpStatusCode.BadRequest, mismatchResponse.StatusCode);
            var relabeledBlockedRoute = ObservationWithBlockedRouteRelabeledPassed();
            using var relabeledResponse = await client.PostAsync("/results", JsonContent(relabeledBlockedRoute));
            Assert.Equal(HttpStatusCode.BadRequest, relabeledResponse.StatusCode);
            var invalid = new { schemaVersion = 2, kind = "invalid" };
            using var invalidResponse = await client.PostAsync("/results", JsonContent(invalid));
            Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
            Assert.Single(Directory.EnumerateFiles(results));
            Assert.Equal(before, Snapshot(corpus));

            await server.DisposeAsync();
            await using var second = await Server.StartAsync(corpus, Path.Combine(root, "second-results"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static StringContent JsonContent(object value) => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static object Observation(string status)
    {
        var routes = RouteIds
            .Select((id, i) =>
            {
                var bytes = Encoding.UTF8.GetBytes($"{{\"route\":{i + 1},\"status\":\"blocked\"}}\n");
                return (object)new { id, status = "blocked", reason = "Inherited blocked source route for integration proof.", transformation = new { path = $"transformations/{i + 1}.blocked.json", length = (long)bytes.Length, sha256 = Hash(bytes) } };
            }).ToArray();
        return new { schemaVersion = 2, kind = "ReelForge Gate 0 independent-playback manual native HTMLMediaElement observation", startedAt = "2026-08-25T00:00:00.0000000+00:00", userAgent = "integration-test", platform = "Windows", highEntropyIdentity = "integration-test", routes, limitations = "No audible/perceptual sync conclusion.", status };
    }

    private static object ObservationWithBlockedRouteRelabeledPassed()
    {
        var routes = RouteIds
            .Select((id, i) => i == 0
                ? (object)new
                {
                    id,
                    status = "passed",
                    mime = "video/mp4",
                    videoOnly = false,
                    userAgent = "integration-test",
                    platform = "Windows",
                    canPlayType = "maybe",
                    metadata = new { width = 320, height = 180, duration = 5.0 },
                    firstAdvance = 0.25,
                    pauseStable = true,
                    midpointSeek = 2.5,
                    resumeAdvance = 2.75,
                    replayAdvance = 0.25,
                    endedCount = 1,
                    events = PassedEvents
                }
                : BlockedObservationRoute(id, i))
            .ToArray();
        return new { schemaVersion = 2, kind = "ReelForge Gate 0 independent-playback manual native HTMLMediaElement observation", startedAt = "2026-08-25T00:00:00.0000000+00:00", userAgent = "integration-test", platform = "Windows", highEntropyIdentity = "integration-test", routes, limitations = "No audible/perceptual sync conclusion.", status = "completed-with-inherited-blocked-routes" };
    }

    private static object BlockedObservationRoute(string id, int index)
    {
        var bytes = Encoding.UTF8.GetBytes($"{{\"route\":{index + 1},\"status\":\"blocked\"}}\n");
        return new { id, status = "blocked", reason = "Inherited blocked source route for integration proof.", transformation = new { path = $"transformations/{index + 1}.blocked.json", length = (long)bytes.Length, sha256 = Hash(bytes) } };
    }

    private static void CreateCorpus(string root)
    {
        var index = "<html><body>Gate 0 playback test</body></html>";
        File.WriteAllText(Path.Combine(root, "index.html"), index);
        var transformations = new List<object>();
        for (var i = 1; i <= 4; i++)
        {
            var name = $"transformations/{i}.blocked.json";
            var bytes = Encoding.UTF8.GetBytes($"{{\"route\":{i},\"status\":\"blocked\"}}\n");
            File.WriteAllBytes(Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)), bytes);
            transformations.Add(new { path = name, length = (long)bytes.Length, sha256 = Hash(bytes) });
        }
        var ids = RouteIds;
        var blocked = ids.Select((id, i) => new { id, status = "blocked", reason = "Inherited blocked source route for integration proof.", transformation = transformations[i] }).ToArray();
        var manifest = new { schemaVersion = 2, kind = "ReelForge Gate 0 independent-playback manual harness corpus", dispositionSummary = new { availableRouteCount = 0, blockedRouteCount = 4 }, routes = Array.Empty<object>(), blockedRoutes = blocked, limitations = "Synthetic blocked corpus for server protocol integration tests." };
        var manifestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest));
        File.WriteAllBytes(Path.Combine(root, "manifest.json"), manifestBytes);
        var evidence = new { schemaVersion = 2, profileId = "P2.BtbnLgplShared.WindowsX64.20260820", preflight = new { status = "passed" }, manifest = Binding("manifest.json", manifestBytes), indexHtml = Binding("index.html", Encoding.UTF8.GetBytes(index)), boundArtifacts = transformations.Prepend(Binding("index.html", Encoding.UTF8.GetBytes(index))).ToArray() };
        File.WriteAllText(Path.Combine(root, "g0.4-playback-corpus-evidence.json"), JsonSerializer.Serialize(evidence));
    }

    private static object Binding(string path, byte[] bytes) => new { path, length = (long)bytes.Length, sha256 = Hash(bytes) };
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static Dictionary<string, (long Length, string Hash)> Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(p => Path.GetRelativePath(root, p).Replace('\\', '/'), p => (new FileInfo(p).Length, Hash(File.ReadAllBytes(p))));
    private static async Task<string> RawRequestAsync(Uri address, string request)
    {
        using var tcp = new System.Net.Sockets.TcpClient();
        await tcp.ConnectAsync(address.Host, address.Port);
        await using var stream = tcp.GetStream();
        var bytes = Encoding.ASCII.GetBytes(request);
        await stream.WriteAsync(bytes);
        using var reader = new StreamReader(stream, Encoding.ASCII);
        return await reader.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }
    private static string RepoPath(params string[] parts) => Path.Combine([PathInRepo(), .. parts]);
    private static string PathInRepo()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent;
        return directory!.FullName;
    }

    private sealed class Server : IAsyncDisposable
    {
        private readonly Process process;
        private bool disposed;
        public Uri BaseAddress { get; }
        private Server(Process process, Uri address) { this.process = process; BaseAddress = address; }
        public static async Task<Server> StartAsync(string corpus, string results)
        {
            var info = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-File"); info.ArgumentList.Add(RepoPath("eng", "gate0", "Start-G04PlaybackHarnessServer.ps1"));
            info.ArgumentList.Add("-CorpusRoot"); info.ArgumentList.Add(corpus); info.ArgumentList.Add("-ResultDirectory"); info.ArgumentList.Add(results); info.ArgumentList.Add("-Port"); info.ArgumentList.Add("0");
            var process = Process.Start(info)!;
            var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var match = System.Text.RegularExpressions.Regex.Match(line ?? "", @"http://127\.0\.0\.1:(\d+)/");
            if (!match.Success) { process.Kill(true); throw new InvalidOperationException($"Server did not report a port: {line} {await process.StandardError.ReadToEndAsync()}"); }
            return new Server(process, new Uri($"http://127.0.0.1:{match.Groups[1].Value}/"));
        }
        public async ValueTask DisposeAsync()
        {
            if (disposed) return;
            disposed = true;
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            process.Dispose();
        }
    }
}
