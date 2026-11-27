using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WAVUtils.Core;

namespace WAVUtils.Tests;

[TestClass]
public sealed class UpdateRegressionTests
{
    [TestMethod]
    public void Finding50OnlyTheVersionedVerifiedInstallerIsSelected()
    {
        const string json = """
            {"tag_name":"v2.0.0","assets":[
              {"name":"portable.zip","browser_download_url":"https://example.com/portable.zip","size":100},
              {"name":"WAVUtils-Setup-2.0.0.exe","browser_download_url":"https://github.com/wtfsoftware/WAVUtils/releases/download/v2.0.0/WAVUtils-Setup-2.0.0.exe","size":100,
               "digest":"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}]}
            """;
        object release = Release(json);
        Assert.AreEqual("https://github.com/wtfsoftware/WAVUtils/releases/download/v2.0.0/WAVUtils-Setup-2.0.0.exe", Call(release, "GetInstallerDownloadUrl"));
        Assert.AreEqual(100L, Call(release, "GetInstallerSize"));
    }

    [TestMethod]
    [DataRow("https://example.com/setup.exe", "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [DataRow("https://github.com/wtfsoftware/WAVUtils/releases/download/v1.0.0/WAVUtils-Setup-1.0.0.exe", "")]
    [DataRow("https://github.com/wtfsoftware/WAVUtils/releases/download/v0.9.0/WAVUtils-Setup-0.9.0.exe", "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [DataRow("https://github.com/wtfsoftware/WAVUtils/releases/download/v1.0.0/other.exe", "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Finding50UntrustedOrUnverifiableAssetsAreNotInstallers(string url, string digest)
    {
        object release = Release(JsonSerializer.Serialize(new { tag_name = "v1.0.0", assets = new[] {
            new { name = "WAVUtils-Setup-1.0.0.exe", browser_download_url = url, size = 100, digest } } }));
        Assert.AreEqual("", Call(release, "GetInstallerDownloadUrl"));
    }

    [TestMethod]
    [DataRow("1.2.3", "v1.2.3.0", false)]
    [DataRow("1.2.3.0", "v1.2.3", false)]
    [DataRow("1.2", "v1.2.0.0", false)]
    [DataRow("1.2.3", "v1.2.3.1", true)]
    [DataRow("1.2.3.1", "v1.2.3", false)]
    [DataRow("1.2.9.0", "v1.2.10", true)]
    [DataRow("1.2.3.0", "v1.2.4-preview", false)]
    [DataRow(null, "v1.2.3", false)]
    public void Finding69OnlyStrictlyNewerNumericVersionsOfferUpdates(string? current, string latest, bool expected) =>
        Assert.AreEqual(expected, UpdateChecker.IsNewerVersion(current: current, latest: latest));

    private static object Release(string json) => JsonSerializer.Deserialize(json,
        typeof(UpdateChecker).GetNestedType("GithubRelease", BindingFlags.NonPublic)!)!;

    private static object? Call(object value, string method) => value.GetType().GetMethod(method)!.Invoke(value, null);
}
