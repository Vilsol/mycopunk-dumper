namespace MycopunkDumper;

/// <summary>
/// A <c>Planet</c> ScriptableObject — its biome composition. <c>BiomeData</c> is the raw
/// <c>planetBiomeData</c> int array the world generator uses to lay out regions on the planet.
/// </summary>
public class PlanetEntry
{
    public string NameID;                // TextBlocks key
    public string Name;                  // localized (block 0 of NameID)
    public string OverrideSubtitleID;    // TextBlocks key; null → subtitle is block 1 of NameID
    public string Subtitle;              // localized, resolved as the game does
    public Upgrade.DIcon Icon;
    public int MinLevel;                 // player level required; 0 = never locked
    public string DailyModifierRegion;   // key into `regions`
    public bool IsHazardous;
    public int[] BiomeData;
}
