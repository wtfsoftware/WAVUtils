using System.Windows.Forms;
using WAVUtils.Core;

namespace WAVUtils.Tray;

static class Program
{
    [STAThread]
    static int Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            DownloadedInstaller? installer;
            using (var instance = new Mutex(initiallyOwned: false, @"Local\WAVUtils.Tray", out bool isFirst))
            {
                if (!isFirst) return 0;
                using var upgrade = new Mutex(initiallyOwned: false, "WAVUtilsGlobalMutex");
                LocalizationManager.Initialize();
                using var form = new TrayForm();
                Application.Run(form);
                installer = form.PendingInstaller;
            }
            // Setup starts only after the tray form and both application guards are gone.
            return installer == null ? 0 : FinishUpdate(installer, UpdateChecker.LaunchInstaller,
                () => TrayForm.OfferManualInstallation(owner: null, LocalizationManager.GetResource("UpdateInstallFailed")));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, LocalizationManager.GetResource("AppTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    internal static int FinishUpdate(DownloadedInstaller installer, Func<DownloadedInstaller, bool> launch, Action offerManualInstallation)
    {
        bool cancelled = false;
        try { if (launch(installer)) return 0; }
        catch (OperationCanceledException) { cancelled = true; }
        File.Delete(installer.Path);
        if (cancelled) return 0;
        offerManualInstallation();
        return 1;
    }
}
