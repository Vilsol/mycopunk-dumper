namespace MycopunkDumper;

public class Gear
{
    public string ID;
    public string Name;
    public string APIName;
    public string TypeName;
    public string Description;
    public string GearType;
    public int MaxLevel;
    public int MinUnlockLevel;
    public bool UnlockAutomatically;
    public bool HideWhenNotCollected;
    public bool CanGainXP;
    public bool HasUpgradeGrid;
    public float XPGainMultiplier;
    public float DirectiveKillsMultiplier;
    public Upgrade.DUnlockCost[] UnlockCost;
    public GridSize[] GridSizes;
    public int SkinCount;
    public LevelUnlockEntry[] LevelUnlocks;
    public Upgrade.DIcon Icon;
    [Newtonsoft.Json.JsonConverter(typeof(NativeConverter))]
    public IUpgradable RawData;

    public class GridSize
    {
        public int Width;
        public int Height;
    }

    public class LevelUnlockEntry
    {
        public string Type;
        public int Level;
        public int Count;
        public float Chance;

        // LevelUnlock_Resource
        public Upgrade.DUnlockCost Resource;

        // LevelUnlock_MultipleUpgrades
        public bool? PrioritizeUndiscovered;
        public string[] Upgrades;

        // LevelUnlock_UpgradeRarity / LevelUnlock_SkinRarity / LevelUnlock_RarityReward
        public string Rarity;

        // LevelUnlock_XP
        public int? XP;

        // LevelUnlock_Upgrade — single upgrade key
        // LevelUnlock_SeededSkin — single SkinUpgrade key (paired with Seed)
        public string Upgrade;

        // LevelUnlock_LootPool
        public string LootPool;
        public int? MinLevel;

        // LevelUnlock_Gear
        public string Gear;
        public bool? Unlock;

        // LevelUnlock_IntroUpgrade — pair of upgrades shown in tutorial
        public string Upgrade1;
        public string Upgrade2;

        // LevelUnlock_Preview
        public string Preview;

        // LevelUnlock_SeededSkin
        public int? Seed;
    }
}

public class CharacterEntry : Gear
{
    public int Index;
    public bool IsPlayable;
    public string EmployeeID;
    public string UIColor;
    public string TextColor;
    public string ParticleColor;
    public string PrimaryColorTag;
    public string TextColorTag;
    public Quip[] Quips;
    public Quip[] DefaultEmotes;
    public string DefaultUpgradeType;
    public string DefaultSkinType;
    public SkillTreeNode[] SkillTree;            // skill-tree node graph for this character (UpgradeTree-flagged upgrades)

    public class SkillTreeNode
    {
        public string Upgrade;                   // upgrade key (matches `upgrades` map)
        public int Layer;                        // tier (0 = root, increases outward)
        public int MinSpentSkillPointsToUnlock;  // computed: layer == 0 ? 0 : 2 + (layer - 1) * 5
        public int CoordX;                       // grid coordinate in the visual tree
        public int CoordY;
        public string MustBeUnlockedFirst;       // upgrade key — prerequisite node (null/empty if root-layer)
    }

    public class Quip
    {
        public int ID;
        public int Index;
        public string APIName;
        public string Label;
        public string VoicelineTextID;
        public string QuipType;
        public bool TriggerOnFire;
        public bool HasVoiceline;
        public Upgrade.DIcon Icon;
    }
}
