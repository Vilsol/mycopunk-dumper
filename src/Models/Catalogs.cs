namespace MycopunkDumper;

/// <summary>
/// Special hex-grid pattern that grants rewards when the upgrade grid contains it.
/// 7 entries observed; rarities filter restricts which upgrade rarities trigger the pattern.
/// </summary>
public class PatternPathEntry
{
    public int ID;
    public string Name;            // ScriptableObject asset name
    public string Rarities;        // RarityFlags enum stringified (e.g. "Standard, Rare, Epic, Exotic")
    [Newtonsoft.Json.JsonConverter(typeof(NativeConverter))]
    public HexMap Pattern;
    public Gear.LevelUnlockEntry[] Rewards;
}

/// <summary>
/// Per-threat-level scaling curve. <see cref="Threats"/> is indexed by threat level
/// (index 0 = threat1, index 6 = threat7). Multiple profiles exist; each scales a different
/// game stat (e.g. enemy health multiplier, damage taken multiplier, …) — the consumer
/// distinguishes them by the asset <see cref="Name"/>.
/// </summary>
public class ThreatProfile
{
    public string Name;            // ScriptableObject asset name
    public float[] Threats;        // length 7 (one per threat tier)
}

/// <summary>
/// Wave composition spec. Mission waves draw from <see cref="Tags"/> and <see cref="Types"/>,
/// optionally weighted by <see cref="TagChances"/> / <see cref="TypeChances"/>. Used by
/// MissionModifier_WaveModifier and similar scheduling code to vary enemy mix per mission.
/// </summary>
public class CustomWaveEntry
{
    public string Name;
    public string Subclass;        // e.g. "GenericCustomWave"
    public string Tags;            // EnemyTags flag-enum stringified
    public string ExcludeTags;
    public string Types;           // EnemyTypeFlags flag-enum stringified
    public float EnemyCountMultiplier;
    public float OverclockChanceMultiplier;
    public string MinRoomSize;
    public int MinOuroRooms;
    public TagWeight[] TagChances;
    public TypeWeight[] TypeChances;
    public bool AddAttachments;
    public int OverrideIndividualEnemies;

    public class TagWeight { public string Tag; public float Multiplier; }
    public class TypeWeight { public string Type; public float Multiplier; }
}

/// <summary>
/// World-collectable definition (e.g. a pump that, when fully collected, awards an upgrade).
/// </summary>
public class CollectableEntry
{
    public string ID;              // nameID
    public string Name;            // resolved
    public string Color;
    public int Count;              // total uses required to "fill" the collectable
    public string PunchTextID;     // localization key for the on-punch line
    public string PunchText;       // resolved flavor text from PunchTextID, block 0
    public Upgrade.DIcon Icon;     // collectable icon (Sprite — texture + sub-rect)
    public Gear.LevelUnlockEntry[] Rewards;
}

/// <summary>
/// Weighted grouping of <see cref="EnemyClass"/> values used by spawn waves to vary enemy mix.
/// </summary>
public class EnemyGroup
{
    public string Name;
    public string EnemyType;       // EnemyType enum stringified
    public WeightedEnemy[] Enemies;

    public class WeightedEnemy
    {
        public string Enemy;       // enemy ID (matches a key in `enemies` map)
        public int Weight;
    }
}
