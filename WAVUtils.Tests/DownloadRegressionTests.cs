using System.Buffers.Binary;
using System.ComponentModel;
using System.Net;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WAVUtils.Core;

namespace WAVUtils.Tests;

[TestClass]
public sealed class DownloadRegressionTests
{
    [TestMethod]
    public async Task Finding50VerifiedDownloadIsClosedAndMatchesTheReleaseDigest()
    {
        using var f = new WaveFixture();
        byte[] bytes = PeBytes();
        using var handler = new FixtureHandler(bytes);
        using var client = new HttpClient(handler);
        var samples = new List<double>();
        var installer = await UpdateChecker.DownloadInstallerAsync(Release(bytes), client, f.Root,
            (value, _) => samples.Add(value), CancellationToken.None);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(installer.Path));
        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(bytes)), installer.Sha256);
        Assert.AreEqual(1d, samples[^1], 0.000001);
        using var unlocked = new FileStream(installer.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.IsTrue(handler.Source.Disposed);
    }

    [TestMethod]
    [DataRow("2.0.0", "v2.0.0/other.exe")]
    [DataRow("2.0.0", "v2.0.0/WAVUtils-Setup-1.0.0.exe")]
    [DataRow("2.0.0", "v1.0.0/WAVUtils-Setup-2.0.0.exe")]
    [DataRow("1.0.0", "v2.0.0/WAVUtils-Setup-2.0.0.exe")]
    [DataRow(null, "v2.0.0/WAVUtils-Setup-2.0.0.exe")]
    public async Task Finding61DownloadRequiresTheExactVersionedSetupBeforeSendingARequest(string? version, string assetPath)
    {
        using var f = new WaveFixture();
        byte[] bytes = PeBytes();
        using var handler = new FixtureHandler(bytes);
        using var client = new HttpClient(handler);
        var update = Release(bytes);
        string url = "https://github.com/wtfsoftware/WAVUtils/releases/download/" + assetPath;
        Assert.IsTrue(update.DownloadUrl != url || update.LatestVersion != version, "The release fixture must be changed.");
        update.DownloadUrl = url;
        update.LatestVersion = version;

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            UpdateChecker.DownloadInstallerAsync(update, client, f.Root, null, CancellationToken.None));
        Assert.AreEqual(0, handler.Requests);
        Assert.AreEqual(0, Directory.GetFiles(f.Root).Length);
    }

    [TestMethod]
    [DataRow("v2.0.0")]
    [DataRow("2.0.0")]
    public async Task Finding61DownloadKeepsTheValidatedReleaseWhileReportingProgress(string tag)
    {
        using var f = new WaveFixture();
        byte[] bytes = PeBytes();
        using var handler = new FixtureHandler(bytes);
        using var client = new HttpClient(handler);
        var update = Release(bytes);
        update.DownloadUrl = $"https://github.com/wtfsoftware/WAVUtils/releases/download/{tag}/WAVUtils-Setup-2.0.0.exe";
        string digest = update.Sha256!;
        bool progressReported = false;
        var installer = await UpdateChecker.DownloadInstallerAsync(update, client, f.Root, (value, _) =>
        {
            if (value >= 1) return;
            progressReported = true;
            update.FileSize = bytes.LongLength + 1;
            update.Sha256 = null;
        }, CancellationToken.None);

        Assert.IsTrue(progressReported);
        Assert.AreEqual(1, handler.Requests);
        Assert.AreEqual(digest, installer.Sha256);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(installer.Path));
    }

    [TestMethod]
    [DataRow("digest")]
    [DataRow("truncated")]
    [DataRow("oversized")]
    [DataRow("not-pe")]
    [DataRow("http")]
    public async Task Finding50UnsafeOrFailedDownloadsLeaveNoFile(string fault)
    {
        using var f = new WaveFixture();
        byte[] expected = PeBytes(), actual = expected.ToArray();
        if (fault == "digest") actual[^1] ^= 1;
        if (fault == "truncated") actual = actual[..^1];
        if (fault == "oversized") actual = [.. actual, 0];
        if (fault == "not-pe") expected = actual = new byte[4096];
        using var handler = new FixtureHandler(actual, success: fault != "http");
        using var client = new HttpClient(handler);
        if (fault == "http")
            await Assert.ThrowsAsync<HttpRequestException>(() => UpdateChecker.DownloadInstallerAsync(Release(expected), client, f.Root, null, CancellationToken.None));
        else
            await Assert.ThrowsAsync<InvalidDataException>(() => UpdateChecker.DownloadInstallerAsync(Release(expected), client, f.Root, null, CancellationToken.None));
        Assert.AreEqual(0, Directory.GetFiles(f.Root).Length);
        Assert.IsTrue(handler.Source.Disposed);
    }

    [TestMethod]
    public async Task Finding50DownloadCancellationClosesTheSourceAndDeletesTheTemporaryFile()
    {
        using var f = new WaveFixture();
        byte[] bytes = PeBytes();
        using var handler = new FixtureHandler(bytes);
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAsync<OperationCanceledException>(() => UpdateChecker.DownloadInstallerAsync(Release(bytes), client, f.Root,
            (_, _) => cancellation.Cancel(), cancellation.Token));
        Assert.AreEqual(0, Directory.GetFiles(f.Root).Length);
        Assert.IsTrue(handler.Source.Disposed);
    }

    [TestMethod]
    public async Task Finding50FinalProgressFailureDoesNotLeakAVerifiedDownload()
    {
        using var f = new WaveFixture();
        byte[] bytes = PeBytes();
        using var handler = new FixtureHandler(bytes);
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => UpdateChecker.DownloadInstallerAsync(Release(bytes), client, f.Root,
            (value, _) => { if (value == 1) throw new InvalidOperationException("Observer failed."); }, CancellationToken.None));
        Assert.AreEqual(0, Directory.GetFiles(f.Root).Length);
    }

    [TestMethod]
    public async Task Finding50CancellationAtCompletionDoesNotReturnAnInstaller()
    {
        using var f = new WaveFixture();
        byte[] bytes = PeBytes();
        using var handler = new FixtureHandler(bytes);
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAsync<OperationCanceledException>(() => UpdateChecker.DownloadInstallerAsync(Release(bytes), client, f.Root,
            (value, _) => { if (value == 1) cancellation.Cancel(); }, cancellation.Token));
        Assert.AreEqual(0, Directory.GetFiles(f.Root).Length);
    }

    [TestMethod]
    public void Finding68LeavesTheDestinationToSetup()
    {
        using var f = new WaveFixture();
        byte[] bytes = PeBytes();
        var installer = new DownloadedInstaller { Path = f.PathOf("fixture.exe"), Sha256 = Release(bytes).Sha256! };
        File.WriteAllBytes(installer.Path, bytes);
        string[]? arguments = null;

        _ = UpdateChecker.RunInstaller(installer, start: info =>
        {
            arguments = [.. info.ArgumentList];
            return null;
        });

        Assert.IsNotNull(arguments, "The fixture must reach the simulated Setup launch.");
        CollectionAssert.AreEqual(new[] { "/NORESTART" }, arguments);
    }

    [TestMethod]
    [DataRow(1223, 0, false)] // ERROR_CANCELLED, including declining the UAC prompt.
    [DataRow(5, 1, true)] // ERROR_ACCESS_DENIED is a launch failure.
    public void Finding58DecliningElevationDoesNotOfferManualInstallation(int nativeError, int exitCode, bool manualExpected)
    {
        using var f = new WaveFixture();
        byte[] bytes = PeBytes();
        var installer = new DownloadedInstaller { Path = f.PathOf("fixture.exe"), Sha256 = Release(bytes).Sha256! };
        File.WriteAllBytes(installer.Path, bytes);
        bool requestedElevation = false, manual = false;

        int result = WAVUtils.Tray.Program.FinishUpdate(installer, file => UpdateChecker.RunInstaller(file, start =>
        {
            requestedElevation = start.UseShellExecute && start.Verb == "runas" && start.FileName == installer.Path;
            throw new Win32Exception(nativeError);
        }), () => manual = true);

        Assert.IsTrue(requestedElevation, "The fixture must reach the simulated platform launch.");
        Assert.IsFalse(File.Exists(installer.Path));
        Assert.AreEqual(exitCode, result);
        Assert.AreEqual(manualExpected, manual);
    }

    internal static byte[] PeBytes()
    {
        byte[] bytes = new byte[4096];
        "MZ"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(60), 128);
        "PE\0\0"u8.CopyTo(bytes.AsSpan(128));
        return bytes;
    }

    internal static UpdateInfo Release(byte[] bytes) => new()
    {
        LatestVersion = "2.0.0", FileSize = bytes.LongLength,
        DownloadUrl = "https://github.com/wtfsoftware/WAVUtils/releases/download/v2.0.0/WAVUtils-Setup-2.0.0.exe",
        Sha256 = Convert.ToHexString(SHA256.HashData(bytes))
    };

    private sealed class ObservedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class FixtureHandler(byte[] bytes, bool success = true) : HttpMessageHandler
    {
        public ObservedStream Source { get; } = new(bytes);
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(success ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable)
            { Content = new StreamContent(Source) });
        }
    }
}
