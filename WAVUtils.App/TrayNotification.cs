using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using WAVUtils.Core;

namespace WAVUtils.App;

/// <summary>
/// Tray notification helper.
/// </summary>
public static class TrayNotification
{
    public static void ShowSuccess(string title, string message)
    {
        using var tray = new NotifyIcon { Icon = SystemIcons.Shield, Visible = true };
        tray.ShowBalloonTip(3000, title, message, ToolTipIcon.Info);
    }

    public static void ShowError(string title, string message)
    {
        using var tray = new NotifyIcon { Icon = SystemIcons.Shield, Visible = true };
        tray.ShowBalloonTip(5000, title, message, ToolTipIcon.Error);
    }

    public static void OpenLogFile()
    {
        try
        {
            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WAVUtils");
            Directory.CreateDirectory(logDir);
            string logFile = Path.Combine(logDir, "WAVUtils.log");
            if (File.Exists(logFile))
                Process.Start(new ProcessStartInfo(logFile) { UseShellExecute = true });
        }
        catch { }
    }

    public static void ShowAbout()
    {
        MessageBox.Show(
            WAVUtils.Core.LocalizationManager.GetResource("AboutText"),
            WAVUtils.Core.LocalizationManager.GetResource("AboutTitle"),
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }
}
