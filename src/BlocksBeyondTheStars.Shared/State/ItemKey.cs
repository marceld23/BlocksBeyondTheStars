using System.Globalization;

namespace BlocksBeyondTheStars.Shared.State;

/// <summary>
/// Helpers for the optional colour modifier carried by an item key. A dyed or glowing block is
/// represented as a <i>composite</i> item key: the base key, a <c>'#'</c> separator, then an
/// optional <c>t&lt;rrggbb&gt;</c> surface-tint and/or <c>g&lt;rrggbb&gt;</c> light-colour payload
/// (lowercase hex), e.g. <c>"mud#t3f6fb0"</c> or <c>"stone#g00ffff"</c>.
///
/// Keeping the modifier inside the key string means the whole inventory / crafting / networking /
/// persistence stack (all of which key on a plain item string + count) treats a dyed stack as a
/// distinct item automatically — a blue-mud stack never merges with plain mud, and no per-stack
/// metadata channel is needed. Only two chokepoints must be modifier-aware: definition lookups
/// (which strip back to the base key, see <c>GameContent.GetItem</c>/<c>MaxStackOf</c>) and the
/// place/mine flow (which reads the colour out to stamp/recover the per-voxel modifier).
///
/// A colour value of 0 means "none"; the palette avoids pure black (use 0x010101 if a near-black
/// tint is ever needed) so 0 stays an unambiguous sentinel.
/// </summary>
public static class ItemKey
{
    public const char Separator = '#';

    /// <summary>The base item key with any colour modifier stripped (a plain key is returned unchanged).</summary>
    public static string Base(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return key;
        }

        int hash = key.IndexOf(Separator);
        return hash < 0 ? key : key.Substring(0, hash);
    }

    /// <summary>True if the key carries a colour modifier (tint and/or glow).</summary>
    public static bool HasModifier(string key) => !string.IsNullOrEmpty(key) && key.IndexOf(Separator) >= 0;

    /// <summary>The 0xRRGGBB surface tint encoded in the key, or 0 if none.</summary>
    public static int Tint(string key) => Field(key, 't');

    /// <summary>The 0xRRGGBB light colour encoded in the key, or 0 if none.</summary>
    public static int Glow(string key) => Field(key, 'g');

    /// <summary>The shape index encoded in the key (see <see cref="World.BlockShape"/>), or 0 (plain cube) if none.
    /// Only the form is carried by the item; the placement orientation is decided from the player's facing.</summary>
    public static int Shape(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return 0;
        }

        int hash = key.IndexOf(Separator);
        return hash < 0 ? 0 : ReadShape(key.Substring(hash + 1));
    }

    /// <summary>Parses the key into its base + tint + glow in one pass.</summary>
    public static (string Base, int Tint, int Glow) Parse(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return (key ?? string.Empty, 0, 0);
        }

        int hash = key.IndexOf(Separator);
        if (hash < 0)
        {
            return (key, 0, 0);
        }

        string payload = key.Substring(hash + 1);
        return (key.Substring(0, hash), ReadColour(payload, 't'), ReadColour(payload, 'g'));
    }

    /// <summary>
    /// Builds a composite key from a base key and colours. Returns the bare base key when both
    /// colours are 0 (so plain items never gain a needless suffix). Any existing modifier on
    /// <paramref name="baseKey"/> is dropped first.
    /// </summary>
    public static string Compose(string baseKey, int tint, int glow) => Compose(baseKey, tint, glow, 0);

    /// <summary>
    /// Builds a composite key from a base key, colours and a shape index (see <see cref="World.BlockShape"/>).
    /// Returns the bare base key when all modifiers are absent. Any existing modifier on
    /// <paramref name="baseKey"/> is dropped first. The order is always <c>t</c>, <c>g</c>, then <c>s</c>.
    /// </summary>
    public static string Compose(string baseKey, int tint, int glow, int shape)
    {
        string root = Base(baseKey);
        tint &= 0xFFFFFF;
        glow &= 0xFFFFFF;
        shape &= 0xFF;
        if (tint == 0 && glow == 0 && shape == 0)
        {
            return root;
        }

        var sb = new System.Text.StringBuilder(root.Length + 18);
        sb.Append(root).Append(Separator);
        if (tint != 0)
        {
            sb.Append('t').Append(tint.ToString("x6", CultureInfo.InvariantCulture));
        }

        if (glow != 0)
        {
            sb.Append('g').Append(glow.ToString("x6", CultureInfo.InvariantCulture));
        }

        if (shape != 0)
        {
            sb.Append('s').Append(shape.ToString("x2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private static int Field(string key, char tag)
    {
        if (string.IsNullOrEmpty(key))
        {
            return 0;
        }

        int hash = key.IndexOf(Separator);
        return hash < 0 ? 0 : ReadColour(key.Substring(hash + 1), tag);
    }

    /// <summary>Reads the 6 hex digits following <paramref name="tag"/> in the payload, or 0 if absent/malformed.</summary>
    private static int ReadColour(string payload, char tag)
    {
        int at = payload.IndexOf(tag);
        if (at < 0 || at + 7 > payload.Length)
        {
            return 0;
        }

        return int.TryParse(payload.AsSpan(at + 1, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb)
            ? rgb & 0xFFFFFF
            : 0;
    }

    /// <summary>Reads the 2 hex digits following the <c>'s'</c> shape tag in the payload, or 0 if absent/malformed.</summary>
    private static int ReadShape(string payload)
    {
        int at = payload.IndexOf('s');
        if (at < 0 || at + 3 > payload.Length)
        {
            return 0;
        }

        return int.TryParse(payload.AsSpan(at + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int s)
            ? s & 0xFF
            : 0;
    }
}
