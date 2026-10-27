using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PasteFromHistory.Services;

internal sealed record ReleaseUpdate(Version Version, string Url);

internal static class ReleaseUpdateService
{
    private const string Repository = "https://github.com/Resnais-Beefhair/PasteFromHistory";
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 1_000_000 };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PasteFromHistory/1.3");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    // Invoked only by the explicit Settings > About action, never on capture/startup.
    internal static async Task<ReleaseUpdate> CheckAsync()
    {
        using var response = await Client.GetAsync("https://api.github.com/repos/Resnais-Beefhair/PasteFromHistory/releases/latest");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return Parse(json);
    }

    internal static ReleaseUpdate Parse(string json)
    {
        if (json.Length > 1_000_000) throw new InvalidDataException("Release response is too large.");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False
            || !root.TryGetProperty("prerelease", out var preview) || preview.ValueKind != JsonValueKind.False
            || !root.TryGetProperty("tag_name", out var tagValue) || tagValue.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("A stable release was not returned.");
        var tag = tagValue.GetString()!;
        if (tag.Length > 32 || !Regex.IsMatch(tag, @"\Av(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)\z", RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100)) || !Version.TryParse(tag[1..], out var version))
            throw new InvalidDataException("The release version is invalid.");
        // Never launch a response-supplied URL. The destination stays in our repo.
        return new(version, Repository + "/releases/tag/" + tag);
    }
}
