using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace WAVUtils.Core;

public class UpdateInfo
{
    public bool UpdateAvailable { get; set; }
    public string? CurrentVersion { get; set; }
    public string? LatestVersion { get; set; }
    public string? DownloadUrl { get; set; }
    public string? ReleaseNotes { get; set; }
    public long? FileSize { get; set; }
    public string? ErrorMessage { get; set; }
}

public static class UpdateChecker
{
    private const string GitHubApiUrl = "https://api.github.com/repos/wtfsoftware/WAVUtils/releases/latest";
    private const string ConfigKey = "checkUpdates";

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
        DefaultRequestHeaders =
        {
            UserAgent = { new System.Net.Http.Headers.ProductInfoHeaderValue("WAVUtils", "1.0.0") }
        }
    };

    public static async Task<UpdateInfo> CheckForUpdatesAsync()
    {
        var info = new UpdateInfo
        {
            CurrentVersion = GetAssemblyVersion()
        };

        try
        {
            var latestRelease = await FetchLatestReleaseAsync();
            if (latestRelease == null)
            {
                info.ErrorMessage = LocalizationManager.GetResource("UpdateCheckFailed");
                return info;
            }

            info.LatestVersion = latestRelease.TagName.TrimStart('v');
            info.UpdateAvailable = IsNewerVersion(info.CurrentVersion, info.LatestVersion);
            info.DownloadUrl = latestRelease.GetInstallerDownloadUrl();
            info.ReleaseNotes = latestRelease.Body;
            info.FileSize = latestRelease.GetInstallerSize();
        }
        catch (Exception ex)
        {
            info.ErrorMessage = string.Format(
                LocalizationManager.GetResource("UpdateCheckError"),
                ex.Message.Length > 100 ? ex.Message[..100] : ex.Message);
        }

        return info;
    }

    public static async Task<bool> DownloadAndInstallAsync(string downloadUrl, string installPath)
    {
        try
        {
            var tempFile = Path.Combine(
                Path.GetTempPath(),
                $"WAVUtils-Update-{DateTime.Now:yyyyMMddHHmmss}.exe");

            var progress = new DownloadProgress();
            var totalDownloaded = 0L;

            using var response = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var totalBytes = response.Content.Headers.ContentLength;
            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                totalDownloaded += bytesRead;
                progress.Update(totalDownloaded, totalBytes);
            }

            fileStream.Flush();
            fileStream.Close();

            return RunInstaller(tempFile, installPath);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<GithubRelease?> FetchLatestReleaseAsync()
    {
        try
        {
            var response = await HttpClient.GetAsync(GitHubApiUrl);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<GithubRelease>(json);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsNewerVersion(string? current, string? latest)
    {
        if (string.IsNullOrEmpty(current) || string.IsNullOrEmpty(latest))
            return false;

        try
        {
            var currentVer = new Version(current.TrimStart('v'));
            var latestVer = new Version(latest.TrimStart('v'));
            return latestVer > currentVer;
        }
        catch
        {
            return false;
        }
    }

    private static string GetAssemblyVersion()
    {
        try
        {
            var version = Assembly.GetEntryAssembly()?.GetName().Version;
            return version?.ToString() ?? "1.0.0";
        }
        catch
        {
            return "1.0.0";
        }
    }

    private static bool RunInstaller(string installerPath, string installPath)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = $"/D=\"{installPath}\" /VERYSILENT /NORESTART",
                UseShellExecute = true,
                Verb = "runas"
            };

            System.Diagnostics.Process.Start(startInfo)?.WaitForExit();
            File.Delete(installerPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private class GithubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("body")]
        public string Body { get; set; } = "";

        [JsonPropertyName("assets")]
        public GithubAsset[] Assets { get; set; } = [];

        public string GetInstallerDownloadUrl()
        {
            foreach (var asset in Assets)
            {
                if (asset.Name.EndsWith("-Setup-1.0.0.exe", StringComparison.OrdinalIgnoreCase))
                {
                    return asset.BrowserDownloadUrl;
                }
            }

            if (Assets.Length > 0)
            {
                return Assets[0].BrowserDownloadUrl;
            }

            return "";
        }

        public long? GetInstallerSize()
        {
            foreach (var asset in Assets)
            {
                if (asset.Name.EndsWith("-Setup-1.0.0.exe", StringComparison.OrdinalIgnoreCase))
                {
                    return asset.Size;
                }
            }
            return null;
        }
    }

    private class GithubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    private class DownloadProgress
    {
        public void Update(long downloaded, long? total)
        {
            if (total.HasValue && total.Value > 0)
            {
                var percent = (int)((downloaded * 100.0) / total.Value);
                if (percent % 10 == 0)
                {
                    Console.WriteLine($"  Downloading: {percent}%");
                }
            }
        }
    }
}
