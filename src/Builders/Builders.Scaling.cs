using System;
using System.Collections.Generic;
using System.Linq;
using Pigeon;

namespace MycopunkDumper;

partial class Plugin
{
    // Generic flatteners for the game's serialized modifier/weight wrappers (all fields publicized).

    private static T[] ThreatValues<T>(ThreatModifier<T> m) => m.threats;

    private static T[] IntensityValues<T>(IntensityModifier<T> m) => m.values;

    private static PlayerScaled<T> PlayerValues<T>(PlayerModifier<T> m) => new() { Values = m.values, PerExtraPlayer = m.multiplier };

    private static PlayerScaled<T>[] ThreatPlayerValues<T>(ThreatModifier<PlayerModifier<T>> m) =>
        m.threats?.Select(PlayerValues).ToArray();

    private static WeightedEntry[] WeightedValues<T>(WeightedArray<T> wa, Func<T, string> key) =>
        wa?.items?.Where(n => n != null).Select(n => new WeightedEntry { Key = key(n.value), Weight = n.weight }).ToArray();

    private static DamageInfo DamageValues(DamageData d) =>
        new() { Damage = d.damage, Effect = d.effect.ToString(), EffectAmount = d.effectAmount, DamageFlags = d.damageFlags.ToString() };

    private static MinMax<T> RangeValues<T>(Range<T> r) where T : struct, IEquatable<T> => new() { Min = r.min, Max = r.max };

    private static CurveData BuildCurve(UnityEngine.AnimationCurve c)
    {
        if (c == null) return null;
        return new CurveData
        {
            PreWrapMode = c.preWrapMode.ToString(),
            PostWrapMode = c.postWrapMode.ToString(),
            Keys = c.keys.Select(k => new CurveData.Key
            {
                Time = k.time,
                Value = k.value,
                InTangent = k.inTangent,
                OutTangent = k.outTangent,
                InWeight = k.inWeight,
                OutWeight = k.outWeight,
                WeightedMode = k.weightedMode.ToString()
            }).ToArray()
        };
    }

    private static EnemyPartList BuildPartList(EnemyClass.PartList p) => new()
    {
        Chance = p.chance,
        Min = p.min,
        Max = p.max,
        OverrideAttachMode = p.overrideAttachMode.ToString(),
        Parts = WeightedValues(p.parts, g => g?.name)
    };

    // Same keying as the `enemies` map.
    private static string EnemyKey(EnemyClass e) => e == null ? null : e.ID != 0 ? e.ID.ToString() : e.APIName;

    private static string RarityKey(Rarity r) => r.ToString().ToLowerInvariant();

    private static Difficulty BuildDifficulty()
    {
        var g = Global.Instance;
        var health = new Dictionary<string, PlayerScaled<float>[]>();
        for (int i = 0; i < g.EnemyHealthScaling.Length; i++)
            health[((EnemyType)i).ToString()] = ThreatPlayerValues(g.EnemyHealthScaling[i]);
        return new Difficulty
        {
            EnemyHealthScaling = health,
            EnemyDamageScaling = ThreatPlayerValues(g.EnemyDamageScaling),
            EnemyWaveSize = ThreatPlayerValues(g.EnemyWaveSize),
            EnemyWaveInterval = ThreatPlayerValues(g.EnemyWaveInterval),
            EnemyMoveSpeedScaling = ThreatValues(g.EnemyMoveSpeedScaling),
            EnemyAttackSpeedScaling = ThreatValues(g.EnemyAttackSpeedScaling),
            LookForPartIntervalScaling = ThreatValues(g.LookForPartIntervalScaling),
            IntensityWaveInterval = IntensityValues(g.IntensityWaveInterval),
            IntensityMinOpenWaveSlots = IntensityValues(g.IntensityMinOpenWaveSlots),
            DefaultTeamReviveCount = PlayerValues(g.DefaultTeamReviveCount)
        };
    }

    private static Economy BuildEconomy()
    {
        var g = Global.Instance;
        var drops = new Dictionary<string, float>();
        for (int i = 0; i < g.enemyUpgradeDropChances.Length; i++)
            drops[((EnemyType)i).ToString()] = g.enemyUpgradeDropChances[i];
        return new Economy
        {
            BaseMissionXP = g.BaseMissionXP,
            BaseScripForMissionCompletion = g.BaseScripForMissionCompletion,
            BaseScripForSideObjective = g.BaseScripForSideObjective,
            BaseXPForSideObjective = g.BaseXPForSideObjective,
            BaseResourcesForSideObjective = g.BaseResourcesForSideObjective,
            ResourceAmountMultiplier = ThreatValues(g.ResourceAmountMultiplier),
            SurvivalBonusScrip = ThreatValues(g.SurvivalBonusScrip),
            IntensityScripAndXPMultiplier = IntensityValues(g.IntensityScripAndXPMultiplier),
            EnemyUpgradeDropChances = drops,
            CombatResourceSpawnChance = g.CombatResourceSpawnChance,
            GlobalEnemyAttachments = g.GlobalEnemyAttachments?.Select(BuildPartList).ToArray(),
            RarityWeights = WeightedValues(g.rarityWeights, RarityKey),
            SkinRarityWeights = WeightedValues(g.skinRarityWeights, RarityKey),
            RandomRewards = g.randomRewards?.items?.Where(n => n != null)
                .Select(n => new Economy.RandomReward { Resource = n.value.resource?.ID, Count = n.value.count, Weight = n.weight }).ToArray()
        };
    }

    private static MissionGeneration BuildMissionGeneration()
    {
        var g = Global.Instance;
        return new MissionGeneration
        {
            MissionModifierChance = g.MissionModifierChance,
            MissionModifierCountCurve = BuildCurve(g.MissionModifierCountCurve),
            MissionModifiers = WeightedValues(g.MissionModifiers, m => m?.ID.ToString()),
            SideObjectiveSpawnChance = g.SideObjectiveSpawnChance,
            SideObjectives = WeightedValues(g.SideObjectives, o => o == null ? null : o.gameObject?.name ?? o.GetType().Name)
        };
    }

    private static CombatGlobals BuildCombatGlobals() => new()
    {
        LightningForkDamage = DamageValues(Global.Instance.LightningForkDamage),
        CoreExplosionDamageFalloff = BuildCurve(Global.Instance.CoreExplosionDamageFalloff)
    };

    // Global.GenericPlayerStatUpgrades, evaluated per character like upgrade Properties.
    private static Upgrade.DProperty[] BuildGenericStatUpgrades()
    {
        var props = Global.Instance.GenericPlayerStatUpgrades.properties;
        if (props == null) return null;
        var result = props.Select(p => new Upgrade.DProperty { Type = p.GetType().Name, Label = PrettifyPropertyType(p.GetType().Name), Raw = p }).ToArray();
        foreach (var c in Global.Instance.Characters ?? [])
        {
            if (c == null) continue;
            var apiName = c.Info?.APIName ?? c.GetType().Name;
            for (int i = 0; i < props.Length; i++)
                CollectPropertyStats(result[i], props[i], c, apiName, _ => null, "GenericPlayerStatUpgrades");
        }
        return result;
    }

    private static SortedDictionary<string, MissionEventEntry> BuildMissionEvents()
    {
        var map = new SortedDictionary<string, MissionEventEntry>();
        var items = Global.Instance.MissionEvents?.items;
        if (items == null) return map;
        for (int i = 0; i < items.Length; i++)
        {
            var obj = items[i]?.value;
            if (obj == null) continue;
            var ev = obj is UnityEngine.GameObject go ? go.GetComponent<IMissionEvent>() as UnityEngine.Object : obj;
            var entry = new MissionEventEntry
            {
                Name = obj.name,
                Subclass = ev?.GetType().Name,
                Index = i,
                Weight = items[i].weight,
                IsPrefab = obj is UnityEngine.GameObject,
                RawData = ev
            };
            if (ev is WeatherMissionEvent w)
            {
                entry.Weather = WeightedValues(w.events, t => t.ToString());
                entry.RainDuration = RangeValues(w.rainDuration);
                entry.FogDuration = RangeValues(w.fogDuration);
                entry.SulfurRainDuration = RangeValues(w.sulfurRainDuration);
                entry.DebrisStormDuration = RangeValues(w.debrisStormDuration);
            }
            else if (ev is AmalgamationMissionEvent a)
                entry.AmalgamationClass = EnemyKey(a.amalgamationClass);
            var key = map.ContainsKey(obj.name) ? $"{obj.name}#{i}" : obj.name;
            map[key] = entry;
        }
        return map;
    }
}
