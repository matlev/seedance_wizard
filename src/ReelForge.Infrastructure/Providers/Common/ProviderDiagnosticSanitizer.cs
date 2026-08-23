using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ReelForge.Infrastructure;

internal static class ProviderDiagnosticSanitizer
{
    private static readonly string[] SensitiveNameFragments =
        ["authorization", "api_key", "apikey", "access_key", "secret", "password", "token"];
    private static readonly Regex DataUrlRegex = new(
        @"data:[^,\s]+;base64,[A-Za-z0-9+/=]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex HttpUrlRegex = new(
        @"https?://[^\s\""'<>]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SensitiveAssignmentRegex = new(
        @"(?<name>authorization|api[_-]?key|access[_-]?key|secret|password|token)\s*[:=]\s*(?<value>[^\s,;]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string SanitizeJsonOrText(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;

        try
        {
            var node = JsonNode.Parse(value);
            if (node is null) return value;
            SanitizeNode(node);
            return node.ToJsonString();
        }
        catch (JsonException)
        {
            return SanitizeText(value);
        }
    }

    private static void SanitizeNode(JsonNode node)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var pair in jsonObject.ToList())
            {
                if (pair.Value is null) continue;
                if (IsSensitiveName(pair.Key))
                {
                    jsonObject[pair.Key] = "[redacted]";
                    continue;
                }

                SanitizeNode(pair.Value);
            }

            return;
        }

        if (node is JsonArray jsonArray)
        {
            foreach (var item in jsonArray)
            {
                if (item is not null) SanitizeNode(item);
            }

            return;
        }

        if (node is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var text)) return;
        if (text.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            jsonValue.ReplaceWith($"[inline data omitted; {text.Length} characters]");
            return;
        }

        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var safeUri = new UriBuilder(uri) { Query = string.Empty, Fragment = string.Empty }.Uri;
            jsonValue.ReplaceWith(safeUri.AbsoluteUri);
        }
    }

    private static bool IsSensitiveName(string name) =>
        SensitiveNameFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static string SanitizeText(string value)
    {
        var sanitized = DataUrlRegex.Replace(
            value,
            match => $"[inline data omitted; {match.Length} characters]");
        sanitized = HttpUrlRegex.Replace(sanitized, match =>
        {
            if (!Uri.TryCreate(match.Value, UriKind.Absolute, out var uri)) return match.Value;
            return new UriBuilder(uri) { Query = string.Empty, Fragment = string.Empty }.Uri.AbsoluteUri;
        });
        return SensitiveAssignmentRegex.Replace(sanitized, "${name}=[redacted]");
    }
}
