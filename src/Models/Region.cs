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
    public int MinThreat;
    public int MaxThreat;
    public bool ExcludeFromOvertimeAssignment;
    public string Planet;                // key into `planets`
    public string[] DefaultModifiers;    // keys into `missionModifiers`
    public Upgrade.DIcon Icon;
    public MissionEntry.SceneRef[] Scenes;
    public Profile[] WorldProfiles;

    public class Profile
    {
        public string Name;                  // WorldProfile asset name
        public string LocationNameID;
        public string LocationName;          // localized
        public string[] DefaultEncounters;   // keys into `encounters`
    }
}
