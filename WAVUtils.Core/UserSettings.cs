using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WAVUtils.Core;

/// <summary>Reads user settings and atomically changes a single preference.</summary>
public static class UserSettings
{
    private static readonly string UserFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WAVUtils", "config.json");
    private static readonly string LegacyFile = Path.Combine(AppContext.BaseDirectory, "config.json");

    public static string ReadConfig() => ReadConfigAt(UserFile, LegacyFile);

    internal static string ReadConfigAt(string path, string? legacyPath = null)
    {
        try
        {
            string json = ReadStoredConfig(path, legacyPath);
            _ = Parse(json);
            return json;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            Trace.TraceWarning("WAVUtils could not read settings. The file is unchanged and automatic update checks are disabled.");
            return """{"checkUpdates":false}""";
        }
    }

    public static void SetValue(string key, object value) => SetValueAt(UserFile, key, value, LegacyFile);

    internal static void SetValueAt(string path, string key, object value, string? legacyPath = null)
    {
        using var mutex = new Mutex(initiallyOwned: false, @"Local\WAVUtils.Settings");
        bool acquired;
        try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new IOException("Settings are busy; try again.");
        string? temporary = null;
        try
        {
            var config = Parse(ReadStoredConfig(path, legacyPath));
            config[key] = JsonSerializer.SerializeToNode(value);
            byte[] bytes = Encoding.UTF8.GetBytes(config.ToJsonString());
            if (bytes.Length > 262144) throw new InvalidDataException("Settings file is too large.");
            string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            Directory.CreateDirectory(directory);
            temporary = Path.Combine(directory, ".wavutils-settings-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
            temporary = null;
        }
        finally
        {
            mutex.ReleaseMutex();
            if (temporary != null) File.Delete(temporary);
        }
    }

    private static string ReadStoredConfig(string path, string? legacyPath) =>
        ReadConfigFile(path) ?? (legacyPath == null ? null : ReadConfigFile(legacyPath)) ?? "{}";

    private static string? ReadConfigFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length > 262144) throw new InvalidDataException("Settings file is too large.");
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static JsonObject Parse(string json) => JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = 16 }) as JsonObject
        ?? throw new InvalidDataException("Settings must be a JSON object.");
}
