namespace MycopunkDumper;

/// <summary>
/// A named bundle of <see cref="UpgradeProperty"/> overrides referenced by skins
/// via <c>SkinUpgradeProperty_Preset.preset</c>. Each entry is a small ScriptableObject
/// (e.g. "Coppertone", "Bloodmetal", "Spectral", "Entombed") that, when its parent
/// skin's preset roll succeeds, applies a coherent set of visual modifiers.
///
/// Catalog source: <c>UnityEngine.Resources.FindObjectsOfTypeAll&lt;UpgradePreset&gt;()</c>.
/// Cross-referenced from <c>upgrades[*].Skin.Modifiers[].Preset</c>.
/// Keyed by SO asset name.
/// </summary>
public class UpgradePresetEntry
{
    public string Name;                        // SO asset name
    public string OverrideNameModifier;        // suffix applied to a skin's display name when this preset rolls (e.g. "Coppertone")
    public string NameModifierColor;           // hex color tag the modifier name is wrapped in
    public bool ShowNameInStats;
    public int RandomContainerRangeMin;
    public int RandomContainerRangeMax;

    /// <summary>
    /// Resolved per-property summary for the preset's own UpgradePropertyList — same
    /// shape as <c>Upgrade.DSkinModifier</c>. Lets a consumer answer
    /// "what does Glittering preset do?" without parsing <see cref="RawData"/>.
    /// </summary>
    public Upgrade.DSkinModifier[] Modifiers;

    /// <summary>
    /// Full preset SO via JsonUtility. Captures the inner <c>UpgradePropertyList</c>
    /// (which themselves are <c>UpgradeProperty</c> subclasses — colors, trim refs,
    /// texture overrides — same shape as the per-skin properties).
    /// </summary>
    [Newtonsoft.Json.JsonConverter(typeof(NativeConverter))]
    public global::UpgradePreset RawData;
}
