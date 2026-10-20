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
    private static Upgrade.DSkin BuildSkin(SkinUpgrade s)
    {
        var skin = new Upgrade.DSkin
        {
            ChanceToRemoveFromPool = s.ChanceToRemoveFromPool,
            ColorIcon = s.ColorIcon(),
            ChangeHueIfNoModifiersApplied = GetPrivateField<bool>(s, "changeHueIfNoModifiersApplied"),
            OverridesPattern = GetPrivateField<HexMapProfile>(s, "overridePattern") != null,
            SkinType = s.SkinType.ToString(),
            SkinTypeName = LocText(SkinTypeKey(s.SkinType), 0),
        };
        var baseSkin = GetPrivateField<global::Upgrade>(s, "baseSkin");
        if (baseSkin != null) skin.BaseSkin = UpgradeKey(baseSkin.ID);
        var ent = GetPrivateField<EntitlementPack>(s, "requiredEntitlement");
        if (ent != null) skin.RequiredEntitlement = ent.name;
        var codex = s.CountWithInCodex;
        if (codex != null) skin.CountWithInCodex = UpgradeKey(codex.ID);

        // Icon atlas cropping data (consumers can pull the source pixels from
        // the atlas without round-tripping through Sprite.Create).
        try
        {
            var iconTex = GetPrivateField<UnityEngine.Texture2D>(s, "iconTexture");
            var iconPos = GetPrivateField<UnityEngine.Vector2>(s, "iconPosition");
            var iconSize = GetPrivateField<UnityEngine.Vector2>(s, "iconSize");
            var texSize = GetPrivateField<int>(s, "textureSize");
            if (iconTex != null)
            {
                skin.IconAtlas = new Upgrade.DIconAtlas
                {
                    Texture = iconTex.name,
                    TextureSize = texSize,
                    Position = new[] { iconPos.x, iconPos.y },
                    Size = new[] { iconSize.x, iconSize.y },
                };
            }
        }
        catch { }

        // Per-property modifier summary. Walk the resolved property list (which
        // includes inherited properties from baseSkin) and resolve every
        // subclass we know about. Also flips the Has* indicator flags.
        var mods = new List<Upgrade.DSkinModifier>();
        try
        {
            var enumerator = s.GetProperties();
            while (enumerator.MoveNext())
            {
                var p = enumerator.Current;
                if (p == null) continue;
                var entry = BuildSkinModifier(p);
                mods.Add(entry);
                if (p is SkinUpgradeProperty_GunCrab) skin.HasCrab = true;
                if (p is SkinUpgradeProperty_GunCrab || p is SkinUpgradeProperty_VFXMod) skin.HasVfx = true;
                if (p is SkinUpgradeProperty_CharacterModel) skin.HasCharacterModel = true;
            }
        }
        catch (Exception ex) { Log.LogWarning($"BuildSkin properties walk failed for {s.name}: {ex.Message}"); }
        skin.Modifiers = mods.ToArray();

        // Distinct name-modifier suffixes via deterministic seed sweep. We poll
        // GetInstanceName which composes "<name> (<mod1>, <mod2>)"; the
        // parenthetical is the part we care about.
        var nameMods = new SortedSet<string>(StringComparer.Ordinal);
        for (int seed = 0; seed < 64; seed++)
        {
            string composed;
            try { composed = s.GetInstanceName(seed); }
            catch { continue; }
            if (string.IsNullOrEmpty(composed)) continue;
            int open = composed.IndexOf('(');
            int close = composed.LastIndexOf(')');
            if (open <= 0 || close <= open) continue;
            var inner = composed.Substring(open + 1, close - open - 1).Trim();
            if (inner.Length > 0) nameMods.Add(inner);
        }
        if (nameMods.Count > 0) skin.NameModifiers = nameMods.ToArray();

        return skin;
    }

    // Mirrors SkinUpgrade.GetSkinTypeName minus its gear check — that branch swaps
    // "sk_crab" for "hat" on Characters, which is per-gear and not a property of the skin.
    private static string SkinTypeKey(SkinType type) => type switch
    {
        SkinType.Crab => "sk_crab",
        SkinType.Flair => "sk_flair",
        SkinType.Motion => "sk_anim",
        _ => "skin",
    };

    /// <summary>
    /// Resolve a single <c>SkinUpgradeProperty</c> (or any <c>UpgradeProperty</c>) into a
    /// summary entry. Used both for per-skin modifiers and for an UpgradePreset's own
    /// inner property list. Falls back to a Type+Chance shell for subclasses we don't
    /// special-case — the full data is still available via Properties[i].Raw on the
    /// parent upgrade entry.
    /// </summary>
    private static Upgrade.DSkinModifier BuildSkinModifier(UpgradeProperty p)
    {
        var entry = new Upgrade.DSkinModifier { Type = p.GetType().Name };

        // Universal: chance + curve. SkinUpgradeProperty.Chance covers subclasses that override
        // it without a backing field (RarityColoring, DevGild, MultiHat, ...).
        try
        {
            var chance = p is SkinUpgradeProperty sp ? sp.Chance : GetPrivateFieldNullable<float>(p, "chance");
            if (chance.HasValue) entry.Chance = chance.Value;
        }
        catch { }
        try
        {
            var curve = GetPrivateField<UnityEngine.AnimationCurve>(p, "curve");
            if (curve != null && curve.length > 0)
            {
                var start = curve.Evaluate(0f);
                var end = curve.Evaluate(1f);
                entry.CurveStart = start;
                entry.CurveEnd = end;
                entry.CurveConstant = Math.Abs(start - end) < 1e-6f;
            }
        }
        catch { }

        switch (p)
        {
            case SkinUpgradeProperty_Trim trim:
                entry.TrimID = trim.trimID;
                entry.TrimName = LocText(trim.trimID, 0);
                entry.TrimColor = trim.trimColor.ToString();
                entry.TrimSpecular = trim.trimSpecular.ToString();
                entry.TrimHueChance = trim.hueChance;
                entry.TrimMaxHue = trim.maxHue;
                entry.TrimFlip = trim.flipTrim;
                entry.TrimAlpha = trim.alphaTrim;
                break;

            case SkinUpgradeProperty_Preset preset:
                entry.Preset = preset.preset?.name;
                entry.Chance = preset.chance;
                break;

            case SkinUpgradeProperty_Texture tex:
                entry.TextureFind = tex.find?.name;
                entry.TextureMain = tex.mainTex?.name;
                entry.TextureSpecular = tex.specular?.name;
                entry.TextureOverrideColor = tex.overrideColor;
                if (tex.overrideColor)
                {
                    entry.TextureColor = tex.color.ToString();
                    entry.TextureShadowColor = tex.shadowColor.ToString();
                }
                entry.TextureOverrideSpecularColor = tex.overrideSpecularColor;
                if (tex.overrideSpecularColor) entry.TextureSpecularColor = tex.specularColor.ToString();
                entry.TextureOverrideSmoothness = tex.overrideSmoothness;
                if (tex.overrideSmoothness) entry.TextureSmoothness = tex.smoothness;
                // GetPropertyChance is constant 1f; null signals "always applies".
                entry.Chance = null;
                break;

            case SkinUpgradeProperty_Color color:
                entry.ColorPrimary = color.color.ToString();
                entry.ColorShadow = color.shadowColor.ToString();
                entry.ColorMaterialMin = color.materialRange.min;
                entry.ColorMaterialMax = color.materialRange.max;
                entry.Chance = null;
                break;

            case SkinUpgradeProperty_Chroma chroma:
                entry.ChromaSunBrightness = chroma.sunBrightness;
                entry.ChromaShadowBrightness = chroma.shadowBrightness;
                break;

            case SkinUpgradeProperty_Emissive emissive:
                entry.EmissiveStatID = emissive.statID;
                entry.EmissiveStatName = LocText(emissive.statID, 0);
                entry.EmissiveColor = emissive.color.ToString();
                entry.EmissiveHueChance = emissive.hueChance;
                entry.EmissiveMaxHue = emissive.maxHue;
                entry.EmissiveFlipTrim = emissive.flipTrim;
                break;

            case SkinUpgradeProperty_Neon neon:
                entry.NeonRed = neon.red;
                entry.NeonGreen = neon.green;
                entry.NeonBlue = neon.blue;
                entry.NeonYellow = neon.yellow;
                entry.NeonMagenta = neon.magenta;
                entry.NeonFlipTrim = neon.flipTrim;
                break;

            case SkinUpgradeProperty_Contrast contrast:
                entry.Contrast = contrast.contrast;
                break;

            case SkinUpgradeProperty_ContrastRange cr:
                entry.ContrastRangeLabelID = cr.labelID;
                entry.ContrastRangeLabel = LocText(cr.labelID, 0);
                break;

            case SkinUpgradeProperty_Overlay overlay:
                entry.OverlayID = overlay.overlayID;
                entry.OverlayLabel = LocText(overlay.overlayID, 0);
                entry.OverlayTexture = overlay.overlayTex?.name;
                entry.OverlayBoost = overlay.overlayBoost;
                entry.OverlayTile = overlay.overlayTile;
                entry.OverlayTriplanar = overlay.triplanar;
                break;

            case SkinUpgradeProperty_OverlayMat overlayMat:
                entry.OverlayTexture = overlayMat.overlayTex?.name;
                entry.OverlayTile = overlayMat.overlayTile;
                entry.OverlayTriplanar = overlayMat.triplanar;
                break;

            case SkinUpgradeProperty_TrickOrTreat tot:
                entry.TrickColor = tot.trickColor.ToString();
                entry.TreatColor = tot.treatColor.ToString();
                entry.TrickHueChance = tot.hueChance;
                entry.TrickRandHueIntensity = tot.randHueIntensity;
                entry.TrickFlipTrim = tot.flipTrim;
                break;

            case SkinUpgradeProperty_Infection infection:
                entry.InfectionScale = infection.scale;
                break;

            // VFXCrab inherits from GunCrab; check the more specific subclass first.
            case SkinUpgradeProperty_VFXCrab vfxCrab:
                entry.CrabModel = vfxCrab.crab?.name;
                entry.VfxCrabNameID = GetPrivateField<string>(vfxCrab, "nameID");
                entry.VfxCrabName = LocText(entry.VfxCrabNameID, 0);
                try { entry.VfxCrabNameColor = GetPrivateField<Rarity>(vfxCrab, "nameColor").ToString(); } catch { }
                entry.VfxCrabSizeOverride = GetPrivateField<float>(vfxCrab, "sizeOverride");
                entry.VfxCrabIntensityOverride = GetPrivateField<float>(vfxCrab, "intensityOverride");
                entry.VfxCrabRateOverride = GetPrivateField<float>(vfxCrab, "emissionRateOverride");
                entry.VfxCrabLongevityOverride = GetPrivateField<float>(vfxCrab, "longevityOverride");
                entry.VfxCrabOverrideSpeed = GetPrivateField<bool>(vfxCrab, "overrideSpeed");
                entry.VfxCrabSpeedOverride = GetPrivateField<float>(vfxCrab, "speedOverride");
                entry.VfxCrabOverrideColor = GetPrivateField<bool>(vfxCrab, "overrideColor");
                if (entry.VfxCrabOverrideColor == true)
                {
                    entry.VfxCrabColor1 = GetPrivateField<UnityEngine.Color>(vfxCrab, "color1Override").ToString();
                    entry.VfxCrabColor2 = GetPrivateField<UnityEngine.Color>(vfxCrab, "color2Override").ToString();
                }
                entry.VfxCrabAlphaOverride = GetPrivateField<float>(vfxCrab, "alphaOverride");
                break;

            case SkinUpgradeProperty_GunCrab_List gcList:
                entry.CrabModel = gcList.crab?.name;
                if (gcList.meshes != null) entry.CrabMeshes = gcList.meshes.Where(m => m != null).Select(m => m.name).ToArray();
                break;

            case SkinUpgradeProperty_GunCrab gunCrab:
                entry.CrabModel = gunCrab.crab?.name;
                break;

            case SkinUpgradeProperty_VFXCustomProp vfxCustom:
                entry.VfxCustomPropName = vfxCustom.customProperty.propertyName;
                entry.VfxCustomPropType = vfxCustom.customProperty.propertyType.ToString();
                var v = vfxCustom.customProperty.value;
                entry.VfxCustomPropValue = new[] { v.x, v.y, v.z, v.w };
                break;

            case SkinUpgradeProperty_MultiHat multiHat:
                entry.HatPrefab = multiHat.hatPrefab?.name;
                entry.HatMesh = multiHat.mesh?.name;
                if (multiHat.meshes != null) entry.HatMeshes = multiHat.meshes.Where(m => m != null).Select(m => m.name).ToArray();
                break;

            case SkinUpgradeProperty_Hat hat:
                entry.HatPrefab = hat.hatPrefab?.name;
                entry.HatMesh = hat.mesh?.name;
                break;

            case SkinUpgradeProperty_Specular spec:
                entry.SpecularColor = spec.specularColor.ToString();
                if (spec.shadowSpecular.useOverride) entry.SpecularShadowOverride = spec.shadowSpecular.value;
                break;

            case SkinUpgradeProperty_NegativeSpecularColor negSpec:
                entry.SpecularColor = negSpec.specularColor.ToString();
                break;

            case SkinUpgradeProperty_SwarmGun_Reload swarmReload:
                entry.ReloadAnimation = swarmReload.animation?.name;
                entry.ReloadLoopAnimation = swarmReload.loop?.name;
                entry.ReloadEndAnimation = swarmReload.end?.name;
                break;

            case SkinUpgradeProperty_Reload reload:
                entry.ReloadAnimation = reload.animation?.name;
                break;

            case SkinUpgradeProperty_RarityColoring:
                // Mirrors the hardcoded thresholds in SkinUpgradeProperty_RarityColoring.Apply.
                entry.RarityColoringWeights = new SortedDictionary<string, float>
                {
                    ["Standard"] = 0.52f, ["Rare"] = 0.25f, ["Epic"] = 0.15f, ["Exotic"] = 0.08f,
                };
                break;

            case SkinUpgradeProperty_SequentialOverlayMats seqOverlay:
                if (seqOverlay.overlayTextures != null) entry.OverlayTextures = seqOverlay.overlayTextures.Where(t => t != null).Select(t => t.name).ToArray();
                break;

            case SkinUpgradeProperty_TextureMultiple texMulti:
                if (texMulti.textures != null) entry.TextureMains = texMulti.textures.Where(t => t != null).Select(t => t.mainTex?.name).ToArray();
                break;

            case SkinUpgradeProperty_CharacterModel cm:
                entry.CharacterModelArmL = GetPrivateField<UnityEngine.Mesh>(cm, "firstPersonArmL")?.name;
                entry.CharacterModelArmR = GetPrivateField<UnityEngine.Mesh>(cm, "firstPersonArmR")?.name;
                entry.CharacterModelThirdPerson = GetPrivateField<UnityEngine.Mesh>(cm, "thirdPersonMesh")?.name;
                break;
        }

        return entry;
    }

    /// <summary>
    /// GetPrivateField that returns null instead of default(T) when the field is absent.
    /// Used for fields like <c>chance</c> that may or may not exist on a given
    /// SkinUpgradeProperty subclass — we want to omit Chance when the subclass doesn't
    /// have one rather than emitting a misleading 0.
    /// </summary>

    private static void PopulateSkinPreviews()
    {
        foreach (var kv in UpgradeMap)
        {
            var up = kv.Value;
            if (up.Skin == null || up.ApplicableTo == null) continue;
            // Match SkinRenderer's RotationTarget list ordering: base, then each preset
            // (preset SO name), then each chance-gated non-preset modifier (type name
            // minus the SkinUpgradeProperty_ prefix, with `_2`/`_3` suffixes for
            // duplicates).
            var slugs = new List<string> { "base" };
            if (up.Skin.Modifiers != null)
            {
                foreach (var m in up.Skin.Modifiers)
                {
                    if (m.Type == nameof(SkinUpgradeProperty_Preset) && !string.IsNullOrEmpty(m.Preset))
                        slugs.Add(SanitizeSlug(m.Preset));
                }
                var typeCounts = new Dictionary<string, int>();
                foreach (var m in up.Skin.Modifiers)
                {
                    var typeName = m.Type ?? "";
                    // Mirror the renderer's filter: skip presets (already added above)
                    // and always-on subclasses; only chance-gated SkinUpgradePropertyRandStat
                    // descendants get isolation rotations.
                    if (!IsChanceGatedModifierType(typeName)) continue;
                    var label = typeName.StartsWith("SkinUpgradeProperty_")
                        ? typeName.Substring("SkinUpgradeProperty_".Length)
                        : typeName;
                    int count = typeCounts.TryGetValue(label, out var n) ? n : 0;
                    typeCounts[label] = count + 1;
                    var slug = count == 0 ? label : $"{label}_{count + 1}";
                    slugs.Add(SanitizeSlug(slug));
                }
            }
            var byGear = new SortedDictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);
            foreach (var g in up.ApplicableTo)
            {
                if (string.IsNullOrEmpty(g.APIName)) continue;
                var byPreset = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (var slug in slugs)
                {
                    var dir = $"{g.APIName}_{up.APIName}__{slug}";
                    byPreset[slug] = $"skin-previews/{dir}/{dir}.mp4";
                }
                byGear[g.APIName] = byPreset;
            }
            up.Skin.Previews = byGear;
        }
    }

    // Mirrors SkinRenderer's modifierProps filter: a property qualifies for an
    // isolation rotation iff it has a public `float chance` field (the chance-gate)
    // and isn't one of the special-cased types handled elsewhere (Preset / Texture
    // / GunCrab*). Driven by type name because we walk dumped DSkinModifiers, not
    // live game objects. Cached because the dump iterates every (skin, modifier).
    private static readonly Dictionary<string, bool> _chanceGatedCache = new(StringComparer.Ordinal);
    private static bool IsChanceGatedModifierType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName) || !typeName.StartsWith("SkinUpgradeProperty_")) return false;
        if (_chanceGatedCache.TryGetValue(typeName, out var cached)) return cached;
        bool result;
        if (typeName == nameof(SkinUpgradeProperty_Preset)
            || typeName == nameof(SkinUpgradeProperty_Texture)
            || typeName == nameof(SkinUpgradeProperty_GunCrab)
            || typeName == nameof(SkinUpgradeProperty_GunCrab_List))
        {
            result = false;
        }
        else
        {
            var t = typeof(SkinUpgrade).Assembly.GetType(typeName);
            var f = t?.GetField("chance", BindingFlags.Public | BindingFlags.Instance);
            result = f != null && f.FieldType == typeof(float);
        }
        _chanceGatedCache[typeName] = result;
        return result;
    }

    private static string SanitizeSlug(string s)
    {
        if (string.IsNullOrEmpty(s)) return "preset";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c == ' ') sb.Append('_');
            else if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.') sb.Append(c);
        }
        var r = sb.ToString();
        return r.Length == 0 ? "preset" : r;
    }


    private static void ProcessUpgrades(IUpgradable upgradable)
    {
        if (upgradable?.Info?.Upgrades == null)
        {
            return;
        }

        foreach (var up in upgradable.Info.Upgrades) ProcessUpgrade(up, upgradable);
    }

    private static Upgrade ProcessUpgrade(global::Upgrade up, IUpgradable upgradable)
    {
        var upgradableName = upgradable.ToString();
        var upgradableApiName = upgradable.Info?.APIName ?? upgradable.GetType().Name;

        if (!UpgradeMap.TryGetValue(UpgradeKey(up.ID), out var upgradeOut))
        {
            upgradeOut = new Upgrade
            {
                ApplicableTo = [],
                ID = UpgradeKey(up.ID),
                APIName = up.APIName,
                Name = up.Name.Replace("\"", "\\\""),
                EffectType = up.EffectType.ToString(),
                UpgradeType = up.UpgradeType.ToString(),
                Rarity = up.Rarity.ToString(),
                Flags = up.Flags.ToString(),
                Color = up.Color.ToString(),
                Description = up.Description,
                Pattern = up.Pattern,
                MustBeUnlockedFirst = up.MustBeUnlockedFirst?.ToString(),
                Icon = BuildIcon(up.Icon),
                Priority = up.Priority,
                CollectionSource = GetPrivateField<object>(up, "collectionSource")?.ToString(),
                IsModded = up.IsModded,
                ModGUID = up.ModGUID
            };

            // Title fragment (GearUpgrade / PlayerUpgrade override the base's empty default).
            var title = up.Title;
            if (!string.IsNullOrEmpty(title.NameID))
            {
                upgradeOut.Title = new Upgrade.DTitle
                {
                    NameID = title.NameID,
                    Adjective = LocText(title.NameID, 0),
                    Noun = LocText(title.NameID, 1),
                    Priority = title.Priority
                };
            }

            // Turbocharge / Ouroboros costs (per-rarity formulas, computed by the game)
            upgradeOut.CanTurbocharge = CanTurbocharge(up);
            try
            {
                var tc = upgradeOut.CanTurbocharge ? up.GetTurbochargeCost() : null;
                if (tc != null && tc.Count > 0)
                {
                    upgradeOut.TurbochargeCost = tc.Select(c => new Upgrade.DUnlockCost
                    {
                        Count = c.count,
                        Resource = StripRichText(c.resource?.Name),
                        ResourceID = c.resource?.ID
                    }).ToArray();
                }
            }
            catch (Exception ex) { Log.LogWarning($"GetTurbochargeCost failed for {UpgradeKey(up.ID)}: {ex.Message}"); }

            try
            {
                var oc = up.GetOuroborosCost();
                if (oc != null && oc.Count > 0)
                {
                    upgradeOut.OuroborosCost = oc.Select(c => new Upgrade.DUnlockCost
                    {
                        Count = c.count,
                        Resource = StripRichText(c.resource?.Name),
                        ResourceID = c.resource?.ID
                    }).ToArray();
                }
            }
            catch (Exception ex) { Log.LogWarning($"GetOuroborosCost failed for {UpgradeKey(up.ID)}: {ex.Message}"); }

            // Cosmetic-specific data — only populated for SkinUpgrade instances.
            if (up is SkinUpgrade skin)
            {
                upgradeOut.Skin = BuildSkin(skin);
            }

            if (up.Properties.properties != null)
            {
                var properties = new Upgrade.DProperty[up.Properties.properties.Length];
                for (var i = 0; i < up.Properties.properties.Length; i++)
                {
                    var prop = up.Properties.properties[i];
                    var typeName = prop.GetType().Name;
                    properties[i] = new Upgrade.DProperty
                    {
                        Type = typeName,
                        Label = PrettifyPropertyType(typeName),
                        Raw = prop
                    };
                }
                upgradeOut.Properties = properties;
            }

            var unlockCost = up.GetUnlockCost();
            upgradeOut.UnlockCost = new Upgrade.DUnlockCost[unlockCost.Count];
            for (var j = 0; j < unlockCost.Count; j++)
            {
                upgradeOut.UnlockCost[j] = new Upgrade.DUnlockCost
                {
                    Count = unlockCost[j].count,
                    Resource = StripRichText(unlockCost[j].resource?.Name),
                    ResourceID = unlockCost[j].resource?.ID
                };
            }

            if (up.additionalUnlockCost != null)
            {
                upgradeOut.AdditionalUnlockCost = new Upgrade.DUnlockCost[up.additionalUnlockCost.Length];
                for (var j = 0; j < up.additionalUnlockCost.Length; j++)
                {
                    upgradeOut.AdditionalUnlockCost[j] = new Upgrade.DUnlockCost
                    {
                        Count = up.additionalUnlockCost[j].count,
                        Resource = StripRichText(up.additionalUnlockCost[j].resource?.Name),
                        ResourceID = up.additionalUnlockCost[j].resource?.ID
                    };
                }
            }

            UpgradeMap[UpgradeKey(up.ID)] = upgradeOut;
        }

        upgradeOut.ApplicableTo.Add(new Upgrade.DUpgradeable
        {
            Name = upgradableName,
            APIName = upgradableApiName
        });

        if (up.Properties.properties != null && upgradeOut.Properties != null)
        {
            var count = System.Math.Min(upgradeOut.Properties.Length, up.Properties.properties.Length);
            for (var i = 0; i < count; i++)
            {
                CollectPropertyStats(upgradeOut.Properties[i], up.Properties.properties[i], upgradable, upgradableApiName,
                    s => new UpgradeInstance(up, upgradable) { Seed = s }, UpgradeKey(up.ID));
                ResolveFlagNames(upgradeOut.Properties[i], up.Properties.properties[i], upgradable);
            }
        }
        return upgradeOut;
    }

    // Gear class → its per-gear flag enum where the name doesn't follow "<Class>UpgradeFlags".
    private static readonly Dictionary<string, Type> GearFlagEnumOverrides = new()
    {
        ["CartridgeSMG"] = typeof(SMGUpgradeFlags),
        ["ScoutLaserRifle"] = typeof(DMLRUpgradeFlags),
        ["FastReloadShotgun"] = typeof(FastShotgunUpgradeFlags),
        ["ScrapperGear"] = typeof(GrapplePoleUpgradeFlags),
        ["AcceleratorGun"] = typeof(AcceleratorUpgradeFlags),
    };

    private static Type GearFlagEnum(object gear)
    {
        for (var t = gear?.GetType(); t != null && t != typeof(object); t = t.BaseType)
        {
            if (GearFlagEnumOverrides.TryGetValue(t.Name, out var e)) return e;
            e = typeof(GearUpgradeFlags).Assembly.GetType(t.Name + "UpgradeFlags");
            if (e != null && e.IsEnum) return e;
        }
        return null;
    }

    // UpgradeFlag / PlayerUpgradeFlag store a bare int; the bits mean different things per gear.
    // Mirrors who reads them: gear upgrades → the gear's enum; character-tree upgrades → the
    // character's UtilityItem gear; Global → Player.UpgradeFlags (PlayerUpgradeFlags);
    // PlayerUpgradeFlagField → per-character enum, else PlayerUpgradeFlags. Bits the
    // specific enum doesn't define fall back to the shared GearUpgradeFlags/PlayerUpgradeFlags names.
    private static void ResolveFlagNames(Upgrade.DProperty dp, UpgradeProperty prop, IUpgradable upgradable)
    {
        if (dp.FlagNames != null) return;
        int bits;
        Type specific, shared = typeof(GearUpgradeFlags);
        switch (prop)
        {
            case UpgradeProperty_PlayerUpgradeFlag pf:
                bits = (int)pf.flags.flags;
                shared = typeof(PlayerUpgradeFlags);
                var c = pf.flags.character;
                var g = Global.Instance;
                specific = c == null ? null
                    : c == g.WranglerCharacter ? typeof(WranglerUpgradeFlags)
                    : c == g.BruiserCharacter ? typeof(BruiserUpgradeFlags)
                    : c == g.ScrapperCharacter ? typeof(ScrapperUpgradeFlags)
                    : c == g.GliderCharacter ? typeof(GliderUpgradeFlags)
                    : null;
                break;
            case UpgradeProperty_UpgradeFlag uf:
                bits = (int)uf.flags.flags;
                if (upgradable is Global) { specific = null; shared = typeof(PlayerUpgradeFlags); }
                else if (upgradable is Character ch) specific = GearFlagEnum(ch.UtilityItem?.GetComponent<IGear>());
                else specific = GearFlagEnum(upgradable);
                break;
            default:
                return;
        }
        var names = new List<string>();
        for (int b = 0; b < 32; b++)
        {
            int bit = 1 << b;
            if ((bits & bit) == 0) continue;
            string name = null;
            if (specific != null && Enum.IsDefined(specific, bit)) name = Enum.GetName(specific, bit);
            if (name == null && Enum.IsDefined(shared, bit)) name = Enum.GetName(shared, bit);
            names.Add(name ?? bit.ToString());
        }
        dp.FlagEnum = specific?.Name ?? shared.Name;
        dp.FlagNames = names.ToArray();
    }

    // Multi-seed StatData + ModifyProperties capture for one property against one upgradable.
    private static void CollectPropertyStats(Upgrade.DProperty dp, UpgradeProperty prop, IUpgradable upgradable,
        string upgradableApiName, Func<int, UpgradeInstance> makeInstance, string ownerKey)
    {
        // Multi-seed sampling: most stats are deterministic, but some properties roll
        // categorical values at runtime (e.g. random element from {Fire, Shock, Acid}
        // via `GetRandomEffect`). Run with several seeds and accumulate distinct values.
        // makeInstance gets the seed too: some properties (e.g. GrowGrid) roll from
        // UpgradeInstance.Seed, which the game's constructor randomizes.
        const int seedCount = 16;
        List<StatData> firstSeedStats = null;
        var rolledValues = new SortedDictionary<string, SortedSet<string>>(); // statName → distinct values
        bool failed = false;
        for (int s = 0; s < seedCount && !failed; s++)
        {
            try
            {
                var enumerator = prop.GetStatData(new Pigeon.Math.Random(s), upgradable, makeInstance(s));
                if (enumerator == null) { if (s == 0) { firstSeedStats = new List<StatData>(); } continue; }

                var thisSeed = new List<StatData>();
                try
                {
                    while (enumerator.MoveNext()) thisSeed.Add(enumerator.Current);
                }
                finally
                {
                    (enumerator as IDisposable)?.Dispose();
                }
                if (s == 0) firstSeedStats = thisSeed;
                foreach (var sd in thisSeed)
                {
                    if (string.IsNullOrEmpty(sd.value)) continue;
                    if (!rolledValues.TryGetValue(sd.name, out var set))
                    {
                        set = new SortedSet<string>();
                        rolledValues[sd.name] = set;
                    }
                    set.Add(sd.value);
                }
            }
            catch (Exception ex)
            {
                if (s == 0)
                {
                    Log.LogWarning($"Stat eval failed for upgrade {ownerKey} property {prop.GetType().Name} on {upgradableApiName}: {ex.GetType().Name}: {ex.Message}");
                    failed = true;
                }
                // Mid-iteration failures on later seeds are silently tolerated.
            }
        }
        if (failed) return;
        var collected = firstSeedStats ?? new List<StatData>();

        dp.StatsByUpgradable ??= new SortedDictionary<string, StatData[]>();
        dp.StatsByUpgradable[upgradableApiName] = collected.ToArray();

        dp.StatNames ??= new SortedSet<string>();
        foreach (var sd in collected)
        {
            var name = StripRichText(sd.name);
            if (!string.IsNullOrEmpty(name)) dp.StatNames.Add(name);
        }

        // Numeric rolls already carry an exhaustive min/max range in StatData; sampling
        // them across seeds only yields a non-exhaustive subset of that range, so exclude
        // those names. What remains are genuinely categorical rolls (e.g. random element)
        // that have no min/max and can only be discovered by sampling.
        var numericNames = new HashSet<string>();
        foreach (var sd in collected)
        {
            if (sd.minValue != null && sd.maxValue != null) numericNames.Add(sd.name);
        }

        // Record only stats with >1 observed distinct value — i.e. genuinely categorical rolls.
        SortedDictionary<string, string[]> nonDet = null;
        foreach (var (name, values) in rolledValues)
        {
            if (values.Count <= 1) continue;
            if (numericNames.Contains(name)) continue;
            nonDet ??= new SortedDictionary<string, string[]>();
            nonDet[name] = values.ToArray();
        }
        if (nonDet != null)
        {
            dp.RolledValuesByUpgradable ??= new SortedDictionary<string, SortedDictionary<string, string[]>>();
            dp.RolledValuesByUpgradable[upgradableApiName] = nonDet;
        }

        // Capture ModifyProperties output — the free-form tooltip text the game shows
        // after StatData lines. Some properties only contribute via this path.
        try
        {
            var modText = "";
            var inst = makeInstance(0);
            prop.ModifyProperties(ref modText, new Pigeon.Math.Random(0), upgradable, inst);
            if (!string.IsNullOrEmpty(modText))
            {
                // Strip leading newlines + carriage returns the game prepends to delimit lines.
                var trimmed = modText.Trim('\n', '\r', ' ');
                if (!string.IsNullOrEmpty(trimmed))
                {
                    dp.ModifyTextByUpgradable ??= new SortedDictionary<string, string>();
                    dp.ModifyTextByUpgradable[upgradableApiName] = trimmed;
                    dp.ModifyTextLines ??= new SortedSet<string>();
                    foreach (var line in trimmed.Split('\n'))
                    {
                        var stripped = StripRichText(line);
                        if (!string.IsNullOrEmpty(stripped)) dp.ModifyTextLines.Add(stripped);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.LogWarning($"ModifyProperties failed for {ownerKey} property {prop.GetType().Name} on {upgradableApiName}: {ex.GetType().Name}: {ex.Message}");
        }

        if (makeInstance(0) is { } probe && CanTurbocharge(probe.Upgrade))
            CollectTurbochargedStats(dp, prop, upgradable, upgradableApiName, makeInstance, collected);
    }

    // UpgradeInstance.CanTurbocharge minus its exotic progression check (needs PlayerData).
    private static bool CanTurbocharge(global::Upgrade up)
    {
        if (up.Rarity > Rarity.Exotic) return false;
        if (up.Rarity == Rarity.Exotic) return (up.Flags & global::Upgrade.UpgradeFlags.CanTurbocharge) != 0;
        return !up.IsSkin() && (up.Flags & (global::Upgrade.UpgradeFlags.NoRandom | global::Upgrade.UpgradeFlags.CantBoost)) == 0;
    }

    // Second pass with the instance's Turbocharged bit set directly (the IsTurbocharged setter
    // writes PlayerData). Many properties only yield stats/text when turbocharged. Seed 0 only,
    // matching StatsByUpgradable; emitted only where it differs beyond maxed rolls.
    private static void CollectTurbochargedStats(Upgrade.DProperty dp, UpgradeProperty prop, IUpgradable upgradable,
        string upgradableApiName, Func<int, UpgradeInstance> makeInstance, List<StatData> normal)
    {
        UpgradeInstance MakeTurbo()
        {
            var inst = makeInstance(0);
            if (inst != null) inst.flags |= UpgradeInstance.Flags.Turbocharged;
            return inst;
        }
        if (makeInstance(0) == null) return;

        try
        {
            var turbo = new List<StatData>();
            var enumerator = prop.GetStatData(new Pigeon.Math.Random(0), upgradable, MakeTurbo());
            if (enumerator != null)
            {
                try { while (enumerator.MoveNext()) turbo.Add(enumerator.Current); }
                finally { (enumerator as IDisposable)?.Dispose(); }
            }
            // Turbocharging maxes every ranged roll (StatData.Create: value = max); that's implied by
            // maxValue, so ignore `value` on ranged stats when deciding whether turbo adds anything.
            static string Shape(List<StatData> l) => JsonConvert.SerializeObject(l.Select(sd =>
            {
                if (sd.minValue != null && sd.maxValue != null) sd.value = null;
                return sd;
            }));
            if (Shape(turbo) != Shape(normal))
            {
                dp.StatsByUpgradableTurbocharged ??= new SortedDictionary<string, StatData[]>();
                dp.StatsByUpgradableTurbocharged[upgradableApiName] = turbo.ToArray();
            }
        }
        catch { }

        try
        {
            var normalText = "";
            prop.ModifyProperties(ref normalText, new Pigeon.Math.Random(0), upgradable, makeInstance(0));
            var turboText = "";
            prop.ModifyProperties(ref turboText, new Pigeon.Math.Random(0), upgradable, MakeTurbo());
            turboText = turboText?.Trim('\n', '\r', ' ');
            if (!string.IsNullOrEmpty(turboText) && turboText != normalText?.Trim('\n', '\r', ' '))
            {
                dp.ModifyTextByUpgradableTurbocharged ??= new SortedDictionary<string, string>();
                dp.ModifyTextByUpgradableTurbocharged[upgradableApiName] = turboText;
            }
        }
        catch { }

        // Hover popup's "When turbocharged:" lines, shown before the upgrade is turbocharged.
        try
        {
            var info = prop.GetTurbochargedInfo(new Pigeon.Math.Random(0), upgradable, makeInstance(0));
            if (!string.IsNullOrEmpty(info))
            {
                dp.TurbochargedInfoByUpgradable ??= new SortedDictionary<string, string>();
                dp.TurbochargedInfoByUpgradable[upgradableApiName] = info.Trim('\n', '\r', ' ');
            }
        }
        catch { }
    }
}
