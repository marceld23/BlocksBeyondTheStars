using System.Globalization;
using System.Text.Json;

namespace BlocksBeyondTheStars.Launcher;

/// <summary>
/// Resolves the localized "Loading…" splash text the same way the game would, so the pre-engine splash
/// matches the player's chosen in-game language instead of the OS language. The launcher runs before Unity,
/// so it cannot ask the running game; instead it reads two on-disk artifacts the game itself owns:
/// <list type="bullet">
/// <item>the chosen language from <c>client_settings.json</c> (<c>Language</c> = "en"/"de"), under the Unity
/// <c>persistentDataPath</c> (<c>%USERPROFILE%\AppData\LocalLow\{company}\{product}</c>);</item>
/// <item>the actual string from the game's own locale table shipped next to the exe at
/// <c>{exeDir}/BlocksBeyondTheStars_Data/StreamingAssets/data/locales/{code}.json</c>, key
/// <c>ui.loading.title</c> — the single source of truth, so new languages need no launcher change.</item>
/// </list>
/// Every step falls back gracefully, ending at a hardcoded English string so the splash always has text.
/// </summary>
internal static class SplashLocalization
{
    // Must match Unity Player Settings (client/ProjectSettings/ProjectSettings.asset) — these compose the
    // persistentDataPath where client_settings.json lives.
    private const string CompanyName = "JuMaVe Games";
    private const string ProductName = "Blocks Beyond the Stars";

    // The game's data folder sits next to the exe (and next to the launcher) — derived from the exe name.
    private const string DataFolderName = "BlocksBeyondTheStars_Data";

    private const string LoadingKey = "ui.loading.title";
    private const string LoadingFallback = "Loading …";

    /// <summary>The localized "Loading…" text for the player's chosen language, with English and a hardcoded
    /// fallback. <paramref name="baseDirectory"/> is the launcher/exe directory (the locale files live under it).</summary>
    internal static string LoadingText(string baseDirectory)
    {
        string lang = ResolveLanguage();
        return ReadLoadingString(baseDirectory, lang)
            ?? ReadLoadingString(baseDirectory, "en")
            ?? LoadingFallback;
    }

    /// <summary>The player's chosen language code from client_settings.json, falling back to the OS UI
    /// language's two-letter code, then "en".</summary>
    private static string ResolveLanguage()
    {
        try
        {
            string settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData", "LocalLow", CompanyName, ProductName, "client_settings.json");
            if (File.Exists(settingsPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
                if (doc.RootElement.TryGetProperty("Language", out var langProp)
                    && langProp.ValueKind == JsonValueKind.String)
                {
                    string? lang = langProp.GetString();
                    if (!string.IsNullOrWhiteSpace(lang))
                    {
                        return lang.Trim();
                    }
                }
            }
        }
        catch
        {
            // Unreadable/corrupt settings — fall through to the OS language.
        }

        try
        {
            string os = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            if (!string.IsNullOrWhiteSpace(os))
            {
                return os;
            }
        }
        catch
        {
            // Ignore and use the final default.
        }

        return "en";
    }

    /// <summary>Reads <c>ui.loading.title</c> from the game's locale table for <paramref name="lang"/>, or
    /// null if the file/key is missing or unreadable.</summary>
    private static string? ReadLoadingString(string baseDirectory, string lang)
    {
        try
        {
            string localePath = Path.Combine(
                baseDirectory, DataFolderName, "StreamingAssets", "data", "locales", lang + ".json");
            if (!File.Exists(localePath))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(localePath));
            if (doc.RootElement.TryGetProperty(LoadingKey, out var prop)
                && prop.ValueKind == JsonValueKind.String)
            {
                string? text = prop.GetString();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
        }
        catch
        {
            // Missing or malformed locale file — let the caller fall back.
        }

        return null;
    }
}
