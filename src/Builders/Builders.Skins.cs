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
        };
        var baseSkin = GetPrivateField<global::Upgrade>(s, "baseSkin");
        if (baseSkin != null) skin.BaseSkin = UpgradeKey(baseSkin.ID);
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

        // Universal: chance + curve (most SkinUpgradeProperty subclasses extend
        // SkinUpgradePropertyRandStat which carries both).
        try
        {
            var chance = GetPrivateFieldNullable<float>(p, "chance");
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
                if (vfxCrab.myUpgrade != null) entry.CrabUpgrade = UpgradeKey(vfxCrab.myUpgrade.ID);
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
                if (gcList.myUpgrade != null) entry.CrabUpgrade = UpgradeKey(gcList.myUpgrade.ID);
                if (gcList.meshes != null) entry.CrabMeshes = gcList.meshes.Where(m => m != null).Select(m => m.name).ToArray();
                break;

            case SkinUpgradeProperty_GunCrab gunCrab:
                entry.CrabModel = gunCrab.crab?.name;
                if (gunCrab.myUpgrade != null) entry.CrabUpgrade = UpgradeKey(gunCrab.myUpgrade.ID);
                break;

            case SkinUpgradeProperty_VFXCustomProp vfxCustom:
                entry.VfxCustomPropName = vfxCustom.customProperty.propertyName;
                entry.VfxCustomPropType = vfxCustom.customProperty.propertyType.ToString();
                var v = vfxCustom.customProperty.value;
                entry.VfxCustomPropValue = new[] { v.x, v.y, v.z, v.w };
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

        var upgradableName = upgradable.ToString();
        var upgradableApiName = upgradable.Info?.APIName ?? upgradable.GetType().Name;

        foreach (var up in upgradable.Info.Upgrades)
        {
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

                // Turbocharge / Ouroboros costs (per-rarity formulas, computed by the game)
                try
                {
                    var tc = up.GetTurbochargeCost();
                    if (tc != null && tc.Count > 0)
                    {
                        upgradeOut.TurbochargeCost = tc.Select(c => new Upgrade.DUnlockCost
                        {
                            Count = c.count,
                            Resource = c.resource?.Name,
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
                            Resource = c.resource?.Name,
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
                        Resource = unlockCost[j].resource?.Name,
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
                            Resource = up.additionalUnlockCost[j].resource?.Name,
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
                    // Multi-seed sampling: most stats are deterministic, but some properties roll
                    // categorical values at runtime (e.g. random element from {Fire, Shock, Acid}
                    // via `GetRandomEffect`). Run with several seeds and accumulate distinct values.
                    const int seedCount = 16;
                    List<StatData> firstSeedStats = null;
                    var rolledValues = new SortedDictionary<string, SortedSet<string>>(); // statName → distinct values
                    bool failed = false;
                    for (int s = 0; s < seedCount && !failed; s++)
                    {
                        try
                        {
                            var enumerator = up.Properties.properties[i].GetStatData(new Pigeon.Math.Random(s), upgradable, new UpgradeInstance(up, upgradable));
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
                                Log.LogWarning($"Stat eval failed for upgrade {UpgradeKey(up.ID)} property {up.Properties.properties[i].GetType().Name} on {upgradableApiName}: {ex.GetType().Name}: {ex.Message}");
                                failed = true;
                            }
                            // Mid-iteration failures on later seeds are silently tolerated.
                        }
                    }
                    if (failed) continue;
                    var collected = firstSeedStats ?? new List<StatData>();

                    upgradeOut.Properties[i].StatsByUpgradable ??= new SortedDictionary<string, StatData[]>();
                    upgradeOut.Properties[i].StatsByUpgradable[upgradableApiName] = collected.ToArray();

                    upgradeOut.Properties[i].StatNames ??= new SortedSet<string>();
                    foreach (var sd in collected)
                    {
                        var name = StripRichText(sd.name);
                        if (!string.IsNullOrEmpty(name)) upgradeOut.Properties[i].StatNames.Add(name);
                    }

                    // Record only stats with >1 observed distinct value — i.e. genuinely categorical rolls.
                    SortedDictionary<string, string[]> nonDet = null;
                    foreach (var (name, values) in rolledValues)
                    {
                        if (values.Count <= 1) continue;
                        nonDet ??= new SortedDictionary<string, string[]>();
                        nonDet[name] = values.ToArray();
                    }
                    if (nonDet != null)
                    {
                        upgradeOut.Properties[i].RolledValuesByUpgradable ??= new SortedDictionary<string, SortedDictionary<string, string[]>>();
                        upgradeOut.Properties[i].RolledValuesByUpgradable[upgradableApiName] = nonDet;
                    }

                    // Capture ModifyProperties output — the free-form tooltip text the game shows
                    // after StatData lines. Some properties only contribute via this path.
                    try
                    {
                        var modText = "";
                        var inst = new UpgradeInstance(up, upgradable);
                        up.Properties.properties[i].ModifyProperties(ref modText, new Pigeon.Math.Random(0), upgradable, inst);
                        if (!string.IsNullOrEmpty(modText))
                        {
                            // Strip leading newlines + carriage returns the game prepends to delimit lines.
                            var trimmed = modText.Trim('\n', '\r', ' ');
                            if (!string.IsNullOrEmpty(trimmed))
                            {
                                upgradeOut.Properties[i].ModifyTextByUpgradable ??= new SortedDictionary<string, string>();
                                upgradeOut.Properties[i].ModifyTextByUpgradable[upgradableApiName] = trimmed;
                                upgradeOut.Properties[i].ModifyTextLines ??= new SortedSet<string>();
                                foreach (var line in trimmed.Split('\n'))
                                {
                                    var stripped = StripRichText(line);
                                    if (!string.IsNullOrEmpty(stripped)) upgradeOut.Properties[i].ModifyTextLines.Add(stripped);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.LogWarning($"ModifyProperties failed for {UpgradeKey(up.ID)} property {up.Properties.properties[i].GetType().Name} on {upgradableApiName}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
        }
    }
}
