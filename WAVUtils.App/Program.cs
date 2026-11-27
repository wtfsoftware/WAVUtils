using System.Windows.Forms;
using WAVUtils.Core;

namespace WAVUtils.App;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        using var upgradeMutex = new Mutex(initiallyOwned: false, "WAVUtilsGlobalMutex");
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            LocalizationManager.Initialize();
            if (args.Length == 0 || args[0] is "--help" or "-h")
            {
                Console.WriteLine(LocalizationManager.GetResource("AppBanner"));
                Console.WriteLine("WAVUtils merge file1.wav file2.wav [...] [--out output.wav]");
                Console.WriteLine("WAVUtils split merged.wav [--output-dir directory]");
                Console.WriteLine("WAVUtils tc [--from donor.wav] file1.wav [...]");
                Console.WriteLine("WAVUtils check-update");
                return 0;
            }
            switch (args[0].ToLowerInvariant())
            {
                case "merge" when args.Length >= 3 && args[1].Equals("--explorer", StringComparison.OrdinalIgnoreCase):
                {
                    string[] files = MergeInstance.CollectAsync(args[2..]).GetAwaiter().GetResult();
                    return files.Length == 0 ? 0 : RunMerge(files, outputPath: null);
                }
                case "merge":
                {
                    var (files, output) = ParseMergeArguments(args[1..]);
                    return RunMerge(files, output);
                }
                case "split":
                {
                    string[] files = ParseInputs(args[1..], "--output-dir", out string? directory);
                    if (files.Length != 1) throw new ArgumentException(LocalizationManager.GetResource("AppSplitArgError"));
                    return RunSplit(files[0], directory);
                }
                case "tc":
                {
                    string[] files = ParseInputs(args[1..], "--from", out string? donor);
                    if (files.Length == 0) throw new ArgumentException("tc: specify target files.");
                    return RunOperation(files, "AppTimecodeOperation", (report, token) => Task.Run(() =>
                        TcCommand.Run(files, tcSource: donor, progress: report, cancellationToken: token).ToArray(), token));
                }
                case "check-update" when args.Length == 1:
                    return RunCheckUpdate().GetAwaiter().GetResult();
                default:
                    throw new ArgumentException(string.Format(LocalizationManager.GetResource("AppUnknownCommand"), args[0]));
            }
        }
        catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); return 2; }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }

    private static (string[] Files, string? Output) ParseMergeArguments(string[] args)
    {
        string[] files = ParseInputs(args, "--out", out string? output);
        // Legacy output is unambiguous only after at least two existing inputs.
        if (output == null && files.Length >= 3 && !File.Exists(files[^1]) && !Directory.Exists(files[^1]) && files[..^1].All(File.Exists))
        {
            output = files[^1];
            files = files[..^1];
        }
        if (files.Length < 2) throw new ArgumentException(LocalizationManager.GetResource("AppMergeArgError"));
        return (files, output);
    }

    private static string[] ParseInputs(string[] args, string option, out string? value)
    {
        value = null;
        var files = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals(option, StringComparison.OrdinalIgnoreCase))
            {
                if (value != null || ++i == args.Length || string.IsNullOrWhiteSpace(args[i]) || args[i].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException("Specify exactly one value for " + option);
                value = args[i];
            }
            else if (args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Unknown option: " + args[i]);
            else files.Add(args[i]);
        }
        if (files.Count > 256) throw new ArgumentException("Too many input files.");
        return files.ToArray();
    }

    private static int RunMerge(string[] inputFiles, string? outputPath) => RunOperation(inputFiles, "AppMergeOperation",
        async (report, token) => [await MergeCommand.Run(inputFiles, outputPath, progress: report, cancellationToken: token)]);

    private static int RunSplit(string mergedFile, string? outputDirectory) => RunOperation([mergedFile], "AppSplitOperation",
        (report, token) => SplitCommand.Run(mergedFile, outputDirectory, progress: report, cancellationToken: token));

    private static int RunOperation(string[] files, string operation,
        Func<Action<double, string>, CancellationToken, Task<string[]>> work)
    {
        using var window = new ProgressWindow(files.Length);
        window.SetOperation(LocalizationManager.GetResource(operation));
        foreach (string file in files) window.AddFile(file);
        window.Start(work);
        Application.Run(window);
        foreach (string line in window.Results) Console.WriteLine(line);
        return window.ExitCode;
    }

    private static async Task<int> RunCheckUpdate()
    {
        Console.WriteLine(LocalizationManager.GetResource("UpdateChecking"));
        
        try
        {
            var updateInfo = await WAVUtils.Core.UpdateChecker.CheckForUpdatesAsync();

            if (!string.IsNullOrEmpty(updateInfo.ErrorMessage))
            {
                Console.Error.WriteLine(updateInfo.ErrorMessage);
                return 1;
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
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(string.Format(
                LocalizationManager.GetResource("UpdateCheckError"),
                ex.Message));
            return 1;
        }
    }
}
