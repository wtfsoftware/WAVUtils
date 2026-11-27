using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WAVUtils.App;

namespace WAVUtils.Tests;

[TestClass]
public sealed class DesktopRegressionTests
{
    [TestMethod]
    public void Finding42ListingInputsDoesNotAdvanceAudioProgress() => OnSta(() =>
    {
        using var window = new ProgressWindow(2);
        window.AddFile("a.wav");
        window.AddFile("b.wav");
        Assert.AreEqual(0, window.Controls.OfType<ProgressBar>().Single().Value);
    });

    [TestMethod]
    [DataRow(1f)]
    [DataRow(1.5f)]
    [DataRow(2f)]
    public void Finding42RenderedControlsFitTheClientArea(float scale) => OnSta(() =>
    {
        using var window = new ProgressWindow(2);
        window.Show();
        window.Scale(new SizeF(scale, scale));
        window.PerformLayout();
        foreach (Control control in window.Controls)
            Assert.IsTrue(window.ClientRectangle.Contains(control.Bounds), $"{control.GetType().Name} {control.Bounds} exceeds {window.ClientRectangle}.");
    });

    [TestMethod]
    [DataRow("unknown-command")]
    [DataRow("merge")]
    [DataRow("split")]
    [DataRow("tc --from donor.wav")]
    public void Finding44BadCliArgumentsReturnFailure(string arguments)
    {
        var start = new ProcessStartInfo(ExecutablePath) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in arguments.Split(' ')) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        try
        {
            Assert.IsTrue(process.WaitForExit(10000), "Argument errors must not leave a resident process.");
            Assert.AreNotEqual(0, process.ExitCode);
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

    internal static string ExecutablePath
    {
        get
        {
            string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../WAVUtils.App/bin", configuration, "net10.0-windows/WAVUtils.exe"));
            Assert.IsTrue(File.Exists(path), "Build the App project before desktop checks.");
            return path;
        }
    }

    internal static void OnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "Desktop test exceeded its deadline.");
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
