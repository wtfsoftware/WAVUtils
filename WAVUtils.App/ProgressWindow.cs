using System.Diagnostics;
using System.Windows.Forms;
using WAVUtils.Core;

namespace WAVUtils.App;

/// <summary>
/// Shows cooperative progress and retains the result until explicitly closed.
/// </summary>
public class ProgressWindow : Form
{
    private readonly Label _label;
    private readonly ProgressBar _progressBar;
    private readonly ListBox _fileList;
    private readonly Button _action;
    private readonly System.Windows.Forms.Timer _refresh;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Stopwatch _elapsed = new();
    private sealed record ProgressState(double Value, string Text);
    private ProgressState _latest = new(0, "");
    private bool _running;
    public int ExitCode { get; private set; }
    public string[] Results { get; private set; } = [];

    public ProgressWindow(int totalFiles)
    {
        if (totalFiles <= 0 || totalFiles > 256) throw new ArgumentOutOfRangeException(nameof(totalFiles));
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(540, 300);
        Text = LocalizationManager.GetResource("AppTitle");
        Font = SystemFonts.MessageBoxFont;

        _label = new Label
        {
            Name = "OperationStatus",
            Location = new Point(20, 12),
            Size = new Size(500, 48),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleCenter
        };
        this.Controls.Add(_label);

        _progressBar = new ProgressBar
        {
            Name = "OperationProgress",
            AccessibleName = LocalizationManager.GetResource("ProgressLabel"),
            Location = new Point(20, 68),
            Size = new Size(500, 24),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Maximum = 1000,
            Style = ProgressBarStyle.Continuous
        };
        this.Controls.Add(_progressBar);

        _fileList = new ListBox
        {
            Name = "OperationFiles",
            AccessibleName = LocalizationManager.GetResource("AppTitle"),
            Location = new Point(20, 108),
            Size = new Size(500, 128),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            HorizontalScrollbar = true,
            IntegralHeight = false
        };
        this.Controls.Add(_fileList);

        _action = new Button
        {
            Name = "OperationAction", Text = LocalizationManager.GetResource("ProgressClose"),
            Location = new Point(424, 252), AutoSize = true, MinimumSize = new Size(96, 32),
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom
        };
        _action.Click += (_, _) => { if (_running) RequestCancellation(); else Close(); };
        Controls.Add(_action);
        CancelButton = _action;
        FormClosing += (_, e) => { if (_running) { e.Cancel = true; RequestCancellation(); } };
        _refresh = new System.Windows.Forms.Timer { Interval = 200 };
        _refresh.Tick += (_, _) =>
        {
            var state = Volatile.Read(ref _latest);
            _progressBar.Value = Math.Max(_progressBar.Value, (int)(Math.Clamp(state.Value, 0, 1) * 1000));
            _label.Text = $"{(_cancellation.IsCancellationRequested ? LocalizationManager.GetResource("ProgressCancelling") : state.Text)}\n{_progressBar.Value / 10.0:0.0}% — {_elapsed.Elapsed:hh\\:mm\\:ss}";
        };
    }

    public void SetOperation(string operation)
    {
        _label.Text = operation;
        Volatile.Write(ref _latest, new ProgressState(0, operation));
    }

    public void AddFile(string fileName)
    {
        _fileList.Items.Add(fileName);
    }

    public void Start(Func<Action<double, string>, CancellationToken, Task<string[]>> work)
    {
        if (_running) throw new InvalidOperationException("An operation is already running.");
        _running = true;
        _action.Text = LocalizationManager.GetResource("ProgressCancel");
        Shown += async (_, _) =>
        {
            _elapsed.Start();
            _refresh.Start();
            try
            {
                Results = await work((value, text) => Volatile.Write(ref _latest, new ProgressState(value, text)), _cancellation.Token);
                _fileList.Items.Clear();
                _fileList.Items.AddRange(Results);
                Complete();
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
                Finish(LocalizationManager.GetResource("ProgressCancelled"), exitCode: 130);
            }
            catch (Exception ex) { Fail(ex.Message); }
        };
    }

    private void RequestCancellation()
    {
        _cancellation.Cancel();
        _action.Enabled = false;
        _label.Text = LocalizationManager.GetResource("ProgressCancelling");
    }

    public void Complete()
    {
        _progressBar.Value = 1000;
        Finish(LocalizationManager.GetResource("ProgressComplete"), exitCode: 0);
    }

    public void Fail(string error)
    {
        Results = [error];
        _fileList.Items.Clear();
        _fileList.Items.AddRange(error.Split('\n'));
        Finish(LocalizationManager.GetResource("ProgressError"), exitCode: 1);
    }

    private void Finish(string message, int exitCode)
    {
        _running = false;
        ExitCode = exitCode;
        _refresh.Stop();
        _elapsed.Stop();
        _label.Text = message;
        _action.Text = LocalizationManager.GetResource("ProgressClose");
        _action.Enabled = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _refresh.Dispose(); _cancellation.Dispose(); }
        base.Dispose(disposing);
    }
}
