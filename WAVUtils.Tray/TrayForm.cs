using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using WAVUtils.Core;

namespace WAVUtils.Tray;

/// <summary>
/// Tray form that runs minimized and provides notifications.
/// </summary>
public class TrayForm : Form
{
    private NotifyIcon _notifyIcon;
    private ContextMenuStrip _contextMenu;
    private System.Windows.Forms.Timer _startupTimer;

    public TrayForm()
    {
        this.Visible = false;
        this.ShowInTaskbar = false;
        this.WindowState = FormWindowState.Minimized;
        this.FormClosing += (s, e) => { e.Cancel = true; this.Hide(); };

        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.Add(new ToolStripMenuItem(WAVUtils.Core.LocalizationManager.GetResource("TrayLog"), null, (s, e) => OpenLogFile()));
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(new ToolStripMenuItem(WAVUtils.Core.LocalizationManager.GetResource("TrayAbout"), null, (s, e) => ShowAbout()));
        _contextMenu.Items.Add(new ToolStripMenuItem(WAVUtils.Core.LocalizationManager.GetResource("TrayExit"), null, (s, e) => Exit()));

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Visible = true,
            Text = WAVUtils.Core.LocalizationManager.GetResource("AppTitle"),
            ContextMenuStrip = _contextMenu
        };
        _notifyIcon.DoubleClick += (s, e) => OpenLogFile();

        _startupTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _startupTimer.Tick += (s, e) =>
        {
            _startupTimer.Stop();
            this.WindowState = FormWindowState.Minimized;
            this.Visible = false;
        };
        _startupTimer.Start();
    }

    private void OpenLogFile()
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

    private void ShowAbout()
    {
        MessageBox.Show(this,
            WAVUtils.Core.LocalizationManager.GetResource("AboutText"),
            WAVUtils.Core.LocalizationManager.GetResource("AboutTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void Exit()
    {
        _notifyIcon?.Dispose();
        Application.Exit();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _notifyIcon?.Dispose();
        base.OnFormClosed(e);
    }
}
