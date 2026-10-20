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
    public bool CanTurbocharge;       // game's UpgradeInstance.CanTurbocharge rules, minus the exotic progression unlock
    public DUnlockCost[] TurbochargeCost;   // resources required to turbocharge this upgrade (per Rarity formula); null when !CanTurbocharge
    public DUnlockCost[] OuroborosCost;     // ouroscrap cost: 5 + Rarity*2
    [Newtonsoft.Json.JsonConverter(typeof(NativeConverter))]
    public HexMap Pattern;
    public DIcon Icon;
    public DTitle Title;              // player title this upgrade contributes; null when it grants none
    public DProperty[] Properties;
    public DUnlockCost[] UnlockCost;
    public DUnlockCost[] AdditionalUnlockCost;
    public List<DUpgradeable> ApplicableTo;
    public string DefaultUpgradeOf;   // character APIName when this is that character's innate ability (Character.DefaultUpgrade)

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

    /// <summary>
    /// <c>Upgrade.Title</c> (<c>TitleID</c>). Equipped upgrades contribute a title fragment each;
    /// the game composes the player's displayed title from the highest-<see cref="Priority"/> ones.
    /// The localization key carries two blocks: an adjective at 0 and a noun at 1
    /// (e.g. <c>t_frost</c> -> "Frosted" / "Glacier").
    /// </summary>
    public class DTitle {
        public string NameID;         // localization key (e.g. "t_frost")
        public string Adjective;      // block 0
        public string Noun;           // block 1
        public int Priority;          // higher wins when several equipped upgrades grant titles
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
        // Same as StatsByUpgradable / ModifyTextByUpgradable but evaluated on a turbocharged instance;
        // only present for upgradables where the output differs from the normal pass.
        public SortedDictionary<string, StatData[]> StatsByUpgradableTurbocharged;
        public SortedDictionary<string, string> ModifyTextByUpgradableTurbocharged;
        // GetTurbochargedInfo — the "When turbocharged:" hover lines shown before turbocharging.
        public SortedDictionary<string, string> TurbochargedInfoByUpgradable;
        // UpgradeProperty_UpgradeFlag / _PlayerUpgradeFlag: the flag int resolved against the enum
        // the game reads it with (e.g. "MycoGearUpgradeFlags" → ["InfectOnlyInCombat"]).
        public string FlagEnum;
        public string[] FlagNames;
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
        public string SkinType;                  // cosmetic slot: "Skin" | "Crab" | "Flair" | "Motion" | "None"
        public string RequiredEntitlement;       // key into `entitlements`; skin is only usable when that pack is owned
        public string SkinTypeName;              // localized slot label; note the game shows "Hat" instead of "Crab" when the gear is a Character
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

        // SkinUpgradeProperty_Hat / _MultiHat
        public string HatPrefab;
        public string HatMesh;
        public string[] HatMeshes;               // _MultiHat.meshes

        // SkinUpgradeProperty_Specular / _NegativeSpecularColor
        public string SpecularColor;             // RGBA(HDR)
        public float? SpecularShadowOverride;    // _Specular.shadowSpecular when overridden

        // SkinUpgradeProperty_Reload / _SwarmGun_Reload (Motion skins)
        public string ReloadAnimation;           // AnimationClip name
        public string ReloadLoopAnimation;       // _SwarmGun_Reload only
        public string ReloadEndAnimation;

        // SkinUpgradeProperty_RarityColoring: hardcoded roll (rand <= .52 Standard, .77 Rare, .92 Epic, else Exotic)
        // tinting the skin with that rarity's lightColor.
        public SortedDictionary<string, float> RarityColoringWeights;

        // SkinUpgradeProperty_SequentialOverlayMats
        public string[] OverlayTextures;

        // SkinUpgradeProperty_TextureMultiple — mainTex of each inner _Texture
        public string[] TextureMains;
    }
}
