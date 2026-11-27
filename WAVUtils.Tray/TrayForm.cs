using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using WAVUtils.Core;

namespace WAVUtils.Tray;

/// <summary>
/// Tray form that runs minimized and provides notifications.
/// </summary>
public class TrayForm : Form
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly System.Windows.Forms.Timer _startupTimer;
    private readonly ToolStripMenuItem _updateCheckItem;
    private readonly ToolStripMenuItem _autoCheckItem;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _autoCheckEnabled;
    private bool _checking;
    private bool _exiting;
    public DownloadedInstaller? PendingInstaller { get; private set; }

    public TrayForm() : this(UserSettings.ReadConfig()) { }

    internal TrayForm(string settings)
    {
        _autoCheckEnabled = LoadAutoCheckSetting(settings);
        this.Visible = false;
        this.ShowInTaskbar = false;
        this.WindowState = FormWindowState.Minimized;
        this.FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing && !_exiting) { e.Cancel = true; Hide(); return; }
            _exiting = true;
            _lifetime.Cancel();
            if (_checking) e.Cancel = true;
        };

        _contextMenu = new ContextMenuStrip();
        _updateCheckItem = new ToolStripMenuItem(WAVUtils.Core.LocalizationManager.GetResource("UpdateCheckNow"));
        _updateCheckItem.Click += async (s, e) => await CheckForUpdatesAsync();
        _contextMenu.Items.Add(_updateCheckItem);
        
        _autoCheckItem = new ToolStripMenuItem(WAVUtils.Core.LocalizationManager.GetResource("UpdateAutoCheck"));
        _autoCheckItem.Checked = _autoCheckEnabled;
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

        _startupTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _startupTimer.Tick += async (s, e) =>
        {
            _startupTimer.Stop();
            this.WindowState = FormWindowState.Minimized;
            this.Visible = false;
            
            if (_autoCheckEnabled)
            {
                await CheckForUpdatesAsync(manual: false);
            }
        };
        _startupTimer.Start();
    }

    internal static bool LoadAutoCheckSetting(string json)
    {
        using var config = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        if (config.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Settings must be a JSON object.");
        if (!config.RootElement.TryGetProperty("checkUpdates", out var value)) return true;
        return value.ValueKind == JsonValueKind.True;
    }

    private void SaveAutoCheckSetting(bool enabled)
    {
        UserSettings.SetValue("checkUpdates", enabled);
    }

    private void ToggleAutoCheck()
    {
        try
        {
            bool enabled = !_autoCheckEnabled;
            SaveAutoCheckSetting(enabled);
            _autoCheckEnabled = enabled;
            _autoCheckItem.Checked = enabled;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, LocalizationManager.GetResource("AppTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task CheckForUpdatesAsync(bool manual = true)
    {
        if (_checking || _exiting) return;
        _checking = true;
        _updateCheckItem.Enabled = false;
        _updateCheckItem.Text = WAVUtils.Core.LocalizationManager.GetResource("UpdateChecking");

        try
        {
            var updateInfo = await UpdateChecker.CheckForUpdatesAsync(_lifetime.Token);
            if (_exiting) return;

            if (!string.IsNullOrEmpty(updateInfo.ErrorMessage))
            {
                if (manual) OfferManualInstallation(this, updateInfo.ErrorMessage);
                else _notifyIcon.ShowBalloonTip(5000, LocalizationManager.GetResource("AppTitle"), updateInfo.ErrorMessage, ToolTipIcon.Warning);
            }
            else if (updateInfo.UpdateAvailable)
            {
                var result = MessageBox.Show(this,
                    string.Format(WAVUtils.Core.LocalizationManager.GetResource("UpdateAvailable"),
                        updateInfo.LatestVersion, updateInfo.CurrentVersion) + "\n\n" + LocalizationManager.GetResource("UpdateDownloadPrompt"),
                    WAVUtils.Core.LocalizationManager.GetResource("AppTitle"),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information, MessageBoxDefaultButton.Button2);

                if (result == DialogResult.Yes)
                {
                    await DownloadAndInstallAsync(updateInfo);
                }
            }
            else if (manual)
            {
                MessageBox.Show(this,
                    WAVUtils.Core.LocalizationManager.GetResource("UpdateUpToDate"),
                    WAVUtils.Core.LocalizationManager.GetResource("AppTitle"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_exiting) OfferManualInstallation(this, ex.Message);
        }
        finally
        {
            _checking = false;
            if (_exiting) Close();
            else
            {
                _updateCheckItem.Enabled = true;
                _updateCheckItem.Text = WAVUtils.Core.LocalizationManager.GetResource("UpdateCheckNow");
            }
        }
    }

    private async Task DownloadAndInstallAsync(UpdateInfo update)
    {
        using var window = new DownloadWindow();
        try
        {
            window.Show(this);
            var installer = await window.RunAsync((report, token) => UpdateChecker.DownloadInstallerAsync(update, report, token), _lifetime.Token);
            if (!_exiting && MessageBox.Show(this, LocalizationManager.GetResource("UpdateVerifiedReady"),
                LocalizationManager.GetResource("AppTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
            {
                PendingInstaller = installer;
                Exit();
            }
            else File.Delete(installer.Path);
        }
        catch (OperationCanceledException) when (window.CancellationRequested || _lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_exiting) OfferManualInstallation(this, ex.Message);
        }
    }

    internal static void OfferManualInstallation(IWin32Window? owner, string reason)
    {
        if (MessageBox.Show(owner, reason + "\n\n" + LocalizationManager.GetResource("UpdateManualDownload") + "\n" + UpdateChecker.OfficialReleasePage,
            LocalizationManager.GetResource("AppTitle"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK)
            using (Process.Start(new ProcessStartInfo(UpdateChecker.OfficialReleasePage) { UseShellExecute = true })) { }
    }

    private void ShowAbout()
    {
        MessageBox.Show(this,
            WAVUtils.Core.LocalizationManager.GetResource("AboutText"),
            WAVUtils.Core.LocalizationManager.GetResource("AboutTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void Exit()
    {
        _exiting = true;
        _lifetime.Cancel();
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _startupTimer.Stop();
        _notifyIcon.Visible = false;
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _startupTimer.Dispose();
            _notifyIcon.Dispose();
            _contextMenu.Dispose();
            _lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
}
