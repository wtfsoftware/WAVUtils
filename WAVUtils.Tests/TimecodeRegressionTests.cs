using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WAVUtils.Core;
using static WAVUtils.Tests.WaveFixture;

namespace WAVUtils.Tests;

[TestClass]
public sealed class TimecodeRegressionTests
{
    [TestMethod]
    [DataRow("en", "Stamping timecode...", "Checking original WAV", "Writing timecode WAV", "Saving original backup", "Verifying timecode WAV")]
    [DataRow("ru", "Запись таймкода...", "Проверка исходного WAV", "Запись WAV с таймкодом", "Сохранение исходной копии", "Проверка WAV после записи таймкода")]
    [DataRow("de", "Timecode wird geschrieben...", "Original-WAV wird geprüft", "WAV mit Timecode wird geschrieben", "Sicherung des Originals wird gespeichert", "WAV mit Timecode wird geprüft")]
    [DataRow("es", "Escribiendo el código de tiempo...", "Comprobando el WAV original", "Escribiendo el WAV con código de tiempo", "Guardando la copia de seguridad original", "Verificando el WAV con código de tiempo")]
    [DataRow("fr", "Écriture du code temporel...", "Vérification du WAV d’origine", "Écriture du WAV avec le code temporel", "Sauvegarde du fichier d’origine", "Vérification du WAV avec le code temporel")]
    [DataRow("ja", "タイムコードを書き込み中...", "元のWAVファイルを確認中", "タイムコード付きWAVを書き込み中", "元のファイルのバックアップを保存中", "タイムコード付きWAVを検証中")]
    [DataRow("zh", "正在写入时间码...", "正在检查原始 WAV", "正在写入带时间码的 WAV", "正在保存原始文件的备份", "正在验证带时间码的 WAV")]
    public void Finding63TimecodeReportsEachPhaseInTheSelectedLanguage(string language, string initial, string checking,
        string writing, string saving, string verifying)
    {
        using var f = new WaveFixture();
        string path = f.Write("ltc.wav", LtcRegressionTests.Audio());
        var messages = new List<string>();
        CultureInfo previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            TcCommand.Run([path], progress: (_, message) => messages.Add(message));
            string[] expected = [checking, writing, saving, verifying, LocalizationManager.GetResource("ProgressComplete")];
            Assert.AreEqual(string.Join('\n', expected), string.Join('\n', messages.Distinct()));
            Assert.AreEqual(initial, LocalizationManager.GetResource("AppTimecodeOperation"));
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [TestMethod]
    public void Finding40AStampedDonorStillUsesItsOriginalRecorderClock()
    {
        using var f = new WaveFixture();
        byte[] originalBext = Bext();
        string donor = f.Write("donor.wav", LtcRegressionTests.Audio(), chunks: [new() { ChunkId = "bext", Data = originalBext }]);
        string target = f.Write("target.wav", Pcm16(1, 2, 3), chunks: [new() { ChunkId = "bext", Data = originalBext },
            new() { ChunkId = "iXML", Data = "<BWF_XML><NOTE>Keep this</NOTE></BWF_XML>"u8.ToArray() }]);
        byte[] before = File.ReadAllBytes(target);
        TcCommand.Run([donor]);
        TcCommand.Run([target], tcSource: donor);
        var chunks = Chunks(target);
        Assert.AreEqual(LtcRegressionTests.Reference, BitConverter.ToInt64(chunks["bext"], 338));
        Assert.AreEqual((short)1, BitConverter.ToInt16(chunks["bext"], 346));
        CollectionAssert.AreEqual(originalBext.AsSpan(348).ToArray(), chunks["bext"].AsSpan(348).ToArray());
        CollectionAssert.AreEqual(Pcm16(1, 2, 3), chunks["data"]);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(target + ".bak"));
        var xml = XDocument.Parse(System.Text.Encoding.UTF8.GetString(chunks["iXML"]));
        Assert.AreEqual("Keep this", xml.Root!.Element("NOTE")!.Value);
        Assert.AreEqual(LtcRegressionTests.Reference.ToString(CultureInfo.InvariantCulture),
            xml.Root.Element("SPEED")!.Element("TIMESTAMP_SAMPLES_SINCE_MIDNIGHT_LO")!.Value);
        byte[] first = File.ReadAllBytes(target);
        TcCommand.Run([target], tcSource: donor);
        CollectionAssert.AreEqual(first, File.ReadAllBytes(target));
    }

    [TestMethod]
    public void Finding34OwnLtcAddsStandardBextAndKeepsTheFirstBackup()
    {
        using var f = new WaveFixture();
        string path = f.Write("ltc.wav", LtcRegressionTests.Audio());
        byte[] before = File.ReadAllBytes(path);
        TcCommand.Run([path]);
        var chunks = Chunks(path);
        Assert.AreEqual(LtcRegressionTests.Reference, BitConverter.ToInt64(chunks["bext"], 338));
        Assert.AreEqual("02:03:04", System.Text.Encoding.ASCII.GetString(chunks["bext"], 330, 8));
        Assert.IsTrue(chunks.ContainsKey("wtcp"));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(path + ".bak"));
        byte[] first = File.ReadAllBytes(path);
        TcCommand.Run([path]);
        CollectionAssert.AreEqual(first, File.ReadAllBytes(path));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(path + ".bak"));
    }

    [TestMethod]
    public void Finding44AnUnrelatedBackupPreventsAnyReplacement()
    {
        using var f = new WaveFixture();
        string path = f.Write("ltc.wav", LtcRegressionTests.Audio(), chunks: [new() { ChunkId = "bext", Data = Bext() }]);
        byte[] before = File.ReadAllBytes(path), sentinel = "unrelated backup"u8.ToArray();
        File.WriteAllBytes(path + ".bak", sentinel);
        Assert.ThrowsExactly<IOException>(() => TcCommand.Run([path]));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
        CollectionAssert.AreEqual(sentinel, File.ReadAllBytes(path + ".bak"));
    }

    [TestMethod]
    public void Finding35InvalidIxmlPreventsStamping()
    {
        using var f = new WaveFixture();
        string path = f.Write("ltc.wav", LtcRegressionTests.Audio(), chunks: [new() { ChunkId = "bext", Data = Bext() },
            new() { ChunkId = "iXML", Data = "<!DOCTYPE BWF_XML [<!ENTITY x 'unsafe'>]><BWF_XML>&x;</BWF_XML>"u8.ToArray() }]);
        byte[] before = File.ReadAllBytes(path);
        Assert.ThrowsExactly<XmlException>(() => TcCommand.Run([path]));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
        Assert.IsFalse(File.Exists(path + ".bak"));
    }

    [TestMethod]
    public void Finding40DonorWithoutAnOriginalBextCannotShiftSiblingTracks()
    {
        using var f = new WaveFixture();
        string donor = f.Write("donor.wav", LtcRegressionTests.Audio());
        string target = f.Write("target.wav", Pcm16(1, 2), chunks: [new() { ChunkId = "bext", Data = Bext() }]);
        byte[] before = File.ReadAllBytes(target);
        Assert.ThrowsExactly<InvalidDataException>(() => TcCommand.Run([target], tcSource: donor));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(target));
    }

    [TestMethod]
    public void Finding44AllTargetsAreValidatedBeforeStamping()
    {
        using var f = new WaveFixture();
        string donor = f.Write("donor.wav", LtcRegressionTests.Audio(), chunks: [new() { ChunkId = "bext", Data = Bext() }]);
        string target = f.Write("target.wav", Pcm16(1, 2), chunks: [new() { ChunkId = "bext", Data = Bext() }]);
        string invalid = f.Write("invalid.wav", [1, 2], bits: 8);
        byte[] before = File.ReadAllBytes(target);
        Assert.ThrowsExactly<InvalidDataException>(() => TcCommand.Run([target, invalid], tcSource: donor));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(target));
        Assert.IsFalse(File.Exists(target + ".bak"));
    }
}
