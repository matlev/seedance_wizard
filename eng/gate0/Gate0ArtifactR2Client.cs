#nullable enable

using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ReelForge.Gate0.Artifacts;

public sealed record Gate0R2ObjectMetadata(long? ContentLength, string? ETag);

public static class Gate0WindowsCredentialReader
{
    private const int CredentialTypeGeneric = 1;
    private const int ErrorNotFound = 1168;

    public static string ReadRequired(string targetName)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Gate 0 R2 credentials require Windows Credential Manager.");
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        if (targetName.IndexOfAny(['\r', '\n', '\0']) >= 0) throw new ArgumentException("Credential target contains invalid characters.", nameof(targetName));
        if (!CredRead(targetName, CredentialTypeGeneric, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound) throw new InvalidOperationException($"Required Windows Generic Credential is unavailable: {targetName}");
            throw new Win32Exception(error, "Windows Credential Manager could not read a required Gate 0 credential.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                throw new InvalidOperationException($"Required Windows Generic Credential is empty: {targetName}");
            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            try
            {
                var value = Encoding.Unicode.GetString(bytes);
                if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"Required Windows Generic Credential is empty: {targetName}");
                return value;
            }
            finally
            {
                Array.Clear(bytes);
            }
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPointer);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credentialPointer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public int Type;
        public string? TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }
}

public sealed class Gate0ArtifactR2Client
{
    private const string Region = "auto";
    private const string Service = "s3";
    private static readonly string EmptyPayloadHash = Hex(SHA256.HashData([]));

    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly string _accessKeyId;
    private readonly string _secretAccessKey;
    private readonly TimeProvider _timeProvider;

    public Gate0ArtifactR2Client(
        HttpClient httpClient,
        Uri endpoint,
        string accessKeyId,
        string secretAccessKey,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.UserInfo) || endpoint.AbsolutePath != "/")
            throw new ArgumentException("R2 endpoint must be a credential-free HTTPS origin.", nameof(endpoint));
        ArgumentException.ThrowIfNullOrWhiteSpace(accessKeyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretAccessKey);

        _httpClient = httpClient;
        _endpoint = new Uri(endpoint.GetLeftPart(UriPartial.Authority).TrimEnd('/') + "/", UriKind.Absolute);
        _accessKeyId = accessKeyId;
        _secretAccessKey = secretAccessKey;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task ProbeBucketAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        using var response = await SendSignedAsync(
            HttpMethod.Head,
            BucketUri(bucketName),
            EmptyPayloadHash,
            null,
            createOnly: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, "access the artifact bucket");
    }

    public async Task<Gate0R2ObjectMetadata?> HeadObjectAsync(
        string bucketName,
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendSignedAsync(
            HttpMethod.Head,
            ObjectUri(bucketName, objectKey),
            EmptyPayloadHash,
            null,
            createOnly: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        EnsureSuccess(response, "inspect an artifact object");
        return new Gate0R2ObjectMetadata(response.Content.Headers.ContentLength, response.Headers.ETag?.Tag);
    }

    public async Task<bool> PutObjectIfAbsentAsync(
        string bucketName,
        string objectKey,
        string filePath,
        string contentSha256,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 128,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await SendSignedAsync(
            HttpMethod.Put,
            ObjectUri(bucketName, objectKey),
            NormalizeSha256(contentSha256),
            content,
            createOnly: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.PreconditionFailed) return false;
        EnsureSuccess(response, "upload an artifact object");
        return true;
    }

    public async Task DownloadObjectAsync(
        string bucketName,
        string objectKey,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        if (File.Exists(destinationPath)) throw new IOException("Artifact download destination must not already exist.");
        using var response = await SendSignedAsync(
            HttpMethod.Get,
            ObjectUri(bucketName, objectKey),
            EmptyPayloadHash,
            null,
            createOnly: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, "download an artifact object");

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            1024 * 128,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendSignedAsync(
        HttpMethod method,
        Uri uri,
        string payloadHash,
        HttpContent? content,
        bool createOnly,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var timestamp = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var date = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var scope = $"{date}/{Region}/{Service}/aws4_request";
        var canonicalHeaders = createOnly
            ? $"host:{HostHeader(uri)}\nif-none-match:*\nx-amz-content-sha256:{payloadHash}\nx-amz-date:{timestamp}\n"
            : $"host:{HostHeader(uri)}\nx-amz-content-sha256:{payloadHash}\nx-amz-date:{timestamp}\n";
        var signedHeaders = createOnly
            ? "host;if-none-match;x-amz-content-sha256;x-amz-date"
            : "host;x-amz-content-sha256;x-amz-date";
        var canonicalRequest = $"{method.Method}\n{uri.AbsolutePath}\n\n{canonicalHeaders}\n{signedHeaders}\n{payloadHash}";
        var signature = Hex(Hmac(SigningKey(date), StringToSign(timestamp, scope, canonicalRequest)));

        using var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.Host = HostHeader(uri);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        request.Headers.TryAddWithoutValidation("x-amz-date", timestamp);
        if (createOnly) request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        request.Headers.TryAddWithoutValidation(
            "Authorization",
            $"AWS4-HMAC-SHA256 Credential={_accessKeyId}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}");
        var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        request.Content = null;
        return response;
    }

    private static void EnsureSuccess(HttpResponseMessage response, string operation)
    {
        if (response.IsSuccessStatusCode) return;
        throw new HttpRequestException(
            $"Cloudflare R2 could not {operation} (HTTP {(int)response.StatusCode}).",
            null,
            response.StatusCode);
    }

    private Uri BucketUri(string bucketName) =>
        new($"{_endpoint.GetLeftPart(UriPartial.Authority)}/{EncodeSegment(bucketName)}", UriKind.Absolute);

    private Uri ObjectUri(string bucketName, string objectKey) =>
        new($"{_endpoint.GetLeftPart(UriPartial.Authority)}/{EncodeSegment(bucketName)}/{EncodePath(objectKey)}", UriKind.Absolute);

    private static string NormalizeSha256(string value)
    {
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Content SHA-256 must contain exactly 64 hexadecimal characters.", nameof(value));
        return value.ToLowerInvariant();
    }

    private static string EncodePath(string value) => string.Join('/', value.Split('/').Select(EncodeSegment));
    private static string EncodeSegment(string value) => Uri.EscapeDataString(value);
    private static string HostHeader(Uri uri) => uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
    private static string StringToSign(string timestamp, string scope, string canonicalRequest) =>
        $"AWS4-HMAC-SHA256\n{timestamp}\n{scope}\n{Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)))}";

    private byte[] SigningKey(string date)
    {
        var dateKey = Hmac(Encoding.UTF8.GetBytes("AWS4" + _secretAccessKey), date);
        var regionKey = Hmac(dateKey, Region);
        var serviceKey = Hmac(regionKey, Service);
        return Hmac(serviceKey, "aws4_request");
    }

    private static byte[] Hmac(byte[] key, string value) =>
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value));

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
