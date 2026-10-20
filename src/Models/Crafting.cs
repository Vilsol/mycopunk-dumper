namespace MycopunkDumper;

/// <summary>
/// Master crafting price table. Source: the <c>CraftingWindow</c> MonoBehaviour
/// prefab's <c>[SerializeField]</c> private cost arrays. Singleton — there is
/// exactly one crafting window prefab in the game.
/// </summary>
public class Crafting
{
    public int MinLevelToAccessCrafting;       // const in source; surfaced for display

    public Upgrade.DUnlockCost[] RandomCraftCost;       // roll a fully-random new upgrade
    public Upgrade.DUnlockCost[] WeaponCraftCost;       // roll a random upgrade for a specific gear
    public Upgrade.DUnlockCost[] UpgradeCraftCost;      // duplicate an existing upgrade
    public Upgrade.DUnlockCost[] RandomNewCraftCost;    // random roll restricted to not-yet-owned upgrades
    public Upgrade.DUnlockCost[] WeaponNewCraftCost;    // gear-specific roll restricted to not-yet-owned upgrades

    public Upgrade.DUnlockCost[] UpcraftToRareCost;     // tier-up: Standard → Rare
    public Upgrade.DUnlockCost[] UpcraftToEpicCost;     // Rare → Epic
    public Upgrade.DUnlockCost[] UpcraftToExoticCost;   // Epic → Exotic

    // Templates above are patched per selected rarity by CraftingWindow.SetRarity; these are the resolved costs.
    public System.Collections.Generic.SortedDictionary<string, RarityCosts> ByRarity;

    public class RarityCosts
    {
        public Upgrade.DUnlockCost[] RandomCraftCost;
        public Upgrade.DUnlockCost[] RandomNewCraftCost;
        public Upgrade.DUnlockCost[] WeaponCraftCost;
        public Upgrade.DUnlockCost[] WeaponNewCraftCost;
        public Upgrade.DUnlockCost[] UpgradeCraftCost;
    }
}
