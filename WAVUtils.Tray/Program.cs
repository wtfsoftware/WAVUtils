using System.Windows.Forms;
using WAVUtils.Core;

namespace WAVUtils.Tray;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        LocalizationManager.Initialize();
        Application.Run(new TrayForm());
    }
}
