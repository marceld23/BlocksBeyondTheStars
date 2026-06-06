namespace Spacecraft.Shared.Definitions;

/// <summary>
/// A world's generated flora species: a coined name + a toxic/edible trait layered onto a fixed visual
/// archetype (an existing <c>flora_*</c> block, recoloured uniformly by the planet's flora hue). The roster
/// is derived deterministically from the world seed + planet (see <c>FloraGenerator</c>), so a given world
/// always names and classifies its plants the same way without storing anything. Purely a per-world identity
/// over the shared archetype blocks — the block still owns its mesh placement, drops and texture.
/// </summary>
public sealed class FloraSpecies
{
    /// <summary>Per-world species id ("fl0", "fl1", …).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Coined, pronounceable species name (e.g. "Skarnweed"), shown to the player on scan.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The archetype block this species is rendered as (a <c>flora_*</c> block key).</summary>
    public string BlockKey { get; set; } = string.Empty;

    /// <summary>Whether the plant is toxic (vs. edible/benign) — reported on scan.</summary>
    public bool Toxic { get; set; }

    /// <summary>True for in-water plants (kelp / lily) — they live in the seas, not on land.</summary>
    public bool Aquatic { get; set; }
}
