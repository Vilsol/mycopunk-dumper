namespace MycopunkDumper;

public class Region
{
    public int ID;
    public string NameID;                // internal regionName field — short ID (e.g. "tundra", "desert")
    public string RegionName;            // localized display name
    public string ColoredRegionName;     // localized name with rich-text color tags
    public string LocalizedDescription;
    public string Flags;                 // LevelFlags stringified (comma-separated)
    public string Color;                 // RGBA(r, g, b, a)
    public bool LockRegion;
    public Upgrade.DIcon Icon;
    public MissionEntry.SceneRef[] Scenes;
}
