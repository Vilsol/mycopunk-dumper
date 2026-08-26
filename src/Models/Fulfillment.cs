namespace MycopunkDumper;

/// <summary>
/// A <c>Fulfillment</c> ScriptableObject — the in-game achievement/challenge catalog.
/// Distinct from the <c>achievements</c> section, which is just the list of Steam
/// achievement API IDs; a Fulfillment carries its own completion condition, score,
/// and reward, and only optionally maps onto a platform achievement.
/// Subclass-specific fields are null/absent when they don't apply.
/// </summary>
public class FulfillmentEntry
{
    public string ID;
    public string Subclass;                  // C# class name of the concrete Fulfillment subclass
    public string Description;               // resolved via GetDescription() where it doesn't need live player data
    public string DescriptionID;             // overrideDescription, or ID when empty — the key GetDescription() resolves
    public string Category;                  // Career | Mission | HUB | Gear
    public string CategoryName;              // localized category label
    public string PlatformAchievementID;     // Steam achievement unlocked alongside; empty for in-game-only entries
    public bool IsHidden;
    public int Score;
    public int CompletionThreshold;
    public bool UseThresholdAsDescParam;
    public Gear.LevelUnlockEntry Reward;     // reward.LevelUnlock
    public string RewardGear;                // GearInfo APIName the reward is applied to

    // ElementalFulfillment
    public string Element;                   // EffectType — Fire/Shock/Acid drive different PlayerData counters
    // FlagFulfillment
    public string FlagName;
    // LevelFulfillment
    public string TargetGear;                // GearInfo APIName; null means player level rather than gear level
    // MissionFulfillment
    public string Mission;                   // mission ID
    public string MissionContainer;          // container ID
    public int? CompletionCount;
    // OuroborosFulfillment
    public int? MinThreat;                   // 0 means the threshold is a room count with no threat floor
    // UpgradeFulfillment
    public bool? DiscoveredOnly;
    public bool? DiscoverAll;
    public string Rarities;                  // RarityFlags
    public bool? CountUpgrades;
    public bool? CountSkins;
    // EncounterFulfillment
    public string Encounter;                 // encounter object name
}
