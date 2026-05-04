using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using Newtonsoft.Json;

namespace MycopunkDumper;

partial class Plugin
{
    private static readonly Dictionary<string, (float DamageMult, float Lifetime)> StatusEffectTuning = new()
    {
        ["el_fire"]   = (1.0f, 3.0f),
        ["el_shock"]  = (1.0f, 3.0f),
        ["el_acid"]   = (1.0f, 3.0f),
        ["el_decay"]  = (1.2f, 4.0f),
        ["el_rot"]    = (1.35f, 60.0f),
        ["el_bee"]    = (1.0f, 5.0f),
        ["el_water"]  = (1.0f, 4.5f),
        ["el_immunity"] = (1.0f, 1.6f),    // 1.6 for player, 0.6 for enemies
        ["el_yellow"] = (1.0f, 30.0f),     // 30s for EnemyCore, 5s otherwise
    };

    private static StatusEffectEntry BuildStatusEffect(StatusEffectData s)
    {
        var entry = new StatusEffectEntry
        {
            ID = GetPrivateField<string>(s, "effectID"),
            Name = s.EffectName,
            ColoredName = s.ColoredEffectName,
            VerbName = s.VerbName,
            PastVerbName = s.PastVerbName,
            IconTextTag = GetPrivateField<string>(s, "iconTextTag"),
            StopsHealthRegeneration = s.StopsHealthRegeneration,
            MinStageValue = GetPrivateField<float>(s, "minStageValue"),
            ElementSwitchID = s.ElementSwitchID,
            NumStages = (s.stages != null ? s.stages.Length : 0),
            TrailColor = GetPrivateField<UnityEngine.Color>(s, "trailColor").ToString(),
            LaserColor = GetPrivateField<UnityEngine.Color>(s, "laserColor").ToString(),
            TextColor = GetPrivateField<UnityEngine.Color>(s, "textColor").ToString(),
            IconColor = GetPrivateField<UnityEngine.Color>(s, "iconColor").ToString(),
            MuzzleFlashColor = GetPrivateField<UnityEngine.Color>(s, "muzzleFlashColor").ToString(),
            Icon = BuildIcon(GetPrivateField<UnityEngine.Sprite>(s, "icon"))
        };
        if (entry.ID != null && StatusEffectTuning.TryGetValue(entry.ID, out var tuning))
        {
            entry.Tuning = new StatusEffectEntry.StatusEffectTuning
            {
                DamageMultiplier = tuning.DamageMult,
                FullSaturationLifetime = tuning.Lifetime,
                DecayDelay = entry.ID == "el_immunity" ? 0.5f : 3.0f,
                DecaySpeed = entry.ID == "el_immunity" ? 20.0f : 0.3f
            };
        }
        return entry;
    }

    private static Stack BuildStack(PlayerStackData p) => new()
    {
        Name = p.Name,
        Color = p.Color.ToString(),
        MaxStacks = p.MaxStacks,
        LoseStackInterval = p.LoseStackInterval,
        TimeOutDuration = p.TimeOutDuration,
        TimeoutOnLevelLoad = p.TimeoutOnLevelLoad,
        DisplayOnHUD = GetPrivateField<bool>(p, "DisplayOnHUD"),
        Icon = BuildIcon(p.Icon)
    };

    private static Threat BuildThreat(ThreatData t)
    {
        var th = new Threat
        {
            ID = GetPrivateField<string>(t, "_threatName") ?? t.name,
            Name = t.ThreatName,
            NumberLabel = t.ThreatNumberLabel,
            Color = t.Color.ToString(),
            Icon = BuildIcon(t.Icon)
        };
        var rewards = GetPrivateField<LevelUnlockList>(t, "MissionRewards").Properties;
        if (rewards != null) th.MissionRewards = rewards.Where(x => x != null).Select(BuildLevelUnlock).ToArray();
        return th;
    }


    private static LootPoolEntry BuildLootPool(LootPool lp)
    {
        var entry = new LootPoolEntry { Name = lp.name };
        var items = lp.upgrades.items;
        if (items == null) return entry;
        var refs = new List<LootPoolEntry.WeightedRef>();
        foreach (var it in items)
        {
            if (it.value == null) continue;
            refs.Add(new LootPoolEntry.WeightedRef
            {
                Upgrade = UpgradeKey(it.value.ID),
                Weight = it.weight
            });
        }
        entry.Upgrades = refs.ToArray();
        return entry;
    }
}
