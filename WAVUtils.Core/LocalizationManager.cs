using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Text.Json;

namespace WAVUtils.Core;

public static class LocalizationManager
{
    private static readonly string ConfigPath = Path.Combine(
        AppContext.BaseDirectory,
        "config.json");

    private static readonly ResourceManager CoreResources = new(
        "WAVUtils.Core.Resources",
        Assembly.GetExecutingAssembly());

    public static string CurrentLanguage { get; private set; } = "en";

    public static void Initialize()
    {
        var config = ReadConfig();
        var lang = config?.Language ?? "auto";

        if (lang == "auto")
        {
            var osLang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            CurrentLanguage = SupportedLanguages.ContainsKey(osLang)
                ? osLang
                : "en";
        }
        else
        {
            CurrentLanguage = SupportedLanguages.ContainsKey(lang)
                ? lang
                : "en";
        }

        Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(CurrentLanguage);
    }

    public static void SetLanguage(string language)
    {
        if (!SupportedLanguages.ContainsKey(language))
            language = "en";

        CurrentLanguage = language;
        Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        WriteConfig(language);
    }

    public static string GetResource(string key)
    {
        var resource = CoreResources.GetString(key, Thread.CurrentThread.CurrentUICulture);
        return resource ?? CoreResources.GetString(key, CultureInfo.GetCultureInfo("en"))
                     ?? key;
    }

    private static ConfigData? ReadConfig()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return null;
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<ConfigData>(json);
        }
        catch { return null; }
    }

    private static void WriteConfig(string language)
    {
        try
        {
            var config = new { language };
            var json = JsonSerializer.Serialize(config);
            File.WriteAllText(ConfigPath, json);
        }
        catch { }
    }

    private static readonly Dictionary<string, string> SupportedLanguages = new()
    {
        { "en", "English" },
        { "ru", "Русский" },
        { "de", "Deutsch" },
        { "fr", "Français" },
        { "es", "Español" },
        { "ja", "日本語" },
        { "zh", "简体中文" }
    };

    private class ConfigData { public string? Language { get; set; } }
}
