using System.Diagnostics;
using WAVUtils.Core;

namespace WAVUtils.Tray;

internal sealed class DownloadWindow : Form
{
    private readonly Label _status;
    private readonly ProgressBar _progress;
    private readonly Button _cancel;
    private readonly System.Windows.Forms.Timer _refresh;
    private readonly Stopwatch _elapsed = new();
    private CancellationTokenSource? _cancellation;
    private double _value;
    private bool _running;
    public bool CancellationRequested => _cancellation?.IsCancellationRequested == true;

    public DownloadWindow()
    {
        Text = LocalizationManager.GetResource("AppTitle");
        ClientSize = new Size(540, 152);
        Font = SystemFonts.MessageBoxFont;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        _status = new Label
        {
            Name = "DownloadStatus", Location = new Point(20, 12), Size = new Size(500, 36),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Text = LocalizationManager.GetResource("UpdateDownloadStart"), TextAlign = ContentAlignment.MiddleCenter
        };
        _progress = new ProgressBar
        {
            Name = "DownloadProgress", AccessibleName = LocalizationManager.GetResource("ProgressLabel"),
            Location = new Point(20, 56), Size = new Size(500, 24), Maximum = 1000, Style = ProgressBarStyle.Continuous,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _cancel = new Button
        {
            Name = "DownloadAction", Text = LocalizationManager.GetResource("ProgressCancel"),
            Location = new Point(424, 100), AutoSize = true, MinimumSize = new Size(96, 32),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        Controls.AddRange([_status, _progress, _cancel]);
        _cancel.Click += (_, _) => CancelDownload();
        CancelButton = _cancel;
        FormClosing += (_, e) => { if (_running) { e.Cancel = true; CancelDownload(); } };
        _refresh = new System.Windows.Forms.Timer { Interval = 200 };
        _refresh.Tick += (_, _) =>
        {
            _progress.Value = (int)(Math.Clamp(Volatile.Read(ref _value), 0, 1) * 1000);
            _status.Text = $"{LocalizationManager.GetResource(CancellationRequested ? "ProgressCancelling" : "UpdateDownloadStart")}\n{_progress.Value / 10.0:0.0}% — {_elapsed.Elapsed:hh\\:mm\\:ss}";
        };
    }

    public async Task<DownloadedInstaller> RunAsync(Func<Action<double, string>, CancellationToken, Task<DownloadedInstaller>> download,
        CancellationToken cancellationToken)
    {
        if (_cancellation != null) throw new InvalidOperationException("This download window has already been used.");
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _running = true;
        _elapsed.Start();
        _refresh.Start();
        try { return await download((value, _) => Volatile.Write(ref _value, value), _cancellation.Token); }
        finally { _running = false; _refresh.Stop(); _elapsed.Stop(); Close(); }
    }

    private void CancelDownload()
    {
        _cancellation?.Cancel();
        _cancel.Enabled = false;
        _status.Text = LocalizationManager.GetResource("ProgressCancelling");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _refresh.Dispose(); _cancellation?.Dispose(); }
        base.Dispose(disposing);
    }
}
