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
    private static void FillGearFields(Gear g, IUpgradable u)
    {
        var info = u.Info;
        g.ID = info.ID.ToString();
        g.Name = info.Name;
        g.APIName = info.APIName;
        g.TypeName = info.TypeName;
        g.Description = info.Description;
        g.GearType = u.GearType.ToString();
        g.MaxLevel = info.MaxLevel;
        g.MinUnlockLevel = info.MinUnlockLevel;
        g.UnlockAutomatically = info.UnlockAutomatically;
        g.HideWhenNotCollected = info.HideWhenNotCollected;
        g.CanGainXP = info.CanGainXP;
        g.HasUpgradeGrid = info.HasUpgradeGrid;
        g.XPGainMultiplier = info.XPGainMultilier; // game's spelling
        g.DirectiveKillsMultiplier = info.DirectiveKillsMultiplier;
        g.SkinCount = info.SkinCount();
        g.Icon = BuildIcon(info.Icon);

        if (info.UnlockCost != null)
        {
            g.UnlockCost = info.UnlockCost.Select(c => new Upgrade.DUnlockCost
            {
                Count = c.count,
                Resource = c.resource?.Name,
                ResourceID = c.resource?.ID
            }).ToArray();
        }

        try
        {
            var sizes = new List<Gear.GridSize>();
            for (var level = 0; level <= System.Math.Max(1, info.MaxLevel); level++)
            {
                info.GetUpgradeGridSize(level, out var w, out var h, false);
                sizes.Add(new Gear.GridSize { Width = w, Height = h });
            }
            g.GridSizes = sizes.ToArray();
        }
        catch (Exception e) { Log.LogWarning($"grid sizes failed for {info.APIName}: {e.Message}"); }

        if (info.LevelUnlocks.Properties != null)
        {
            g.LevelUnlocks = info.LevelUnlocks.Properties
                .Where(lu => lu != null)
                .Select(BuildLevelUnlock)
                .ToArray();
        }
    }

    private static Gear BuildGear(IUpgradable u)
    {
        var g = new Gear();
        FillGearFields(g, u);
        g.RawData = u;
        return g;
    }

    private static CharacterEntry BuildCharacter(Character c)
    {
        var ch = new CharacterEntry();
        FillGearFields(ch, c);
        ch.Index = c.Index;
        ch.IsPlayable = c.IsPlayable;
        ch.EmployeeID = c.EmployeeID;
        ch.UIColor = c.UIColor.ToString();
        ch.TextColor = c.TextColor.ToString();
        ch.ParticleColor = c.ParticleColor.ToString();
        ch.PrimaryColorTag = c.PrimaryColorTag;
        ch.TextColorTag = c.TextColorTag;
        ch.DefaultUpgradeType = c.DefaultUpgrade?.GetType().Name;
        ch.DefaultSkinType = c.DefaultSkin?.GetType().Name;
        if (c.Quips != null) ch.Quips = c.Quips.Select(BuildQuip).ToArray();
        if (c.DefaultEmotes != null) ch.DefaultEmotes = c.DefaultEmotes.Select(BuildQuip).ToArray();
        ch.RawData = c;

        // Skill tree: each character has a SkillTree MonoBehaviour with SkillTreeUpgradeUI
        // children — each child is one node (upgrade + tier + grid coords + prereq).
        try
        {
            var tree = c.SkillTree;
            if (tree != null)
            {
                var nodes = tree.GetComponentsInChildren<SkillTreeUpgradeUI>(true);
                if (nodes != null && nodes.Length > 0)
                {
                    var list = new List<CharacterEntry.SkillTreeNode>(nodes.Length);
                    foreach (var n in nodes)
                    {
                        if (n == null) continue;
                        var nodeUpgrade = GetPrivateField<global::Upgrade>(n, "upgrade");
                        if (nodeUpgrade == null) continue;
                        var prereq = GetPrivateField<global::Upgrade>(n, "mustBeUnlockedFirst");
                        list.Add(new CharacterEntry.SkillTreeNode
                        {
                            Upgrade = UpgradeKey(nodeUpgrade.ID),
                            Layer = GetPrivateField<int>(n, "layer"),
                            CoordX = GetPrivateField<int>(n, "coordX"),
                            CoordY = GetPrivateField<int>(n, "coordY"),
                            MustBeUnlockedFirst = prereq != null ? UpgradeKey(prereq.ID) : null,
                            MinSpentSkillPointsToUnlock = n.MinSpentSkillPointsToUnlock
                        });
                    }
                    ch.SkillTree = list.ToArray();
                }
            }
        }
        catch (Exception ex) { Log.LogWarning($"SkillTree extract failed for {c.Info?.APIName}: {ex.Message}"); }

        return ch;
    }

    private static CharacterEntry.Quip BuildQuip(QuipData q) => new()
    {
        ID = q.ID,
        Index = q.Index,
        APIName = q.APIName,
        Label = q.Label,
        VoicelineTextID = q.VoicelineTextID,
        QuipType = q.QuipType.ToString(),
        TriggerOnFire = q.TriggerOnFire,
        HasVoiceline = q.HasVoiceline,
        Icon = BuildIcon(q.Icon)
    };


    private static Gear.LevelUnlockEntry BuildLevelUnlock(LevelUnlock lu)
    {
        var entry = new Gear.LevelUnlockEntry
        {
            Type = lu.GetType().Name,
            Level = lu.Level,
            Count = GetPrivateField<int>(lu, "count"),
            Chance = GetPrivateField<float>(lu, "chance")
        };

        switch (lu)
        {
            case LevelUnlock_Resource lur when lur.Resource.resource != null:
                entry.Resource = new Upgrade.DUnlockCost
                {
                    Count = lur.Resource.count,
                    Resource = lur.Resource.resource.Name,
                    ResourceID = lur.Resource.resource.ID
                };
                break;
            case LevelUnlock_MultipleUpgrades lum:
                entry.PrioritizeUndiscovered = GetPrivateField<bool>(lum, "prioritizeUndiscovered");
                var ups = GetPrivateField<global::Upgrade[]>(lum, "upgrades");
                if (ups != null) entry.Upgrades = ups.Where(u => u != null).Select(u => UpgradeKey(u.ID)).ToArray();
                break;
            case LevelUnlock_UpgradeRarity:
            case LevelUnlock_SkinRarity:
                var rarity = GetPrivateField<object>(lu, "rarity");
                entry.Rarity = rarity?.ToString();
                break;
        }

        // Subclasses observed in mission/container rewards but not gear/directive rewards.
        // These types may not be referenced from the publicized DLL by the dumper's
        // build target, so handle them by class name + reflection rather than `is`.
        switch (lu.GetType().Name)
        {
            case "LevelUnlock_XP":
                entry.XP = GetPrivateField<int>(lu, "xp");
                break;
            case "LevelUnlock_Upgrade":
                var u = GetPrivateField<global::Upgrade>(lu, "upgrade");
                if (u != null) entry.Upgrade = UpgradeKey(u.ID);
                break;
            case "LevelUnlock_SeededSkin":
                var su = GetPrivateField<global::Upgrade>(lu, "upgrade");
                if (su != null) entry.Upgrade = UpgradeKey(su.ID);
                entry.Seed = GetPrivateField<int>(lu, "seed");
                break;
            case "LevelUnlock_LootPool":
                var pool = GetPrivateField<UnityEngine.Object>(lu, "pool");
                if (pool != null) entry.LootPool = pool.name;
                entry.MinLevel = GetPrivateField<int>(lu, "minLevel");
                break;
            case "LevelUnlock_Gear":
                var gear = GetPrivateField<UnityEngine.Object>(lu, "gear");
                if (gear != null) entry.Gear = gear.name;
                entry.Unlock = GetPrivateField<bool>(lu, "unlock");
                break;
            case "LevelUnlock_IntroUpgrade":
                var u1 = GetPrivateField<global::Upgrade>(lu, "upgrade1");
                var u2 = GetPrivateField<global::Upgrade>(lu, "upgrade2");
                if (u1 != null) entry.Upgrade1 = UpgradeKey(u1.ID);
                if (u2 != null) entry.Upgrade2 = UpgradeKey(u2.ID);
                break;
            case "LevelUnlock_Preview":
                entry.Preview = GetPrivateField<string>(lu, "nameID");
                break;
            case "LevelUnlock_RarityReward":
                var rr = GetPrivateField<object>(lu, "rarity");
                entry.Rarity = rr?.ToString();
                break;
            // LevelUnlock_Skin has no extra fields beyond Count
        }

        return entry;
    }
}
