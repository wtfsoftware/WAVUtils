using System.Windows.Forms;
using WAVUtils.Core;

namespace WAVUtils.App;

/// <summary>
/// Progress window for merge/split operations. Auto-closes after 2 seconds from completion.
/// </summary>
public class ProgressWindow : Form
{
    private Label _label;
    private ProgressBar _progressBar;
    private ListBox _fileList;
    private System.Windows.Forms.Timer _autoCloseTimer;
    private int _filesCompleted;
    private int _totalFiles;

    public ProgressWindow(int totalFiles)
    {
        _totalFiles = totalFiles;
        _filesCompleted = 0;

        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.Size = new System.Drawing.Size(400, 200);
        this.Text = WAVUtils.Core.LocalizationManager.GetResource("AppTitle");
        this.Font = SystemFonts.MessageBoxFont;

        _label = new Label
        {
            Location = new System.Drawing.Point(20, 15),
            Size = new System.Drawing.Size(360, 25),
            Font = new System.Drawing.Font(this.Font.FontFamily, 10f, System.Drawing.FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter
        };
        this.Controls.Add(_label);

        _progressBar = new ProgressBar
        {
            Location = new System.Drawing.Point(20, 50),
            Size = new System.Drawing.Size(360, 25),
            Style = ProgressBarStyle.Continuous
        };
        this.Controls.Add(_progressBar);

        _fileList = new ListBox
        {
            Location = new System.Drawing.Point(20, 85),
            Size = new System.Drawing.Size(360, 90),
            Font = new System.Drawing.Font("Consolas", 8.5f)
        };
        this.Controls.Add(_fileList);

        _autoCloseTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _autoCloseTimer.Tick += (s, e) =>
        {
            _autoCloseTimer.Stop();
            this.Close();
        };
    }

    public void SetOperation(string operation)
    {
        _label.Text = operation;
        this.Update();
    }

    public void AddFile(string fileName)
    {
        _fileList.Items.Add(fileName);
        _filesCompleted++;
        _progressBar.Value = (_filesCompleted * 100) / _totalFiles;
        this.Update();
    }

    public void Complete()
    {
        _label.Text = WAVUtils.Core.LocalizationManager.GetResource("ProgressComplete");
        _progressBar.Value = 100;
        _autoCloseTimer.Start();
    }

    public void Fail(string error)
    {
        _label.Text = string.Format(WAVUtils.Core.LocalizationManager.GetResource("ProgressError"), error);
        _progressBar.Style = ProgressBarStyle.Blocks;
        _progressBar.Value = 0;
        _autoCloseTimer.Interval = 5000;
        _autoCloseTimer.Start();
    }
}
