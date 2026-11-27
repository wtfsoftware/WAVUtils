using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WAVUtils.Tests;

[TestClass]
public sealed class ScriptRegressionTests
{
    [TestMethod]
    [DataRow("parse")]
    [DataRow("registry")]
    [DataRow("registry-uninstall")]
    [DataRow("installer-profile")]
    [DataRow("tasks")]
    [DataRow("installer-launch")]
    [DataRow("installer-log")]
    [DataRow("installer-payload")]
    [DataRow("publish")]
    [DataRow("publish-missing-lock")]
    [DataRow("native-error")]
    [DataRow("fallback")]
    [DataRow("http")]
    public async Task Finding45ScriptsPassParsingAndSandboxChecks(string scenario)
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(root, "WAVUtils.Tests", "ScriptChecks.ps1"), "-Root", root, "-Scenario", scenario }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        Assert.AreEqual(0, process.ExitCode, (await output) + "\n" + (await error));
    }
}
