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
            MinLevelToAccessCrafting = 10, // const in source — CraftingWindow.MinLevelToAccessCrafting
            RandomCraftCost = read("randomCraftCost"),
            WeaponCraftCost = read("weaponCraftCost"),
            UpgradeCraftCost = read("upgradeCraftCost"),
            UpcraftToRareCost = read("upcraftToRareCost"),
            UpcraftToEpicCost = read("upcraftToEpicCost"),
            UpcraftToExoticCost = read("upcraftToExoticCost")
        };
    }

    // ---- Dialogue catalog ---------------------------------------------------
}
