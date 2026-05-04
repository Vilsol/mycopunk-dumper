namespace MycopunkDumper;

/// <summary>
/// Master crafting price table. Source: the <c>CraftingWindow</c> MonoBehaviour
/// prefab's <c>[SerializeField]</c> private cost arrays. Singleton — there is
/// exactly one crafting window prefab in the game.
/// </summary>
public class Crafting
{
    public int MinLevelToAccessCrafting;       // const 10 in source; surfaced for display

    public Upgrade.DUnlockCost[] RandomCraftCost;       // roll a fully-random new upgrade
    public Upgrade.DUnlockCost[] WeaponCraftCost;       // roll a random upgrade for a specific gear
    public Upgrade.DUnlockCost[] UpgradeCraftCost;      // duplicate an existing upgrade

    public Upgrade.DUnlockCost[] UpcraftToRareCost;     // tier-up: Standard → Rare
    public Upgrade.DUnlockCost[] UpcraftToEpicCost;     // Rare → Epic
    public Upgrade.DUnlockCost[] UpcraftToExoticCost;   // Epic → Exotic
}
