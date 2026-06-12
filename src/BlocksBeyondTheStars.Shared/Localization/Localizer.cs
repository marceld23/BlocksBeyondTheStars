namespace BlocksBeyondTheStars.Shared.Localization;

/// <summary>
/// Resolves localization keys to player-facing text for a chosen locale, falling back
/// to English when a key is missing in the active locale. Locale tables are plain
/// key→text dictionaries loaded from <c>data/locales/{code}.json</c>.
/// </summary>
public sealed class Localizer
{
    private readonly IReadOnlyDictionary<string, string> _active;
    private readonly IReadOnlyDictionary<string, string> _fallback;

    public GameLocale Locale { get; }

    public Localizer(
        GameLocale locale,
        IReadOnlyDictionary<string, string> activeTable,
        IReadOnlyDictionary<string, string> englishFallbackTable)
    {
        Locale = locale;
        _active = activeTable;
        _fallback = englishFallbackTable;
    }

    /// <summary>
    /// Returns the localized text for <paramref name="key"/>, or the English fallback,
    /// or the key itself wrapped in brackets if it is unknown everywhere.
    /// </summary>
    public string Get(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        if (_active.TryGetValue(key, out var text) || _fallback.TryGetValue(key, out text))
        {
            return text;
        }

        return $"[{key}]";
    }

    public bool Has(string key) => _active.ContainsKey(key) || _fallback.ContainsKey(key);
}
