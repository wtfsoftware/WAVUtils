using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WAVUtils.Core;
using WAVUtils.Tray;

namespace WAVUtils.Tests;

[TestClass]
public sealed class UserSettingsTests
{
    [TestMethod]
    public void Finding49SettingsFallbackKeepsLanguageAndUnknownFields()
    {
        using var f = new WaveFixture();
        string legacy = f.PathOf("legacy.json"), current = f.PathOf("user/config.json");
        const string original = "{\"language\":\"ru\",\"checkUpdates\":false,\"custom\":{\"keep\":17}}";
        File.WriteAllText(legacy, original);
        Assert.AreEqual(original, UserSettings.ReadConfigAt(current, legacy));
        UserSettings.SetValueAt(current, "checkUpdates", true, legacy);
        using var json = JsonDocument.Parse(UserSettings.ReadConfigAt(current, legacy));
        Assert.AreEqual("ru", json.RootElement.GetProperty("language").GetString());
        Assert.IsTrue(json.RootElement.GetProperty("checkUpdates").GetBoolean());
        Assert.AreEqual(17, json.RootElement.GetProperty("custom").GetProperty("keep").GetInt32());
        Assert.AreEqual(original, File.ReadAllText(legacy));
        Assert.AreEqual(1, Directory.GetFiles(Path.GetDirectoryName(current)!).Length);
    }

    [TestMethod]
    public void Finding49InvalidSettingsAreNotOverwritten()
    {
        using var f = new WaveFixture();
        string path = f.PathOf("config.json");
        File.WriteAllText(path, "[not an object");
        Assert.Throws<JsonException>(() => UserSettings.SetValueAt(path, "language", "en"));
        Assert.AreEqual("[not an object", File.ReadAllText(path));
    }

    [TestMethod]
    [DataRow("{\"language\":")]
    [DataRow("[]")]
    public void Finding73MalformedSettingsPermitStartupWithoutReadingTheLegacyFile(string contents)
    {
        using var f = new WaveFixture();
        string path = f.PathOf("config.json"), legacy = f.PathOf("legacy.json");
        File.WriteAllText(path, contents);
        File.WriteAllText(legacy, """{"language":"ru","checkUpdates":true}""");

        string startup = UserSettings.ReadConfigAt(path, legacy);

        Assert.IsFalse(TrayForm.LoadAutoCheckSetting(startup));
        Assert.AreEqual("auto", LocalizationManager.GetPreferredLanguage(startup));
        Assert.AreEqual(contents, File.ReadAllText(path));
    }

    [TestMethod]
    public void Finding73UnreadableUserSettingsDoNotReenableUpdatesFromLegacySettings()
    {
        using var f = new WaveFixture();
        string path = f.PathOf("config.json"), legacy = f.PathOf("legacy.json");
        Directory.CreateDirectory(path);
        File.WriteAllText(legacy, """{"checkUpdates":true}""");

        Assert.IsFalse(TrayForm.LoadAutoCheckSetting(UserSettings.ReadConfigAt(path, legacy)));
        Assert.IsTrue(Directory.Exists(path));
    }
}
