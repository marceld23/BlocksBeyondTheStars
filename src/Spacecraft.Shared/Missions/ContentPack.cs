namespace Spacecraft.Shared.Missions;

/// <summary>
/// A portable bundle of admin-created server content (technical requirements /
/// `anf_admin_blueprinf.md` §12). Exported/imported as JSON so server owners can share,
/// back up and version their custom content. The MVP carries missions; blueprint/recipe
/// overlays are reserved for a later extension.
/// </summary>
public sealed class ContentPack
{
    public string Name { get; set; } = "content-pack";
    public int Version { get; set; } = 1;

    public List<MissionDefinition> Missions { get; set; } = new();
}
