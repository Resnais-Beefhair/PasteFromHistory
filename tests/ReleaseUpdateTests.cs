using PasteFromHistory.Services;

namespace PasteFromHistory.Tests;

internal static class ReleaseUpdateTests
{
    internal static void Run(Action<bool, string> check)
    {
        var release = ReleaseUpdateService.Parse("{\"tag_name\":\"v1.10.0\",\"draft\":false,\"prerelease\":false,\"html_url\":\"https://example.org/untrusted\"}");
        check(release.Version > new Version(1, 3, 0), "Update comparison uses numeric version components");
        check(release.Url == "https://github.com/Resnais-Beefhair/PasteFromHistory/releases/tag/v1.10.0",
            "Update navigation stays in the trusted repository regardless of response URL");
        foreach (var json in new[] { "{}", "[]", "{\"tag_name\":\"v1.3.0\",\"draft\":true,\"prerelease\":false}",
            "{\"tag_name\":\"v1.3.0\",\"draft\":false,\"prerelease\":true}",
            "{\"tag_name\":\"v1.3.0/../../elsewhere\",\"draft\":false,\"prerelease\":false}",
            "{\"tag_name\":\"v1.3.0-beta\",\"draft\":false,\"prerelease\":false}" })
        {
            var rejected = false;
            try { ReleaseUpdateService.Parse(json); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "Invalid, draft or preview update response rejected");
        }
    }
}
