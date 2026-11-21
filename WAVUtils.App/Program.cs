using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;
using WAVUtils.Core;

namespace WAVUtils.App;

static class Program
{
    [STAThread]
    static async Task Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        WAVUtils.Core.LocalizationManager.Initialize();

        if (args.Length == 0)
        {
            Console.WriteLine(LocalizationManager.GetResource("AppBanner"));
            Console.WriteLine();
            Console.WriteLine(LocalizationManager.GetResource("AppUsage"));
            Console.WriteLine(LocalizationManager.GetResource("AppMergeUsage"));
            Console.WriteLine(LocalizationManager.GetResource("AppSplitUsage"));
            return;
        }

        string command = args[0].ToLower();

        switch (command)
        {
            case "merge" when args.Length >= 3
                && string.Equals(args[1], "--explorer", StringComparison.OrdinalIgnoreCase):
            {
                // Explorer starts this verb once per selected file; gather all files first.
                string[] explorerFiles = MergeInstance.CollectAsync(args[2..]).GetAwaiter().GetResult();
                if (explorerFiles.Length == 0)
                    return; // handed to the running collector instance
                RunMerge(explorerFiles, outputPath: null);
                break;
            }

            case "merge" when args.Length >= 3:
                RunMerge(args[1..^1], outputPath: args[^1]);
                break;

            case "merge":
                Console.Error.WriteLine(LocalizationManager.GetResource("AppMergeArgError"));
                break;

            case "split":
                if (args.Length < 2)
                {
                    Console.Error.WriteLine(LocalizationManager.GetResource("AppSplitArgError"));
                    return;
                }
                RunSplit(args[1]);
                break;

            case "tc" when args.Length >= 2:
            {
                string[] tcArgs = args[1..];
                string? tcSource = null;
                if (string.Equals(tcArgs[0], "--from", StringComparison.OrdinalIgnoreCase) && tcArgs.Length >= 2)
                {
                    tcSource = tcArgs[1];
                    tcArgs = tcArgs[2..];
                    if (tcArgs.Length == 0)
                    {
                        Console.Error.WriteLine("tc: no target files after --from <source>");
                        return;
                    }
                }
                foreach (var line in WAVUtils.Core.TcCommand.Run(tcArgs, tcSource))
                    Console.WriteLine(line);
                break;
            }

            case "check-update":
                await RunCheckUpdate();
                break;

            default:
                Console.Error.WriteLine(string.Format(LocalizationManager.GetResource("AppUnknownCommand"), command));
                break;
        }
    }

    // The UI thread pumps explicitly (no Application.Run here): an await
    // continuation posted to the WinForms sync context would never run and the
    // process would hang with the window stuck open.
    private static void RunMerge(string[] inputFiles, string? outputPath)
    {
        using var progress = new ProgressWindow(inputFiles.Length);
        progress.SetOperation(LocalizationManager.GetResource("AppMergeOperation"));
        progress.Show();

        foreach (var file in inputFiles)
        {
            progress.AddFile(Path.GetFileName(file));
        }

        string? result = null;
        Exception? error = null;
        var work = Task.Run(async () =>
        {
            try { result = await WAVUtils.Core.MergeCommand.Run(inputFiles, outputPath); }
            catch (Exception ex) { error = ex; }
        });
        while (!work.Wait(20))
            Application.DoEvents();

        if (error is null)
        {
            if (!progress.IsDisposed)
                progress.Complete();
            using var tray = new NotifyIcon { Icon = SystemIcons.Shield, Visible = true };
            tray.ShowBalloonTip(3000, LocalizationManager.GetResource("AppTitle"), string.Format(LocalizationManager.GetResource("AppMergeBalloon"), Path.GetFileName(result!)), ToolTipIcon.Info);
        }
        else
        {
            if (!progress.IsDisposed)
                progress.Fail(error.Message);
            using var tray = new NotifyIcon { Icon = SystemIcons.Shield, Visible = true };
            tray.ShowBalloonTip(5000, LocalizationManager.GetResource("AppTitle"), string.Format(LocalizationManager.GetResource("AppErrorBalloon"), error.Message), ToolTipIcon.Error);
        }
        PumpUntilClosed(progress);
    }

    private static void RunSplit(string mergedFile)
    {
        using var progress = new ProgressWindow(1);
        progress.SetOperation(LocalizationManager.GetResource("AppSplitOperation"));
        progress.Show();

        string[]? result = null;
        Exception? error = null;
        var work = Task.Run(async () =>
        {
            try { result = await WAVUtils.Core.SplitCommand.Run(mergedFile); }
            catch (Exception ex) { error = ex; }
        });
        while (!work.Wait(20))
            Application.DoEvents();

        if (error is null)
        {
            if (!progress.IsDisposed)
                progress.Complete();
            string fileNames = string.Join(", ", result!.Where(f => !string.IsNullOrEmpty(f)).Select(Path.GetFileName));
            using var tray = new NotifyIcon { Icon = SystemIcons.Shield, Visible = true };
            tray.ShowBalloonTip(3000, LocalizationManager.GetResource("AppTitle"), string.Format(LocalizationManager.GetResource("AppSplitBalloon"), result.Length, fileNames), ToolTipIcon.Info);
        }
        else
        {
            if (!progress.IsDisposed)
                progress.Fail(error.Message);
            using var tray = new NotifyIcon { Icon = SystemIcons.Shield, Visible = true };
            tray.ShowBalloonTip(5000, LocalizationManager.GetResource("AppTitle"), string.Format(LocalizationManager.GetResource("AppErrorBalloon"), error.Message), ToolTipIcon.Error);
        }
        PumpUntilClosed(progress);
    }

    private static void PumpUntilClosed(Form form)
    {
        while (form.Visible)
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(20);
        }
    }

    private static async Task RunCheckUpdate()
    {
        Console.WriteLine(LocalizationManager.GetResource("UpdateChecking"));
        
        try
        {
            var updateInfo = await WAVUtils.Core.UpdateChecker.CheckForUpdatesAsync();

            if (!string.IsNullOrEmpty(updateInfo.ErrorMessage))
            {
                Console.Error.WriteLine(updateInfo.ErrorMessage);
                return;
            }

            if (updateInfo.UpdateAvailable)
            {
                Console.WriteLine(string.Format(
                    LocalizationManager.GetResource("UpdateAvailable"),
                    updateInfo.LatestVersion, updateInfo.CurrentVersion));
                Console.WriteLine($"Download: {updateInfo.DownloadUrl}");
            }
            else
            {
                Console.WriteLine(LocalizationManager.GetResource("UpdateUpToDate"));
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(string.Format(
                LocalizationManager.GetResource("UpdateCheckError"),
                ex.Message));
        }
    }
}
