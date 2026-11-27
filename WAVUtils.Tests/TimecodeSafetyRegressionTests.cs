using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WAVUtils.Core;
using static WAVUtils.Tests.WaveFixture;

namespace WAVUtils.Tests;

[TestClass]
public sealed class TimecodeSafetyRegressionTests
{
    [TestMethod]
    public void Finding44ReplacementReallyRollsBackAndPreservesAConcurrentEdit()
    {
        using var f = new WaveFixture();
        string path = f.Write("ltc.wav", LtcRegressionTests.Audio(), chunks: [new() { ChunkId = "bext", Data = Bext() }]);
        byte[] original = File.ReadAllBytes(path), concurrent = original.ToArray();
        concurrent[45] ^= 1;
        using var plan = (IDisposable)Invoke("BuildPlan", path, null, null, CancellationToken.None)!;
        ReplaceSource(plan, path, () => File.WriteAllBytes(path, concurrent));
        Assert.ThrowsExactly<IOException>(() => Invoke("Apply", plan, null, CancellationToken.None));
        CollectionAssert.AreEqual(concurrent, File.ReadAllBytes(path), "Rollback must preserve the competing writer's bytes.");
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path + ".bak"));
        Assert.AreEqual(2, Directory.GetFiles(f.Root).Length);
        using var unlocked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [TestMethod]
    public void Finding42CancellationAfterReplacementRollsBackInsteadOfHashingUninterruptibly()
    {
        using var f = new WaveFixture();
        string path = f.Write("ltc.wav", LtcRegressionTests.Audio(), chunks: [new() { ChunkId = "bext", Data = Bext() }]);
        byte[] original = File.ReadAllBytes(path);
        using var cancellation = new CancellationTokenSource();
        using var plan = (IDisposable)Invoke("BuildPlan", path, null, null, cancellation.Token)!;
        ReplaceSource(plan, path, cancellation.Cancel);
        Assert.Throws<OperationCanceledException>(() => Invoke("Apply", plan, null, cancellation.Token));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path + ".bak"));
        Assert.AreEqual(2, Directory.GetFiles(f.Root).Length);
        using var unlocked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [TestMethod]
    [DataRow("version")]
    [DataRow("rate")]
    [DataRow("digest")]
    [DataRow("bext")]
    [DataRow("duplicate")]
    public void Finding40UntrustedProvenanceCannotChooseARecorderClock(string fault)
    {
        using var f = new WaveFixture();
        byte[] audio = LtcRegressionTests.Audio();
        string path = f.Write("ltc.wav", audio, chunks: [new() { ChunkId = "bext", Data = Bext() }]);
        TcCommand.Run([path]);
        var chunks = Chunks(path).Where(pair => pair.Key is not ("fmt " or "data" or "JUNK"))
            .Select(pair => new WavChunkData { ChunkId = pair.Key, Data = pair.Value }).ToList();
        var provenance = chunks.Single(chunk => chunk.ChunkId == "wtcp");
        var state = JsonNode.Parse(provenance.Data)!;
        if (fault == "version") state["version"] = 2;
        if (fault == "rate") state["sampleRate"] = 44100;
        if (fault == "digest") state["originalSha256"] = "not a digest";
        if (fault == "bext") chunks.Single(chunk => chunk.ChunkId == "bext").Data[338] ^= 1;
        if (fault == "duplicate") chunks.Add(new() { ChunkId = "WTCP", Data = provenance.Data });
        provenance.Data = System.Text.Encoding.UTF8.GetBytes(state.ToJsonString());
        f.Write("ltc.wav", audio, chunks: chunks.ToArray());
        byte[] before = File.ReadAllBytes(path), backup = File.ReadAllBytes(path + ".bak");
        Assert.ThrowsExactly<InvalidDataException>(() => TcCommand.Run([path]));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
        CollectionAssert.AreEqual(backup, File.ReadAllBytes(path + ".bak"));
        Assert.AreEqual(2, Directory.GetFiles(f.Root).Length);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Finding40StoredOriginalClockMustMatchTheBackup(bool useAsDonor)
    {
        using var f = new WaveFixture();
        byte[] audio = LtcRegressionTests.Audio();
        string stamped = f.Write("stamped.wav", audio, chunks: [new() { ChunkId = "bext", Data = Bext() }]);
        string clean = f.Write("clean.wav", audio, chunks: [new() { ChunkId = "bext", Data = Bext() }]);
        TcCommand.Run([stamped]);
        var chunks = Chunks(stamped).Where(pair => pair.Key is not ("fmt " or "data" or "JUNK"))
            .Select(pair => new WavChunkData { ChunkId = pair.Key, Data = pair.Value }).ToArray();
        var provenance = chunks.Single(chunk => chunk.ChunkId == "wtcp");
        var state = JsonNode.Parse(provenance.Data)!;
        byte[] embedded = Convert.FromBase64String(state["originalBext"]!.GetValue<string>());
        BitConverter.GetBytes(172800000L + 48000).CopyTo(embedded, 338);
        state["originalBext"] = Convert.ToBase64String(embedded);
        provenance.Data = System.Text.Encoding.UTF8.GetBytes(state.ToJsonString());
        f.Write("stamped.wav", audio, chunks: chunks);

        byte[] before = File.ReadAllBytes(stamped), cleanBefore = File.ReadAllBytes(clean);
        Assert.AreEqual(172848000L, BitConverter.ToInt64(embedded, 338));
        Assert.AreEqual(172800000L, BitConverter.ToInt64(Chunks(stamped + ".bak")["bext"], 338));
        Assert.ThrowsExactly<InvalidDataException>(() => useAsDonor
            ? TcCommand.Run([clean], tcSource: stamped)
            : TcCommand.Run([stamped], tcSource: clean));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(stamped));
        CollectionAssert.AreEqual(cleanBefore, File.ReadAllBytes(clean));
    }

    [TestMethod]
    [DataRow("changed target")]
    [DataRow("unchanged target")]
    [DataRow("donor")]
    public void Finding40MissingOriginalBackupPreventsUseOfStoredProvenance(string use)
    {
        using var f = new WaveFixture();
        string target = f.Write("target.wav", LtcRegressionTests.Audio(), chunks: [new() { ChunkId = "bext", Data = Bext() }]);
        string donor = f.Write("donor.wav", LtcRegressionTests.Audio(), chunks: [new() { ChunkId = "bext", Data = Bext(timeReference: 0) }]);
        TcCommand.Run([target]);
        File.Delete(target + ".bak");
        byte[] before = File.ReadAllBytes(target);
        byte[] donorBefore = File.ReadAllBytes(donor);
        Assert.IsFalse(File.Exists(target + ".bak"));
        Assert.Throws<IOException>(() => use switch
        {
            "changed target" => TcCommand.Run([target], tcSource: donor),
            "unchanged target" => TcCommand.Run([target]),
            _ => TcCommand.Run([donor], tcSource: target)
        });
        CollectionAssert.AreEqual(before, File.ReadAllBytes(target));
        CollectionAssert.AreEqual(donorBefore, File.ReadAllBytes(donor));
        Assert.AreEqual(2, Directory.GetFiles(f.Root).Length);
    }

    [TestMethod]
    public void Finding40ProvenanceIdentifierDoesNotBecomeDuplicatedAfterAnotherStamp()
    {
        using var f = new WaveFixture();
        byte[] audio = LtcRegressionTests.Audio();
        string target = f.Write("target.wav", audio, chunks: [new() { ChunkId = "bext", Data = Bext() }]);
        string donor = f.Write("donor.wav", audio, chunks: [new() { ChunkId = "bext", Data = Bext(timeReference: 0) }]);
        TcCommand.Run([target]);
        var chunks = Chunks(target).Where(pair => pair.Key is not ("fmt " or "data" or "JUNK"))
            .Select(pair => new WavChunkData { ChunkId = pair.Key == "wtcp" ? "WTCP" : pair.Key, Data = pair.Value }).ToArray();
        f.Write("target.wav", audio, chunks: chunks);
        TcCommand.Run([target], tcSource: donor);
        Assert.AreEqual(1, Chunks(target).Keys.Count(id => id.Equals("wtcp", StringComparison.OrdinalIgnoreCase)));
        byte[] once = File.ReadAllBytes(target);
        TcCommand.Run([target], tcSource: donor);
        CollectionAssert.AreEqual(once, File.ReadAllBytes(target));
    }

    private static void ReplaceSource(IDisposable plan, string path, Action afterClose)
    {
        var property = plan.GetType().GetProperty("Source")!;
        ((FileStream)property.GetValue(plan)!).Dispose();
        // Models the narrow real race between closing the protected source handle and File.Replace.
        property.SetValue(plan, new ClosingStream(path, afterClose));
    }

    private static object? Invoke(string method, params object?[] arguments)
    {
        try { return typeof(TcCommand).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, arguments); }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private sealed class ClosingStream(string path, Action afterClose) : FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
    {
        private bool _closed;
        protected override void Dispose(bool disposing)
        {
            bool notify = disposing && !_closed;
            _closed = true;
            base.Dispose(disposing);
            if (notify) afterClose();
        }
    }
}
