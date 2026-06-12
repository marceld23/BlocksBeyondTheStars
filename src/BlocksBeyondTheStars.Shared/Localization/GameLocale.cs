namespace BlocksBeyondTheStars.Shared.Localization;

/// <summary>
/// Supported in-game languages. The game must be playable in both German and English;
/// English is the fallback locale.
/// </summary>
public enum GameLocale
{
    English,
    German,
}

public static class GameLocaleExtensions
{
    /// <summary>The file-name code used for locale resource files, e.g. "en", "de".</summary>
    public static string Code(this GameLocale locale) => locale switch
    {
        GameLocale.English => "en",
        GameLocale.German => "de",
        _ => "en",
    };

    public static bool TryParse(string code, out GameLocale locale)
    {
        switch (code?.Trim().ToLowerInvariant())
        {
            case "en":
            case "en-us":
            case "english":
                locale = GameLocale.English;
                return true;
            case "de":
            case "de-de":
            case "german":
            case "deutsch":
                locale = GameLocale.German;
                return true;
            default:
                locale = GameLocale.English;
                return false;
        }
    }
}
