using Microsoft.VisualStudio.TestTools.UnitTesting;
using WAVUtils.Core;
using static WAVUtils.Tests.WaveFixture;

namespace WAVUtils.Tests;

[TestClass]
public sealed class CancellationRegressionTests
{
    [TestMethod]
    public async Task Finding42MergeCancellationLeavesNoOutputAndReleasesInputs()
    {
        using var f = new WaveFixture();
        string a = f.Write("a.wav", new byte[1024 * 1024]), b = f.Write("b.wav", new byte[1024 * 1024]);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAsync<OperationCanceledException>(() => MergeCommand.Run([a, b], outputPath: f.PathOf("out.wav"),
            progress: (value, _) => { if (value > 0.2) cancellation.Cancel(); }, cancellationToken: cancellation.Token));
        Assert.IsFalse(File.Exists(f.PathOf("out.wav")));
        Assert.AreEqual(2, Directory.GetFiles(f.Root).Length);
        using var first = new FileStream(a, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var last = new FileStream(b, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [TestMethod]
    public async Task Finding42SplitCancellationLeavesNoTracksOrTemporaryFiles()
    {
        using var f = new WaveFixture();
        string merged = await MergeCommand.Run([f.Write("a.wav", new byte[1024 * 1024]), f.Write("b.wav", new byte[1024 * 1024])], outputPath: f.PathOf("merged.wav"));
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAsync<OperationCanceledException>(() => SplitCommand.Run(merged, outputDirectory: f.PathOf("out"),
            progress: (value, _) => { if (value > 0.2) cancellation.Cancel(); }, cancellationToken: cancellation.Token));
        Assert.AreEqual(0, Directory.GetFiles(f.PathOf("out")).Length);
        using var unlocked = new FileStream(merged, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [TestMethod]
    public void Finding42TimecodeCancellationKeepsOriginalBytes()
    {
        using var f = new WaveFixture();
        byte[] audio = [.. LtcRegressionTests.Audio(), .. new byte[1024 * 1024]];
        string path = f.Write("ltc.wav", audio, chunks: [new() { ChunkId = "bext", Data = Bext() }]);
        byte[] before = File.ReadAllBytes(path);
        using var cancellation = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => TcCommand.Run([path],
            progress: (value, _) => { if (value > 0.25) cancellation.Cancel(); }, cancellationToken: cancellation.Token));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
        Assert.AreEqual(1, Directory.GetFiles(f.Root).Length);
        using var unlocked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }
}
