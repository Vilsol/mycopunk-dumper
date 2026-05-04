using System.Collections.Generic;

namespace MycopunkDumper;

public class Upgrade {

    public string ID;
    public string APIName;
    public string Name;
    public string EffectType;
    public string UpgradeType;
    public string Rarity;
    public string Flags;
    public string Color;
    public string MustBeUnlockedFirst;
    public string Description;

    public int Priority;              // display sort priority
    public string CollectionSource;   // "WorldPool" | "UpgradeTree" | "DropsFromSource" | "HiddenAlways" | "HiddenIfNotOwned" | "Beta" | "LevelUp" | "Resources" | "None"
    public bool IsModded;
    public string ModGUID;            // empty for vanilla
    public DUnlockCost[] TurbochargeCost;   // resources required to turbocharge this upgrade (per Rarity formula)
    public DUnlockCost[] OuroborosCost;     // ouroscrap cost: 5 + Rarity*2
    [Newtonsoft.Json.JsonConverter(typeof(NativeConverter))]
    public HexMap Pattern;
    public DIcon Icon;
    public DProperty[] Properties;
    public DUnlockCost[] UnlockCost;
    public DUnlockCost[] AdditionalUnlockCost;
    public List<DUpgradeable> ApplicableTo;

    /// <summary>
    /// Cosmetic-specific data — populated only when <see cref="UpgradeType"/> == <c>"Cosmetic"</c>
    /// (i.e. the upgrade is a <c>SkinUpgrade</c>). Pulls together fields scattered across
    /// <c>SkinUpgrade</c> and its <c>SkinUpgradeProperty</c> subclasses into a single shape
    /// that's usable for a wiki/calculator without manual instance-ID chasing.
    /// </summary>
    public DSkin Skin;

    public class DIcon {
        public float[] Rect;
        public string Texture;
    }

    public class DProperty {
        public string Type;       // C# class name, e.g. "UpgradeProperty_Bruiser_Cannonball"
        public string Label;      // Prettified class name, e.g. "Bruiser Cannonball"
        public SortedSet<string> StatNames;  // Distinct stat names this property emits, rich-text stripped
        [Newtonsoft.Json.JsonConverter(typeof(NativeConverter))]
        public UpgradeProperty Raw;
        public SortedDictionary<string, StatData[]> StatsByUpgradable;
        // Free-form tooltip lines produced by `UpgradeProperty.ModifyProperties(ref string, ...)` — the
        // SECOND source of in-game tooltip text after StatData. Game-rendered text often only appears
        // here (e.g. "eye: 3" on Gun Crab skin properties, "Fire Rate: +7.58%" on Mini Cannon prisms).
        // Keyed by upgradable APIName, same shape as StatsByUpgradable.
        public SortedDictionary<string, string> ModifyTextByUpgradable;
        // Distinct, rich-text-stripped, sorted lines pulled out of ModifyTextByUpgradable for indexing.
        public SortedSet<string> ModifyTextLines;
        // For stats whose `value` field varies across RNG seeds (e.g. randomly-rolled element from
        // {Fire, Shock, Acid}), this captures all distinct values observed across N seed samples.
        // Outer key: upgradable APIName. Inner key: StatData.name (rich-text stripped).
        // Inner value: sorted list of all distinct `value` strings observed (rich-text intact).
        // Numeric stats with already-typed min/max won't usually appear here since their value
        // is just a sample from the existing range — only categorical rolls leave traces.
        public SortedDictionary<string, SortedDictionary<string, string[]>> RolledValuesByUpgradable;
    }

    public class DUnlockCost {
        public int Count;
        public string Resource;     // legacy: PlayerResource.Name (display name)
        public string ResourceID;   // canonical key into the `resources` map (PlayerResource.ID)
    }

    public class DUpgradeable {
        public string Name;
        public string APIName;
    }

    public class DSkin {
        public string BaseSkin;                  // upgrade key — parent skin this inherits visuals from (or null for root skins)
        public string CountWithInCodex;          // upgrade key — codex-collection alias (e.g. "Factory" and "Factory (Bloodmetal)" share a codex slot)
        public float ChanceToRemoveFromPool;     // 0..1 — probability this skin is removed from the world drop pool after rolling
        public bool ColorIcon;                   // whether the icon is tinted by the skin color at render time
        public bool ChangeHueIfNoModifiersApplied;
        public bool OverridesPattern;            // true if SkinUpgrade.overridePattern is set (skin tree uses a non-default HexMapProfile)
        public bool HasVfx;                      // any VFX-spawning property (VFXCrab, VFXCustomProp, VFXMod, GunCrab, GunCrab_List)
        public bool HasCrab;                     // any GunCrab-derived property (gun has a crab/eye/pump/roach companion)
        public bool HasCharacterModel;           // any CharacterModel property (replaces first/third-person mesh)
        public DIconAtlas IconAtlas;             // raw atlas-cropping data (textureSize, iconPosition, iconSize) for cropping the source asset
        public string[] NameModifiers;           // distinct instance-name modifier suffixes observed across seed sweep (e.g. "Bloodmetal", "Spectral, Sapphire")
        public DSkinModifier[] Modifiers;        // per-property summary: trim names, preset references, texture-replacement counts
        // Per-(gear, preset) relative paths to the rendered mp4 turntable. Outer key: gear APIName.
        // Inner key: preset slug ("base" for the no-preset rotation, otherwise sanitized preset name).
        // Value: path relative to the base extracted-dump dir (e.g. "skin-previews/wrangler_Factory__Bloodmetal/wrangler_Factory__Bloodmetal.mp4").
        public System.Collections.Generic.SortedDictionary<string, System.Collections.Generic.SortedDictionary<string, string>> Previews;
    }

    public class DIconAtlas {
        public string Texture;                   // source Texture2D asset name
        public int TextureSize;                  // SkinUpgrade.textureSize (default 4096) — divisor for the position/size scale factor
        public float[] Position;                 // [x,y] in atlas pixel-coordinates (pre-scaling)
        public float[] Size;                     // [w,h] in atlas pixel-coordinates (pre-scaling)
    }

    public class DSkinModifier {
        public string Type;                      // SkinUpgradeProperty subclass name (e.g. "SkinUpgradeProperty_Trim")
        public float? Chance;                    // roll chance (0..1) — null if subclass doesn't expose one
        public bool? CurveConstant;              // true when AnimationCurve evaluates to the same value at 0 and 1 (chance-style); null if no curve
        public float? CurveStart;                // curve.Evaluate(0)
        public float? CurveEnd;                  // curve.Evaluate(1)

        // SkinUpgradeProperty_Trim
        public string TrimID;                    // TextBlocks key
        public string TrimName;                  // resolved trim name ("Sapphire", "Topaz")
        public string TrimColor;                 // RGBA(...) — Unity Color stringified
        public string TrimSpecular;              // RGBA(...) — specular tint
        public float? TrimHueChance;             // 0..1 — chance the trim's hue is shifted instead of using the literal trimColor
        public float? TrimMaxHue;                // -1..1 — max hue shift magnitude when hue rolling fires
        public bool? TrimFlip;
        public bool? TrimAlpha;

        // SkinUpgradeProperty_Preset
        public string Preset;                    // UpgradePreset asset name (resolves in `upgradePresets`)

        // SkinUpgradeProperty_Texture
        public string TextureFind;               // source Material being replaced
        public string TextureMain;               // replacement albedo Texture2D
        public string TextureSpecular;           // replacement specular Texture2D
        public bool? TextureOverrideColor;
        public string TextureColor;              // RGBA(...) when overrideColor=true
        public string TextureShadowColor;
        public bool? TextureOverrideSpecularColor;
        public string TextureSpecularColor;
        public bool? TextureOverrideSmoothness;
        public float? TextureSmoothness;

        // SkinUpgradeProperty_Color
        public string ColorPrimary;              // RGBA(...)
        public string ColorShadow;               // RGBA(...)
        public int? ColorMaterialMin;            // Range<int> materialRange.min — restricts which override-mat indices receive the tint
        public int? ColorMaterialMax;

        // SkinUpgradeProperty_Chroma
        public float? ChromaSunBrightness;
        public float? ChromaShadowBrightness;

        // SkinUpgradeProperty_Emissive
        public string EmissiveStatID;            // TextBlocks key for the modifier's display name
        public string EmissiveStatName;          // resolved name
        public string EmissiveColor;             // RGBA(...)
        public float? EmissiveHueChance;
        public float? EmissiveMaxHue;
        public bool? EmissiveFlipTrim;

        // SkinUpgradeProperty_Neon — channel intensities, per primary
        public float? NeonRed;
        public float? NeonGreen;
        public float? NeonBlue;
        public float? NeonYellow;
        public float? NeonMagenta;
        public bool? NeonFlipTrim;

        // SkinUpgradeProperty_Contrast
        public float? Contrast;

        // SkinUpgradeProperty_ContrastRange
        public string ContrastRangeLabelID;
        public string ContrastRangeLabel;        // resolved

        // SkinUpgradeProperty_Overlay / _OverlayMat
        public string OverlayID;                 // _Overlay only
        public string OverlayLabel;              // resolved name
        public string OverlayTexture;            // overlayTex name
        public float? OverlayBoost;              // _Overlay only
        public float? OverlayTile;
        public bool? OverlayTriplanar;

        // SkinUpgradeProperty_TrickOrTreat
        public string TrickColor;                // RGBA(...)
        public string TreatColor;                // RGBA(...)
        public float? TrickHueChance;
        public float? TrickRandHueIntensity;
        public bool? TrickFlipTrim;

        // SkinUpgradeProperty_Infection
        public float? InfectionScale;

        // SkinUpgradeProperty_GunCrab + subclasses
        public string CrabModel;                 // crab GameObject prefab name
        public string CrabUpgrade;               // SkinUpgradeProperty_GunCrab.myUpgrade ID (UpgradeKey)
        public string[] CrabMeshes;              // SkinUpgradeProperty_GunCrab_List.meshes asset names

        // SkinUpgradeProperty_VFXCrab — only override fields that diverge from the base prefab
        public string VfxCrabNameID;             // TextBlocks key
        public string VfxCrabName;               // resolved
        public string VfxCrabNameColor;          // Rarity enum stringified
        public float? VfxCrabSizeOverride;       // -1 = no override
        public float? VfxCrabIntensityOverride;
        public float? VfxCrabRateOverride;
        public float? VfxCrabLongevityOverride;
        public bool? VfxCrabOverrideSpeed;
        public float? VfxCrabSpeedOverride;
        public bool? VfxCrabOverrideColor;
        public string VfxCrabColor1;             // RGBA(HDR)
        public string VfxCrabColor2;
        public float? VfxCrabAlphaOverride;

        // SkinUpgradeProperty_VFXCustomProp
        public string VfxCustomPropName;
        public string VfxCustomPropType;         // Float / Float2 / Float3 / Float4
        public float[] VfxCustomPropValue;       // Vector4 → 4 floats

        // SkinUpgradeProperty_CharacterModel
        public string CharacterModelArmL;        // SkinnedMeshRenderer mesh asset name
        public string CharacterModelArmR;
        public string CharacterModelThirdPerson;
    }
}
