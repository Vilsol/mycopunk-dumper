namespace MycopunkDumper;

/// <summary>
/// Per-rarity pricing/visual data — the master cost table referenced by every
/// upgrade's `UnlockCost`/`AdditionalUnlockCost` and by the Ouroboros cleanse
/// flow. Sourced from <c>Global.Instance.Rarities[]</c> (a struct array, not a
/// ScriptableObject catalog). 6 entries: Standard, Rare, Epic, Exotic, Boosted,
/// Turbocharged. Keyed by <see cref="Name"/>.
/// </summary>
public class RarityEntry
{
    public string Name;                  // canonical key: "standard", "rare", "epic", "exotic", "boosted", "turbocharged"
    public string LocalizedName;         // RarityData.Name (localized display name)
    public string ColorTag;              // RarityData.ColorTag (hex RGB of `color`)
    public string BoostedName;           // RarityData.BoostedName, e.g. "Rare / Boosted"
    public string TurbochargedName;      // RarityData.TurbochargedName, e.g. "Exotic / Turbocharged"

    // Pricing — the headline numbers a calculator needs.
    public int UpgradeScripCost;         // base scrip cost to unlock at this rarity
    public int UpgradeRareResourceCost;  // base rare-resource cost
    public int CleanseCost;              // Ouroboros cleanse cost (in `ourosample` resource)
    public int CraftNewSaxoniteCost;     // saxonite price for "craft new" rolls
    public int CraftNewNormalResourceCost; // per-ingredient count base for crafting (see `crafting.ByRarity`)
    public Upgrade.DUnlockCost[] AdditionalUpgradeCost; // extra ingredients beyond scrip+rare

    // Cross-references
    public string ScrapResource;         // PlayerResource ID (e.g. "standardscrap"); resolves in `resources`

    // Theme
    public string Color;                 // Unity RGBA(r,g,b,a) string
    public string BackgroundColor;
    public string LightColor;

    public Upgrade.DIcon Icon;
}
