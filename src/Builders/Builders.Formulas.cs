using System.Collections.Generic;
using System.Linq;

namespace MycopunkDumper;

partial class Plugin
{
    // Constants below are code literals transcribed from decompiled source; everything else is referenced by dump path.

    private static Formulas.Formula BuildRarityLevelBonus()
    {
        // The bonus slot is index 1 of Global.rarityWeights; name it from the live array rather than assuming.
        var items = Global.Instance.rarityWeights?.items;
        var slot = items != null && items.Length > 1 && items[1] != null ? RarityKey(items[1].value) : null;
        return new Formulas.Formula
        {
            Description = $"Global.GetWeightedRarity: the weight at economy.RarityWeights[1] ({slot}) gains a player-level bonus, then one weighted roll. Below player level 1, an Epic+ result is rerolled once, and an Exotic+ second result rerolled again.",
            Expression = "weights[1] = economy.RarityWeights[1].Weight + LevelBonus * clamp01((playerLevel - LevelFrom) / (LevelTo - LevelFrom)); rarity = weightedPick(weights); if (playerLevel < 1 && rarity >= epic) { rarity = weightedPick(weights); if (rarity >= exotic) rarity = weightedPick(weights); }",
            Inputs = new Dictionary<string, string>
            {
                ["weights"] = "economy.RarityWeights (array order)",
                ["bonusSlot"] = slot,
                ["playerLevel"] = "PlayerData.GetPlayerLevel()",
            },
            Constants = new Dictionary<string, float> { ["LevelBonus"] = 20f, ["LevelFrom"] = 8f, ["LevelTo"] = 22f },
        };
    }

    private static Formulas.Formula BuildDirectiveThreatBonus() => new()
    {
        Description = "DirectiveManager.ApplyThreatMultiplier: additive directive-progress bonus per threat level above 2, capped at 4 levels.",
        Expression = "progress + clamp(threat - ThreatOffset, 0, MaxSteps) * PerThreat",
        Inputs = new Dictionary<string, string>
        {
            ["progress"] = "directive progress before threat scaling",
            ["threat"] = "GameManager.ThreatLevel (1-based)",
        },
        Constants = new Dictionary<string, float> { ["ThreatOffset"] = 2f, ["MaxSteps"] = 4f, ["PerThreat"] = 0.075f },
    };

    private static Formulas.Formula BuildOuroBossHealth() => new()
    {
        Description = "OuroObjective.SpawnBossCoroutine: enemy health multiplier applied to the spawned Ouroboros boss. Boss is a weighted pick from finalBossClasses when roomsCompleted == finalBossRoom, else bossClasses, filtered to entries with minRoomsCompleted <= roomsCompleted <= maxRoomsCompleted (max <= 0 = unbounded).",
        Expression = "m = (enemyType == Brute ? EnemyHealthMultiplier * BruteMultiplier : enemyType == Abomination ? AbominationMultiplier : OtherMultiplier); if (boss.healthMultiplier > 0) m *= boss.healthMultiplier; i = max index >= 1 with roomsCompleted >= bossHealthMultipliers[i].y, else 0; m *= bossHealthMultipliers[i].x * bossHealthPlayerScaling[players] * bossHealthThreatScaling[threat]",
        Inputs = new Dictionary<string, string>
        {
            ["boss"] = "objectives.OuroObjective.RawData.bossClasses / finalBossClasses (items[].value)",
            ["finalBossRoom"] = "objectives.OuroObjective.RawData.finalBossRoom",
            ["bossHealthMultipliers"] = "objectives.OuroObjective.RawData.bossHealthMultipliers (x = multiplier, y = min roomsCompleted)",
            ["bossHealthPlayerScaling"] = "objectives.OuroObjective.RawData.bossHealthPlayerScaling (PlayerModifier: values[players-1], +multiplier per player beyond 4)",
            ["bossHealthThreatScaling"] = "objectives.OuroObjective.RawData.bossHealthThreatScaling (threats[threat-1])",
            ["EnemyHealthMultiplier"] = "EnemyManager.EnemyHealthMultiplier at spawn time (only kept for Brute)",
        },
        Constants = new Dictionary<string, float> { ["BruteMultiplier"] = 6f, ["AbominationMultiplier"] = 2f, ["OtherMultiplier"] = 0.65f },
    };

    private static Formulas.Formula BuildLotteryLarry() => new()
    {
        Description = "LotteryLarry: each spin costs larrytoken. Without a preselected skin, the result is a uniform pick over the gear's Info.Upgrades that are obtainable skins, not ExcludeFromWorldPool, and (if ExcludeFromWorldPoolPriorToUnlock) already collected. The skin seed is random; on a rare spin the final seed is the one with the lowest GetSkinChance among RareSpinCandidates seeds.",
        Expression = "cost = TokenCost * larrytoken; rareSpin = (larrySpinCounter % RareSpinEvery == 0) [counter before this spin]; skin = uniform(eligibleSkins); seed = rareSpin ? argmin_{RareSpinCandidates seeds} skin.GetSkinChance(gear, seed) : randomSeed",
        Inputs = new Dictionary<string, string>
        {
            ["larrytoken"] = "resources.larrytoken",
            ["larrySpinCounter"] = "PlayerData.larrySpinCounter (incremented per spin)",
            ["eligibleSkins"] = "upgrades[] with Skin set and ApplicableTo the gear (Obtainable()/ExcludeFromWorldPool are not dumped)",
        },
        Constants = new Dictionary<string, float> { ["TokenCost"] = 1f, ["RareSpinEvery"] = 6f, ["RareSpinCandidates"] = 31f },
    };

    // rarityDurations is (weight, minSeconds, maxSeconds) indexed by Rarity; read from the publicized statics.
    private static Dictionary<string, Formulas.DroneRarity[]> BuildDroneRarityCycles()
    {
        static Formulas.DroneRarity[] Table(UnityEngine.Vector3[] d) => d?.Select((v, i) => new Formulas.DroneRarity
        {
            Rarity = RarityKey((Rarity)i), Weight = v.x, MinSeconds = v.y, MaxSeconds = v.z,
        }).ToArray();
        return new Dictionary<string, Formulas.DroneRarity[]>
        {
            ["UpgradeDrone"] = Table(UpgradeDrone.rarityDurations),
            ["DroneHalo"] = Table(DroneHalo.rarityDurations),
        };
    }
}
