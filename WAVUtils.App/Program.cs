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
            case "merge":
                if (args.Length < 3)
                {
                    Console.Error.WriteLine(LocalizationManager.GetResource("AppMergeArgError"));
                    return;
                }
                await RunMerge(args[1..]);
                break;

            case "split":
                if (args.Length < 2)
                {
                    Console.Error.WriteLine(LocalizationManager.GetResource("AppSplitArgError"));
                    return;
                }
                await RunSplit(args[1]);
                break;

            case "check-update":
                await RunCheckUpdate();
                break;

            default:
                Console.Error.WriteLine(string.Format(LocalizationManager.GetResource("AppUnknownCommand"), command));
                break;
        }
    }

    private static async Task RunMerge(string[] inputArgs)
    {
        string[] inputFiles = inputArgs.Take(inputArgs.Length - 1).ToArray();
        string? outputPath = inputArgs.Length > inputFiles.Length ? inputArgs[inputArgs.Length - 1] : null;

        using var progress = new ProgressWindow(inputFiles.Length);
        progress.SetOperation(LocalizationManager.GetResource("AppMergeOperation"));
        progress.Show();
        Application.DoEvents();

        try
        {
            foreach (var file in inputFiles)
            {
                progress.AddFile(Path.GetFileName(file));
            }

            string result = await Task.Run(() => WAVUtils.Core.MergeCommand.Run(inputFiles, outputPath));
            progress.Complete();

            // Tray notification
            using var tray = new NotifyIcon
            {
                Icon = SystemIcons.Shield,
                Visible = true
            };
            tray.ShowBalloonTip(3000, LocalizationManager.GetResource("AppTitle"), string.Format(LocalizationManager.GetResource("AppMergeBalloon"), Path.GetFileName(result)), ToolTipIcon.Info);
            tray.Dispose();
        }
        catch (Exception ex)
        {
            progress.Fail(ex.Message);
            using var tray = new NotifyIcon { Icon = SystemIcons.Shield, Visible = true };
            tray.ShowBalloonTip(5000, LocalizationManager.GetResource("AppTitle"), string.Format(LocalizationManager.GetResource("AppErrorBalloon"), ex.Message), ToolTipIcon.Error);
            tray.Dispose();
        }
    }

    private static async Task RunSplit(string mergedFile)
    {
        using var progress = new ProgressWindow(1);
        progress.SetOperation(LocalizationManager.GetResource("AppSplitOperation"));
        progress.Show();
        Application.DoEvents();

        try
        {
            var result = await Task.Run(() => WAVUtils.Core.SplitCommand.Run(mergedFile));
            progress.Complete();

            string fileNames = string.Join(", ", result.Where(f => !string.IsNullOrEmpty(f)).Select(Path.GetFileName));
            using var tray = new NotifyIcon { Icon = SystemIcons.Shield, Visible = true };
            tray.ShowBalloonTip(3000, LocalizationManager.GetResource("AppTitle"), string.Format(LocalizationManager.GetResource("AppSplitBalloon"), result.Length, fileNames), ToolTipIcon.Info);
            tray.Dispose();
        }
        catch (Exception ex)
        {
            progress.Fail(ex.Message);
            using var tray = new NotifyIcon { Icon = SystemIcons.Shield, Visible = true };
            tray.ShowBalloonTip(5000, "WAV Utils", $"Ошибка: {ex.Message}", ToolTipIcon.Error);
            tray.Dispose();
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
