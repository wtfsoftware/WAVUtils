using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
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
    private ToolStripMenuItem _updateCheckItem;
    private ToolStripMenuItem _autoCheckItem;
    private bool _autoCheckEnabled = true;

    public TrayForm()
    {
        this.Visible = false;
        this.ShowInTaskbar = false;
        this.WindowState = FormWindowState.Minimized;
        this.FormClosing += (s, e) => { e.Cancel = true; this.Hide(); };

        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.Add(new ToolStripMenuItem(WAVUtils.Core.LocalizationManager.GetResource("TrayLog"), null, (s, e) => OpenLogFile()));
        _contextMenu.Items.Add(new ToolStripSeparator());
        
        _updateCheckItem = new ToolStripMenuItem(WAVUtils.Core.LocalizationManager.GetResource("UpdateCheckNow"));
        _updateCheckItem.Click += async (s, e) => await CheckForUpdatesAsync();
        _contextMenu.Items.Add(_updateCheckItem);
        
        _autoCheckItem = new ToolStripMenuItem(WAVUtils.Core.LocalizationManager.GetResource("UpdateAutoCheck"));
        _autoCheckItem.Checked = LoadAutoCheckSetting();
        _autoCheckItem.Click += (s, e) => ToggleAutoCheck();
        _contextMenu.Items.Add(_autoCheckItem);
        
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
            
            if (_autoCheckEnabled)
            {
                Task.Run(() => CheckForUpdatesAsync());
            }
        };
        _startupTimer.Start();
    }

    private bool LoadAutoCheckSetting()
    {
        try
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                var config = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                if (config?.ContainsKey("checkUpdates") == true)
                {
                    return config["checkUpdates"] is bool b && b;
                }
            }
        }
        catch { }
        return true;
    }

    private void SaveAutoCheckSetting(bool enabled)
    {
        try
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
            Dictionary<string, object> config;
            
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                config = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new();
            }
            else
            {
                config = new();
            }

            config["checkUpdates"] = enabled;
            var newJson = System.Text.Json.JsonSerializer.Serialize(config);
            File.WriteAllText(configPath, newJson);
        }
        catch { }
    }

    private void ToggleAutoCheck()
    {
        _autoCheckItem.Checked = !_autoCheckItem.Checked;
        _autoCheckEnabled = _autoCheckItem.Checked;
        SaveAutoCheckSetting(_autoCheckEnabled);
    }

    private async Task CheckForUpdatesAsync()
    {
        _updateCheckItem.Enabled = false;
        _updateCheckItem.Text = WAVUtils.Core.LocalizationManager.GetResource("UpdateChecking");

        try
        {
            var updateInfo = await UpdateChecker.CheckForUpdatesAsync();

            if (!string.IsNullOrEmpty(updateInfo.ErrorMessage))
            {
                MessageBox.Show(this,
                    updateInfo.ErrorMessage,
                    WAVUtils.Core.LocalizationManager.GetResource("AppTitle"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            else if (updateInfo.UpdateAvailable)
            {
                var result = MessageBox.Show(this,
                    string.Format(WAVUtils.Core.LocalizationManager.GetResource("UpdateAvailable"),
                        updateInfo.LatestVersion, updateInfo.CurrentVersion),
                    WAVUtils.Core.LocalizationManager.GetResource("AppTitle"),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);

                if (result == DialogResult.Yes && !string.IsNullOrEmpty(updateInfo.DownloadUrl))
                {
                    await DownloadAndInstallAsync(updateInfo.DownloadUrl);
                }
            }
            else
            {
                MessageBox.Show(this,
                    WAVUtils.Core.LocalizationManager.GetResource("UpdateUpToDate"),
                    WAVUtils.Core.LocalizationManager.GetResource("AppTitle"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                string.Format(WAVUtils.Core.LocalizationManager.GetResource("UpdateCheckError"), ex.Message),
                WAVUtils.Core.LocalizationManager.GetResource("AppTitle"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _updateCheckItem.Enabled = true;
            _updateCheckItem.Text = WAVUtils.Core.LocalizationManager.GetResource("UpdateCheckNow");
        }
    }

    private async Task DownloadAndInstallAsync(string downloadUrl)
    {
        try
        {
            MessageBox.Show(this,
                WAVUtils.Core.LocalizationManager.GetResource("UpdateDownloadStart"),
                WAVUtils.Core.LocalizationManager.GetResource("AppTitle"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            var installPath = Path.GetDirectoryName(Application.ExecutablePath) ?? Environment.CurrentDirectory;
            var success = await UpdateChecker.DownloadAndInstallAsync(downloadUrl, installPath);

            if (success)
            {
                MessageBox.Show(this,
                    WAVUtils.Core.LocalizationManager.GetResource("UpdateInstallSuccess"),
                    WAVUtils.Core.LocalizationManager.GetResource("AppTitle"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            else
            {
                var result = MessageBox.Show(this,
                    WAVUtils.Core.LocalizationManager.GetResource("UpdateInstallFailed") + "\n\n" +
                    WAVUtils.Core.LocalizationManager.GetResource("UpdateManualDownload") + ":\n" +
                    downloadUrl,
                    WAVUtils.Core.LocalizationManager.GetResource("AppTitle"),
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning);

                if (result == DialogResult.OK)
                {
                    Process.Start(new ProcessStartInfo(downloadUrl) { UseShellExecute = true });
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                string.Format(WAVUtils.Core.LocalizationManager.GetResource("UpdateCheckError"), ex.Message),
                WAVUtils.Core.LocalizationManager.GetResource("AppTitle"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
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
