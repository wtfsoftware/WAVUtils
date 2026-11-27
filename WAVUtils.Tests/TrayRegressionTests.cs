using System.Globalization;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WAVUtils.Tray;

namespace WAVUtils.Tests;

[TestClass]
[DoNotParallelize]
public sealed class TrayRegressionTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Finding49TrayStartupAndMenuUseTheSameLoadedPreference(bool enabled) => DesktopRegressionTests.OnSta(() =>
    {
        using var form = new TrayForm("{\"checkUpdates\":" + (enabled ? "true" : "false") + "}");
        var icon = (NotifyIcon)typeof(TrayForm).GetField("_notifyIcon", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        var item = icon.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(menu => menu.CheckState != CheckState.Unchecked || menu.Text == WAVUtils.Core.LocalizationManager.GetResource("UpdateAutoCheck"));
        Assert.AreEqual(enabled, item.Checked);
        Assert.AreEqual(enabled, typeof(TrayForm).GetField("_autoCheckEnabled", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form));
    });

    [TestMethod]
    public void Finding66TrayDoesNotOfferToOpenAnUnwrittenLog() => DesktopRegressionTests.OnSta(() =>
    {
        using var form = new TrayForm("{\"checkUpdates\":false}");
        var icon = (NotifyIcon)typeof(TrayForm).GetField("_notifyIcon", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        Assert.IsFalse(icon.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().Any(menu =>
            menu.Text == WAVUtils.Core.LocalizationManager.GetResource("TrayLog")), "Tray must not offer to open a log it does not write.");
    });

    [TestMethod]
    [DataRow("en", 1f)]
    [DataRow("ru", 1f)]
    [DataRow("en", 1.5f)]
    [DataRow("ru", 2f)]
    public void Finding50DownloadControlsFitAtEachRenderedScale(string language, float scale) => DesktopRegressionTests.OnSta(() =>
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        using var window = new DownloadWindow();
        window.Show();
        window.Scale(new SizeF(scale, scale));
        window.PerformLayout();
        foreach (Control control in window.Controls)
            Assert.IsTrue(window.ClientRectangle.Contains(control.Bounds), $"{control.Name}: {control.Bounds} exceeds {window.ClientRectangle}.");
        var button = window.Controls.OfType<Button>().Single();
        Assert.IsTrue(TextRenderer.MeasureText(button.Text, button.Font).Width <= button.ClientSize.Width);
    });

    [TestMethod]
    public void Finding50ClosingADownloadCancelsTheWorkerBeforeTheWindowDisappears() => DesktopRegressionTests.OnSta(() =>
    {
        using var window = new DownloadWindow();
        using var reachedRead = new ManualResetEventSlim();
        bool disposed = false;
        Exception? failure = null;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        window.Shown += async (_, _) =>
        {
            try
            {
                await window.RunAsync(async (_, token) =>
                {
                    reachedRead.Set();
                    try { await Task.Delay(Timeout.Infinite, token); }
                    finally { disposed = true; }
                    throw new AssertFailedException("The cancelled worker returned a download.");
                }, CancellationToken.None);
                failure = new AssertFailedException("Closing must cancel the active download.");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { failure = ex; }
        };
        using var click = new System.Windows.Forms.Timer { Interval = 50 };
        click.Tick += (_, _) =>
        {
            if (!reachedRead.IsSet) return;
            click.Stop();
            clock.Restart();
            window.Close();
        };
        click.Start();
        Application.Run(window);
        Assert.IsTrue(disposed, "The worker must release its source before UI idle.");
        Assert.IsTrue(clock.Elapsed < TimeSpan.FromSeconds(10));
        if (failure != null) throw failure;
    });
}
