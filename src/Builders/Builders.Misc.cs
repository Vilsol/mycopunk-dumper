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
    /// <summary>
    /// Walk the player-level milestone chain. The chain is built in code inside
    /// <c>LevelMilestones.Initialize()</c> (via <c>FirstMilestone()</c>), so we need any
    /// instance of the prefab to trigger and read it. Returns null if no instance is found.
    /// </summary>
    private static LevelMilestoneEntry[] BuildLevelMilestones()
    {
        var lm = UnityEngine.Resources.FindObjectsOfTypeAll<LevelMilestones>().FirstOrDefault();
        if (lm == null) return null;
        try
        {
            var list = new List<LevelMilestoneEntry>();
            for (var ms = lm.FirstMilestone(); ms != null; ms = ms.Next)
            {
                var items = (ms.Items ?? Array.Empty<Pigeon.HoverInfo.Item>())
                    .Select(it => new LevelMilestoneEntry.LevelMilestoneItem
                    {
                        Label = it.Name,
                        Icon = it.Icon != null ? it.Icon.texture?.name : null
                    }).ToArray();
                list.Add(new LevelMilestoneEntry { Level = ms.Level, Items = items });
            }
            return list.ToArray();
        }
        catch (Exception ex)
        {
            Log.LogWarning($"LevelMilestones walk failed: {ex.Message}");
            return null;
        }
    }

    private static DirectiveManagerEntry BuildDirectiveManager(DirectiveManager dm)
    {
        float[] Tiers(object mod) => new[]
        {
            GetPrivateField<float>(mod, "tier1"),
            GetPrivateField<float>(mod, "tier2"),
            GetPrivateField<float>(mod, "tier3"),
            GetPrivateField<float>(mod, "tier4")
        };
        Gear.LevelUnlockEntry[] Rewards(LevelUnlockList l) =>
            l.Properties?.Where(x => x != null).Select(BuildLevelUnlock).ToArray();

        return new DirectiveManagerEntry
        {
            TierCount = DirectiveManager.TierCount,
            PageRewardInterval = DirectiveManager.PageRewardInterval,
            KillsMultiplier = Tiers(dm.KillsMultiplier),
            HeavyKillsMultiplier = Tiers(dm.HeavyKillsMultiplier),
            MissionsMultiplier = Tiers(dm.MissionsMultiplier),
            TierRewards = new[]
            {
                Rewards(dm.GetTierRewards(0)),
                Rewards(dm.GetTierRewards(1)),
                Rewards(dm.GetTierRewards(2)),
                Rewards(dm.GetTierRewards(3))
            },
            Page10Rewards = Rewards(dm.Page10Rewards)
        };
    }

    private static CollectableEntry BuildCollectable(CollectableProfile cp)
    {
        var entry = new CollectableEntry
        {
            ID = cp.APIName,
            Name = cp.Name,
            Color = cp.Color.ToString(),
            Count = cp.Count,
            PunchTextID = cp.PunchTextID,
            PunchText = LocText(cp.PunchTextID, 0),
            Icon = BuildIcon(cp.Icon)
        };
        var rewards = GetPrivateField<LevelUnlockList>(cp, "rewards").Properties;
        if (rewards != null) entry.Rewards = rewards.Where(x => x != null).Select(BuildLevelUnlock).ToArray();
        return entry;
    }



    private static void BuildDialogue()
    {
        DialogueEntry = new DialogueData
        {
            Exchanges = new SortedDictionary<string, DialogueEntry>(),
            TriggerChances = GetPrivateField<float[]>(Global.Instance, "DialogueEventChances")
        };
        var exchanges = GetPrivateField<System.Array>(Global.Instance, "DialogueExchanges");
        if (exchanges == null) return;
        foreach (var dx in exchanges)
        {
            if (dx == null) continue;
            var id = GetPrivateField<string>(dx, "id");
            if (string.IsNullOrEmpty(id) || DialogueEntry.Exchanges.ContainsKey(id)) continue;
            var main = GetPrivateField<UnityEngine.Object>(dx, "mainCharacter");
            var sec = GetPrivateField<UnityEngine.Object>(dx, "secondaryCharacter");
            var entry = new MycopunkDumper.DialogueEntry
            {
                ID = id,
                Trigger = GetPrivateField<object>(dx, "trigger")?.ToString(),
                MissionTypes = GetPrivateField<object>(dx, "missionTypes")?.ToString(),
                LevelTypes = GetPrivateField<object>(dx, "levelTypes")?.ToString(),
                ValidRegions = GetPrivateField<object>(dx, "validRegions")?.ToString(),
                MainCharacter = main?.name,
                SecondaryCharacter = sec?.name
            };
            // lines : Line[] (struct of character/delay/startWithNext). Each line
            // is the i-th block under the exchange's TextBlocks key.
            var lines = GetPrivateField<System.Array>(dx, "lines");
            if (lines != null)
            {
                var list = new List<MycopunkDumper.DialogueEntry.LineEntry>();
                int lineIdx = 0;
                foreach (var ln in lines)
                {
                    if (ln == null) { lineIdx++; continue; }
                    var ch = GetPrivateField<UnityEngine.Object>(ln, "character");
                    list.Add(new MycopunkDumper.DialogueEntry.LineEntry
                    {
                        Character = ch?.name,
                        Delay = GetPrivateField<float>(ln, "delay"),
                        StartWithNext = GetPrivateField<bool>(ln, "startWithNext"),
                        Text = LocText(id, lineIdx)
                    });
                    lineIdx++;
                }
                entry.Lines = list.ToArray();
            }
            DialogueEntry.Exchanges[id] = entry;
        }
    }

    // ---- Master quip catalog -----------------------------------------------

    private static QuipEntry BuildQuipEntry(QuipData q, int index, string label)
    {
        var icon = GetPrivateField<UnityEngine.Sprite>(q, "<Icon>k__BackingField") ?? q.Icon;
        var firstClip = GetPrivateField<object>(q, "<AnimationFirstPerson>k__BackingField");
        var thirdClip = GetPrivateField<object>(q, "<AnimationThirdPerson>k__BackingField");
        var voicelineID = GetPrivateField<string>(q, "voicelineTextID");
        return new QuipEntry
        {
            ID = GetPrivateField<int>(q, "id"),
            Index = index,
            Label = label,
            Character = GetPrivateField<object>(q, "character")?.ToString(),
            QuipType = GetPrivateField<object>(q, "QuipType")?.ToString(),
            VoicelineTextID = voicelineID,
            VoicelineText = LocText(voicelineID, 0),
            TriggerOnFire = GetPrivateField<bool>(q, "triggerOnFire"),
            CancelOnFire = GetPrivateField<bool>(q, "cancelOnFire"),
            PlayThirdPersonAnimationForOwner = GetPrivateField<bool>(q, "playThirdPersonAnimationForOwner"),
            HideThirdPersonWeapon = GetPrivateField<bool>(q, "HideThirdPersonWeapon"),
            IsThirdPersonAdditive = GetPrivateField<bool>(q, "<IsThirdPersonAdditive>k__BackingField"),
            MinAnimationInterval = GetPrivateField<float>(q, "minAnimationInterval"),
            AnimationFirstPersonClip = NestedClipName(firstClip),
            AnimationThirdPersonClip = NestedClipName(thirdClip),
            Icon = BuildIcon(icon)
        };
    }

    // Mirrors Fulfillment.GetCategoryName. Going through LocText rather than the game's
    // helper keeps unresolved keys as null instead of echoing the key back as display text
    // ("career" has no CSV entry in this build).
    private static string FulfillmentCategoryKey(Fulfillment.Category category) => category switch
    {
        Fulfillment.Category.Career => "career",
        Fulfillment.Category.Mission => "mission",
        Fulfillment.Category.HUB => "hub",
        Fulfillment.Category.Gear => "gear",
        _ => null,
    };

    private static FulfillmentEntry BuildFulfillment(Fulfillment f)
    {
        var overrideDesc = GetPrivateField<string>(f, "overrideDescription");
        var category = f.FulfillmentCategory;
        var entry = new FulfillmentEntry
        {
            ID = f.ID,
            Subclass = f.GetType().Name,
            DescriptionID = string.IsNullOrEmpty(overrideDesc) ? f.ID : overrideDesc,
            Category = category.ToString(),
            CategoryName = LocText(FulfillmentCategoryKey(category), 0),
            PlatformAchievementID = GetPrivateField<string>(f, "platformAchievementID"),
            IsHidden = f.IsHidden,
            Score = f.Score,
            CompletionThreshold = GetPrivateField<int>(f, "completionThreshold"),
            UseThresholdAsDescParam = GetPrivateField<bool>(f, "useThresholdAsDescParam"),
        };

        // Several subclasses resolve their description through PlayerData, which isn't
        // populated in a headless dump. TextBlocks also echoes the key back for missing
        // entries, and this build ships no fulfillment strings at all — treat both as "no text".
        try
        {
            var desc = f.GetDescription();
            if (!string.IsNullOrEmpty(desc) && desc != entry.DescriptionID) entry.Description = desc;
        }
        catch { entry.Description = LocText(entry.DescriptionID, 0); }

        try
        {
            var reward = GetPrivateField<LevelUnlockContainer>(f, "reward").LevelUnlock;
            if (reward != null) entry.Reward = BuildLevelUnlock(reward);
        }
        catch (Exception ex) { Log.LogWarning($"Fulfillment reward failed for {f.ID}: {ex.Message}"); }
        entry.RewardGear = GetPrivateField<GearInfo>(f, "rewardGear")?.APIName;

        switch (f)
        {
            case ElementalFulfillment:
                entry.Element = GetPrivateField<EffectType>(f, "element").ToString();
                break;

            case FlagFulfillment:
                entry.FlagName = GetPrivateField<string>(f, "flagName");
                break;

            case LevelFulfillment:
                entry.TargetGear = GetPrivateField<GearInfo>(f, "gear")?.APIName;
                break;

            case MissionFulfillment:
                entry.Mission = GetPrivateField<Mission>(f, "mission")?.ID;
                entry.MissionContainer = GetPrivateField<MissionContainer>(f, "container")?.ID;
                entry.CompletionCount = GetPrivateField<int>(f, "completionCount");
                break;

            case OuroborosFulfillment:
                entry.MinThreat = GetPrivateField<int>(f, "minThreat");
                break;

            case UpgradeFulfillment:
                entry.DiscoveredOnly = GetPrivateField<bool>(f, "discoveredOnly");
                entry.DiscoverAll = GetPrivateField<bool>(f, "discoverAll");
                entry.Rarities = GetPrivateField<RarityFlags>(f, "rarities").ToString();
                entry.CountUpgrades = GetPrivateField<bool>(f, "countUpgrades");
                entry.CountSkins = GetPrivateField<bool>(f, "countSkins");
                break;

            case EncounterFulfillment:
                entry.Encounter = GetPrivateField<UnityEngine.Object>(f, "encounter")?.name;
                break;
        }

        return entry;
    }
}
