using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WAVUtils.Core;

namespace WAVUtils.Tests;

[TestClass]
public sealed class ResourceRegressionTests
{
    [TestMethod]
    [DataRow("en")]
    [DataRow("ru")]
    public void Finding49DesktopKeysResolveThroughTheSharedResourceManager(string language)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            foreach (string key in new[] { "AppTitle", "AppMergeOperation", "AppSplitOperation", "ProgressComplete", "ProgressError", "TrayLog", "TrayExit", "AboutTitle" })
                Assert.AreNotEqual(key, LocalizationManager.GetResource(key), "Unresolved resource: " + key);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [TestMethod]
    [DataRow("en", "Checking for updates...", "Download verified.")]
    [DataRow("ru", "Проверка обновлений...", "Загрузка проверена.")]
    public void Finding57UpdateFeedbackUsesTheSelectedLanguageAndNeverClaimsSetupStarted(string language, string checking, string verified)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            Assert.AreEqual(checking, LocalizationManager.GetResource("UpdateChecking"));
            Assert.AreEqual(verified, LocalizationManager.GetResource("UpdateDownloadComplete"));
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [TestMethod]
    [DataRow("de", "Beenden")]
    [DataRow("es", "Salir")]
    [DataRow("fr", "Quitter")]
    [DataRow("ja", "終了")]
    [DataRow("zh", "退出")]
    public void Finding57SharedResourcesKeepTheExistingSupportedTranslations(string language, string exit)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            Assert.AreEqual(exit, LocalizationManager.GetResource("TrayExit"));
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
}
