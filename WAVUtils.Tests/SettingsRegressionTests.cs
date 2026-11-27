using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WAVUtils.Core;
using WAVUtils.Tray;

namespace WAVUtils.Tests;

[TestClass]
public sealed class SettingsRegressionTests
{
    [TestMethod]
    public void Finding51LegacyLowercaseLanguageIsRecognized()
        => Assert.AreEqual("ru", LocalizationManager.GetPreferredLanguage("""{"language":"ru","checkUpdates":false}"""));

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Finding49AutocheckReadsTheActualJsonBoolean(bool enabled)
    {
        string json = JsonSerializer.Serialize(new { checkUpdates = enabled });
        Assert.AreEqual(enabled, TrayForm.LoadAutoCheckSetting(json));
    }

    [TestMethod]
    public void Finding73InvalidLanguageDoesNotBlockReadingThePreferences()
    {
        const string json = """{"language":42,"checkUpdates":false}""";
        Assert.AreEqual("auto", LocalizationManager.GetPreferredLanguage(json));
        Assert.IsFalse(TrayForm.LoadAutoCheckSetting(json));
    }

    [TestMethod]
    [DataRow("{}", true, "auto")]
    [DataRow("{\"language\":\"ru\",\"checkUpdates\":\"true\"}", false, "ru")]
    [DataRow("{\"checkUpdates\":null}", false, "auto")]
    public void Finding73AnInvalidUpdatePreferenceIsNotTreatedAsOptIn(string json, bool expected, string language)
    {
        Assert.AreEqual(expected, TrayForm.LoadAutoCheckSetting(json));
        Assert.AreEqual(language, LocalizationManager.GetPreferredLanguage(json));
    }
}
