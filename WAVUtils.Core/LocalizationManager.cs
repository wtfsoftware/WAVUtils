using System;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text.Json;

namespace WAVUtils.Core;

public static class LocalizationManager
{
    private static readonly ResourceManager CoreResources = new(
        "WAVUtils.Core.Resources",
        Assembly.GetExecutingAssembly());

    public static string CurrentLanguage { get; private set; } = "en";

    public static void Initialize()
    {
        var lang = GetPreferredLanguage(UserSettings.ReadConfig());

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

    internal static string GetPreferredLanguage(string json)
    {
        using var config = JsonDocument.Parse(json);
        return config.RootElement.TryGetProperty("language", out var language) && language.ValueKind == JsonValueKind.String
            ? language.GetString()!
            : "auto";
    }

    private static void WriteConfig(string language)
    {
        UserSettings.SetValue("language", language);
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
}
