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
    private static Resource BuildResource(PlayerResource r) => new()
    {
        ID = r.ID,
        Name = r.Name,
        Description = r.Description,
        Color = r.Color.ToString(),
        IconColor = r.IconColor.ToString(),
        Max = r.Max,
        ItemCount = r.ItemCount,
        UnlockUseCount = r.UnlockUseCount,
        UnlockUsePerSecond = r.UnlockUsePerSecond,
        Rarity = r.Rarity.ToString(),
        Visibility = r.Visibility.ToString(),
        IsAmountAffectedByDifficulty = r.IsAmountAffectedByDifficulty,
        IsItem = r.IsItem,
        Icon = BuildIcon(r.Icon)
    };


    private static void BuildRarities()
    {
        var arr = GetPrivateField<System.Array>(Global.Instance, "Rarities");
        if (arr == null) return;
        for (int i = 0; i < arr.Length; i++)
        {
            object r = arr.GetValue(i);
            if (r == null) continue;
            var name = GetPrivateField<string>(r, "name");
            if (string.IsNullOrEmpty(name) || RarityMap.ContainsKey(name)) continue;

            var scrap = GetPrivateField<PlayerResource>(r, "scrapResource");
            var icon = GetPrivateField<UnityEngine.Sprite>(r, "icon");
            var entry = new RarityEntry
            {
                Name = name,
                LocalizedName = GetPrivateField<string>(r, "_localizedName"),
                ColorTag = GetPrivateField<string>(r, "_colorTag"),
                BoostedName = GetPrivateField<string>(r, "_boostedName"),
                TurbochargedName = GetPrivateField<string>(r, "_turbochargedName"),
                UpgradeScripCost = GetPrivateField<int>(r, "upgradeScripCost"),
                UpgradeRareResourceCost = GetPrivateField<int>(r, "upgradeRareResourceCost"),
                CleanseCost = GetPrivateField<int>(r, "cleanseCost"),
                CraftNewSaxoniteCost = GetPrivateField<int>(r, "craftNewSaxoniteCost"),
                CraftNewNormalResourceCost = GetPrivateField<int>(r, "craftNewNormalResourceCost"),
                ScrapResource = scrap?.ID,
                Color = GetPrivateField<UnityEngine.Color>(r, "color").ToString(),
                BackgroundColor = GetPrivateField<UnityEngine.Color>(r, "backgroundColor").ToString(),
                LightColor = GetPrivateField<UnityEngine.Color>(r, "lightColor").ToString(),
                Icon = BuildIcon(icon)
            };
            // additionalUpgradeCost: ResourceCost[]
            var extra = GetPrivateField<System.Array>(r, "additionalUpgradeCost");
            if (extra != null)
            {
                var list = new List<Upgrade.DUnlockCost>();
                foreach (var rc in extra)
                {
                    if (rc == null) continue;
                    var res = GetPrivateField<PlayerResource>(rc, "resource");
                    list.Add(new Upgrade.DUnlockCost
                    {
                        Count = GetPrivateField<int>(rc, "count"),
                        Resource = res?.Name,
                        ResourceID = res?.ID
                    });
                }
                entry.AdditionalUpgradeCost = list.ToArray();
            }
            RarityMap[name] = entry;
        }
    }

    // ---- Crafting table -----------------------------------------------------

    /// <summary>
    /// Build the master crafting price table from the <c>CraftingWindow</c>
    /// MonoBehaviour prefab's private <c>ResourceCost[]</c> fields.
    /// </summary>
    private static void BuildCrafting()
    {
        var t = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
            .FirstOrDefault(x => x.Name == "CraftingWindow");
        if (t == null) return;
        var prefabs = UnityEngine.Resources.FindObjectsOfTypeAll(t);
        if (prefabs.Length == 0) return;
        var cw = prefabs[0];
        Upgrade.DUnlockCost[] read(string field)
        {
            var arr = t.GetField(field, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)?.GetValue(cw) as System.Array;
            if (arr == null) return null;
            var list = new List<Upgrade.DUnlockCost>();
            foreach (var rc in arr)
            {
                if (rc == null) continue;
                var res = GetPrivateField<PlayerResource>(rc, "resource");
                list.Add(new Upgrade.DUnlockCost
                {
                    Count = GetPrivateField<int>(rc, "count"),
                    Resource = res?.Name,
                    ResourceID = res?.ID
                });
            }
            return list.ToArray();
        }
        CraftingEntry = new Crafting
        {
            MinLevelToAccessCrafting = CraftingWindow.MinLevelToAccessCrafting,
            RandomCraftCost = read("randomCraftCost"),
            WeaponCraftCost = read("weaponCraftCost"),
            UpgradeCraftCost = read("upgradeCraftCost"),
            RandomNewCraftCost = read("randomNewCraftCost"),
            WeaponNewCraftCost = read("weaponNewCraftCost"),
            UpcraftToRareCost = read("upcraftToRareCost"),
            UpcraftToEpicCost = read("upcraftToEpicCost"),
            UpcraftToExoticCost = read("upcraftToExoticCost")
        };

        // Mirror CraftingWindow.SetRarity: [0] becomes the rarity's scrap, middle entries scale
        // off craftNewNormalResourceCost, the last (saxonite) entry scales off craftNewSaxoniteCost.
        var buttons = t.GetField("rarityButtons", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(cw) as System.Array;
        var rarities = Global.Instance.Rarities;
        int n = Math.Min(rarities.Length, buttons?.Length ?? 0);
        CraftingEntry.ByRarity = new SortedDictionary<string, Crafting.RarityCosts>();
        for (int i = 0; i < n; i++)
        {
            var rd = rarities[i];
            var scrap = rd.scrapResource;
            int normal = rd.craftNewNormalResourceCost;
            Upgrade.DUnlockCost[] patch(Upgrade.DUnlockCost[] tpl, int? mid, int? last)
            {
                if (tpl == null) return null;
                var arr = tpl.Select(c => new Upgrade.DUnlockCost { Count = c.Count, Resource = c.Resource, ResourceID = c.ResourceID }).ToArray();
                if (arr.Length == 0) return arr;
                arr[0].Resource = scrap?.Name;
                arr[0].ResourceID = scrap?.ID;
                if (mid.HasValue) for (int k = 1; k < arr.Length - 1; k++) arr[k].Count = mid.Value;
                if (last.HasValue && arr.Length > 1) arr[^1].Count = last.Value;
                return arr;
            }
            CraftingEntry.ByRarity[GetPrivateField<string>(rd, "name")] = new Crafting.RarityCosts
            {
                RandomCraftCost = patch(CraftingEntry.RandomCraftCost, UnityEngine.Mathf.CeilToInt(normal * 0.65f), null),
                RandomNewCraftCost = patch(CraftingEntry.RandomNewCraftCost, normal, rd.craftNewSaxoniteCost),
                WeaponCraftCost = patch(CraftingEntry.WeaponCraftCost, null, null),
                WeaponNewCraftCost = patch(CraftingEntry.WeaponNewCraftCost, UnityEngine.Mathf.CeilToInt(normal * 1.4f), UnityEngine.Mathf.CeilToInt(rd.craftNewSaxoniteCost * 1.4f)),
                UpgradeCraftCost = patch(CraftingEntry.UpgradeCraftCost, null, null)
            };
        }
    }

    private static Entitlement BuildEntitlement(EntitlementPack ep)
    {
        ep.HasNametagIcon(out var icon);
        return new Entitlement
        {
            Name = ep.name,
            SteamID = ep.SteamID,
            PS5ID = string.IsNullOrEmpty(ep.PS5ID) ? null : ep.PS5ID,
            AvailableOnSteam = ep.AvailableOnSteam,
            PlayerFlagTag = GetPrivateField<string>(ep, "playerFlagTag") is { Length: > 0 } tag ? tag : null,
            NametagIcon = BuildIcon(icon),
            UnlockWithPack = ep.unlockWithPack?
                .Where(x => x.Upgrade != null)
                .Select(x => new Entitlement.Item { Upgrade = UpgradeKey(x.Upgrade.ID), Seed = x.Seed })
                .ToArray()
        };
    }

    private static Rock[] BuildRocks()
    {
        var rm = RockManager.Instance;
        if (rm == null) return null;
        return Enumerable.Range(0, rm.RockCount).Select(i =>
        {
            var a = rm.GetRock(i);
            return new Rock { SourceID = i, Asteroid = a != null ? a.name : null, Skin = a != null && a.Skin != null ? a.Skin.name : null };
        }).ToArray();
    }

    // ---- Dialogue catalog ---------------------------------------------------
}
