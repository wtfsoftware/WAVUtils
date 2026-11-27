using System.Diagnostics;
using System.Globalization;
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
        Assert.AreEqual(0, ((ProgressBar)window.Controls.Find("OperationProgress", searchAllChildren: true).Single()).Value);
    });

    [TestMethod]
    [DataRow("en", 1f, false)]
    [DataRow("en", 1.5f, false)]
    [DataRow("ru", 2f, false)]
    [DataRow("ru", 2f, true)]
    public void Finding42RenderedControlsFitTheClientArea(string language, float scale, bool constrainWidth) => OnSta(() =>
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        using var window = new ProgressWindow(2);
        AssertScaledLayout(window, scale, constrainWidth);
    });

    internal static void AssertScaledLayout(Form window, float scale, bool constrainWidth)
    {
        window.Show();
        int maximumWidth = SystemInformation.MaxWindowTrackSize.Width;
        int requestedWidth = maximumWidth + Math.Max(128, maximumWidth / 8);
        // Scale past the real WinForms limit without changing display settings.
        float widthScale = constrainWidth ? requestedWidth / (float)window.ClientSize.Width : scale;
        window.Scale(new SizeF(widthScale, scale));
        window.PerformLayout();
        if (constrainWidth)
            Assert.IsTrue(window.Width <= maximumWidth && window.ClientSize.Width < requestedWidth - 64,
                $"Expected WinForms to cap requested client width {requestedWidth}, got {window.ClientSize.Width}.");

        AssertChildrenFit(window);
        var action = (Button)window.CancelButton!;
        Assert.IsTrue(action.CanSelect);
        Size text = TextRenderer.MeasureText(action.Text, action.Font);
        Assert.IsTrue(text.Width <= action.ClientSize.Width && text.Height <= action.ClientSize.Height);
    }

    private static void AssertChildrenFit(Control container)
    {
        var children = container.Controls.Cast<Control>().ToArray();
        for (int i = 0; i < children.Length; i++)
        {
            Control control = children[i];
            Assert.IsTrue(control.Visible && control.Width > 0 && control.Height > 0 && container.ClientRectangle.Contains(control.Bounds),
                $"{control.Name}: {control.Bounds} is not visible inside {container.ClientRectangle}.");
            for (int j = 0; j < i; j++)
                Assert.IsFalse(children[j].Bounds.IntersectsWith(control.Bounds), $"{children[j].Name} overlaps {control.Name}.");
            AssertChildrenFit(control);
        }
    }

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
