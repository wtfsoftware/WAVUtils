using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
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
    public string? Sha256 { get; set; }
}

public sealed class DownloadedInstaller
{
    public required string Path { get; init; }
    public required string Sha256 { get; init; }
}

public static class UpdateChecker
{
    private const string GitHubApiUrl = "https://api.github.com/repos/wtfsoftware/WAVUtils/releases/latest";
    public const string OfficialReleasePage = "https://github.com/wtfsoftware/WAVUtils/releases/latest";
    internal const long MaxInstallerBytes = 512L * 1024 * 1024;

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
        DefaultRequestHeaders =
        {
            UserAgent = { new System.Net.Http.Headers.ProductInfoHeaderValue("WAVUtils", "1.0.0") }
        }
    };

    public static async Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var info = new UpdateInfo
        {
            CurrentVersion = GetAssemblyVersion()
        };

        try
        {
            var latestRelease = await FetchLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
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
            info.Sha256 = latestRelease.GetInstallerSha256();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            info.ErrorMessage = string.Format(
                LocalizationManager.GetResource("UpdateCheckError"),
                ex.Message.Length > 100 ? ex.Message[..100] : ex.Message);
        }

        return info;
    }

    public static async Task<bool> DownloadAndInstallAsync(string downloadUrl, string installPath)
    {
        DownloadedInstaller? installer = null;
        bool launched = false;
        try
        {
            var update = await CheckForUpdatesAsync().ConfigureAwait(false);
            if (update.DownloadUrl != downloadUrl) return false;
            installer = await DownloadInstallerAsync(update).ConfigureAwait(false);
            // Keep the legacy API parameter; Inno Setup resolves the destination itself.
            return launched = LaunchInstaller(installer);
        }
        catch (IOException) { return false; }
        catch (HttpRequestException) { return false; }
        catch (InvalidDataException) { return false; }
        catch (OperationCanceledException) { return false; }
        finally { if (!launched && installer != null) File.Delete(installer.Path); }
    }

    public static Task<DownloadedInstaller> DownloadInstallerAsync(UpdateInfo update,
        Action<double, string>? progress = null, CancellationToken cancellationToken = default) =>
        DownloadInstallerAsync(update, client: HttpClient, directory: Path.GetTempPath(), progress, cancellationToken);

    internal static async Task<DownloadedInstaller> DownloadInstallerAsync(UpdateInfo update, HttpClient client,
        string directory, Action<double, string>? progress, CancellationToken cancellationToken)
    {
        if (update.DownloadUrl is not { } url || !IsOfficialAsset(url, update.LatestVersion) ||
            update.Sha256 is not { } digest || !ValidDigest(digest) ||
            update.FileSize is not long size || size <= 0 || size > MaxInstallerBytes)
            throw new InvalidDataException("No verified official installer is available; use the official release page.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(10));
        string path = Path.Combine(directory, "WAVUtils-Update-" + Guid.NewGuid().ToString("N") + ".exe");
        bool complete = false;
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != size)
                throw new InvalidDataException("Installer Content-Length does not match its release metadata.");
            using var source = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, useAsync: true);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[81920];
            long downloaded = 0;
            while (true)
            {
                using var readDeadline = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                readDeadline.CancelAfter(TimeSpan.FromSeconds(30));
                int count = await source.ReadAsync(buffer, readDeadline.Token).ConfigureAwait(false);
                if (count == 0) break;
                downloaded = checked(downloaded + count);
                if (downloaded > size) throw new InvalidDataException("Installer exceeds its declared size.");
                await output.WriteAsync(buffer.AsMemory(0, count), deadline.Token).ConfigureAwait(false);
                hash.AppendData(buffer, 0, count);
                progress?.Invoke(0.95 * downloaded / size, LocalizationManager.GetResource("UpdateDownloadStart"));
            }
            if (downloaded != size || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(digest)))
                throw new InvalidDataException("Installer length or SHA-256 verification failed.");
            deadline.Token.ThrowIfCancellationRequested();
            output.Flush(flushToDisk: true);
            ValidatePe(output);
            progress?.Invoke(1, LocalizationManager.GetResource("UpdateDownloadComplete"));
            deadline.Token.ThrowIfCancellationRequested();
            complete = true;
            return new DownloadedInstaller { Path = path, Sha256 = digest };
        }
        finally { if (!complete && File.Exists(path)) File.Delete(path); }
    }

    private static async Task<GithubRelease?> FetchLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await HttpClient.GetAsync(GitHubApiUrl, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var source = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
        using var output = new MemoryStream();
        byte[] buffer = new byte[81920];
        while (true)
        {
            int count = await source.ReadAsync(buffer, deadline.Token).ConfigureAwait(false);
            if (count == 0) break;
            if (output.Length + count > 1024 * 1024) throw new InvalidDataException("Release metadata is too large.");
            output.Write(buffer, 0, count);
        }
        return JsonSerializer.Deserialize<GithubRelease>(output.ToArray(), new JsonSerializerOptions { MaxDepth = 16 });
    }

    internal static bool IsNewerVersion(string? current, string? latest)
    {
        return Version.TryParse(current?.TrimStart('v'), out var installed) &&
            Version.TryParse(latest?.TrimStart('v'), out var released) &&
            WithMissingComponentsAsZero(released) > WithMissingComponentsAsZero(installed);

        static Version WithMissingComponentsAsZero(Version version) =>
            new(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
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

    /// <summary>Rechecks the downloaded installer and requests elevation to start Setup.</summary>
    /// <exception cref="OperationCanceledException">The user declines elevation.</exception>
    public static bool LaunchInstaller(DownloadedInstaller installer) => RunInstaller(installer, Process.Start);

    internal static bool RunInstaller(DownloadedInstaller installer, Func<ProcessStartInfo, Process?> start)
    {
        try
        {
            if (!ValidDigest(installer.Sha256)) return false;
            using var file = new FileStream(installer.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(file), Convert.FromHexString(installer.Sha256))) return false;
            ValidatePe(file);
            var startInfo = new ProcessStartInfo
            {
                FileName = installer.Path,
                UseShellExecute = true,
                Verb = "runas"
            };
            startInfo.ArgumentList.Add("/NORESTART");
            using var process = start(startInfo);
            return process != null; // Launch accepted, not proof that installation completed.
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED
        {
            throw new OperationCanceledException("The user declined installation.", ex);
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
            return Installer()?.BrowserDownloadUrl ?? "";
        }

        public long? GetInstallerSize()
        {
            return Installer()?.Size;
        }

        public string? GetInstallerSha256() => Installer()?.Digest?[7..];

        private GithubAsset? Installer()
        {
            if (TagName is not { } tag || InstallerName(tag.StartsWith('v') ? tag[1..] : tag) is not { } name) return null;
            string url = InstallerUrl(tag: tag, name: name);
            return Assets?.FirstOrDefault(asset => asset != null && asset.Name == name && asset.BrowserDownloadUrl == url &&
                asset.Size > 0 && asset.Size <= MaxInstallerBytes &&
                asset.Digest?.StartsWith("sha256:", StringComparison.Ordinal) == true && ValidDigest(asset.Digest[7..]));
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
        [JsonPropertyName("digest")]
        public string? Digest { get; set; }
    }

    private static bool ValidDigest(string? digest) => digest?.Length == 64 && digest.All(char.IsAsciiHexDigit);

    private static string? InstallerName(string? version) =>
        version != null && version.All(c => char.IsAsciiDigit(c) || c == '.') && Version.TryParse(version, out _)
            ? $"WAVUtils-Setup-{version}.exe" : null;

    private static string InstallerUrl(string tag, string name) =>
        $"https://github.com/wtfsoftware/WAVUtils/releases/download/{tag}/{name}";

    private static bool IsOfficialAsset(string url, string? version) => version != null && InstallerName(version) is { } name &&
        (url == InstallerUrl(tag: version, name: name) || url == InstallerUrl(tag: "v" + version, name: name));

    private static void ValidatePe(Stream file)
    {
        if (file.Length < 64) throw new InvalidDataException("Truncated installer PE envelope.");
        file.Position = 0;
        Span<byte> header = stackalloc byte[64];
        file.ReadExactly(header);
        int offset = BinaryPrimitives.ReadInt32LittleEndian(header[60..]);
        if (!header[..2].SequenceEqual("MZ"u8) || offset < 64 || offset > file.Length - 4)
            throw new InvalidDataException("Invalid installer PE envelope.");
        file.Position = offset;
        file.ReadExactly(header[..4]);
        if (!header[..4].SequenceEqual("PE\0\0"u8)) throw new InvalidDataException("Missing installer PE signature.");
    }
}
