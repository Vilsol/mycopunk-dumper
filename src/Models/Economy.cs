using System.Collections.Generic;

namespace MycopunkDumper;

// Global reward economy + drop chances. Threat arrays indexed by threat level - 1; intensity arrays by intensity (0..2).
public class Economy
{
    public int BaseMissionXP;
    public int BaseScripForMissionCompletion;
    public int BaseScripForSideObjective;
    public int BaseXPForSideObjective;
    public int BaseResourcesForSideObjective;
    public float[] ResourceAmountMultiplier;
    public float[] IntensityScripAndXPMultiplier;

    public Dictionary<string, float> EnemyUpgradeDropChances;  // keyed by EnemyType
    public float CombatResourceSpawnChance;
    public EnemyPartList[] GlobalEnemyAttachments;
    public WeightedEntry[] RarityWeights;                       // keyed by rarity name (matches `rarities`)
    public WeightedEntry[] SkinRarityWeights;
    public RandomReward[] RandomRewards;                         // Global.randomRewards — enemy-core resource drops

    public class RandomReward
    {
        public string Resource;   // resource ID; null = no drop
        public int Count;
        public int Weight;
    }
}
