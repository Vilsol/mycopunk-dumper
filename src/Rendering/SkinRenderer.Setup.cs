using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BepInEx;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MycopunkDumper;

internal static partial class SkinRenderer
{
    private partial class RenderDriver
    {
        /// <summary>
        /// One-time per-skin setup: instantiate, configure, frame the camera,
        /// snapshot original materials. Camera position is set once and stays
        /// fixed across all FramesPerSkin (rotation-invariant via bounding sphere).
        /// </summary>
        private IEnumerator SetupSkinRender(IUpgradable gear, SkinUpgrade skin, Camera cam, GameObject stageRoot, SkinRenderContext ctx)
        {
            // Clear any previous staged content.
            for (int i = stageRoot.transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(stageRoot.transform.GetChild(i).gameObject);

            GameObject srcGo;
            if (gear is Character ch && ch.ThirdPersonModel != null) srcGo = ch.ThirdPersonModel;
            else { var src = gear as Component; if (src == null) yield break; srcGo = src.gameObject; }

            ctx.pivot = new GameObject("SkinRenderer.Pivot");
            ctx.pivot.transform.SetParent(stageRoot.transform, worldPositionStays: false);
            ctx.pivot.transform.localPosition = Vector3.zero;
            ctx.pivot.transform.localRotation = Quaternion.identity;
            SetLayerRecursively(ctx.pivot, 31);

            ctx.instance = UnityEngine.Object.Instantiate(srcGo, ctx.pivot.transform);
            ctx.instance.SetActive(true);
            SetLayerRecursively(ctx.instance, 31);

            foreach (var anim in ctx.instance.GetComponentsInChildren<Animator>(true)) anim.enabled = false;
            // Disable every MonoBehaviour on the instance so per-frame Update()
            // drift (Gun.cs sight tracking, recoil settle, mag wobble, etc.)
            // can't move sub-meshes and produce a "wobble" between rendered
            // frames. VisualEffect / ParticleSystem inherit from Behaviour (not
            // MonoBehaviour) so they're not in this list — the constellation /
            // crab particle visuals stay enabled by virtue of being skipped here.
            foreach (var mb in ctx.instance.GetComponentsInChildren<MonoBehaviour>(true))
                mb.enabled = false;
            foreach (var t in ctx.instance.GetComponentsInChildren<Transform>(true)) t.gameObject.SetActive(true);
            foreach (var r in ctx.instance.GetComponentsInChildren<Renderer>(true)) r.enabled = true;

            bool isGun = ctx.instance.GetComponentInChildren<Gun>(true) != null;
            bool isCharacter = gear is Character;
            ctx.baseRotation = isGun ? -90f : (isCharacter ? 180f : 0f);

            // Bounds at identity rotation (rotation-invariant via bounding sphere).
            ctx.instance.transform.localPosition = Vector3.zero;
            ctx.instance.transform.localRotation = Quaternion.identity;
            ctx.pivot.transform.localRotation = Quaternion.identity;
            var bounds = ComputeMeshBounds(ctx.instance);
            if (!bounds.HasValue) { UnityEngine.Object.Destroy(ctx.pivot); yield break; }
            var b = bounds.Value;
            ctx.instance.transform.localPosition = -b.center;
            ctx.instanceLocalPos = -b.center;

            // One-time camera framing — same for every frame of this skin.
            float radius = b.size.magnitude * 0.5f;
            if (radius < 0.005f) radius = 0.5f;
            float fovRad = cam.fieldOfView * Mathf.Deg2Rad;
            float margin = isGun ? 0.8f : 1.2f;
            float distance = radius / Mathf.Tan(fovRad * 0.5f) * margin;
            ctx.camPos = new Vector3(0f, b.size.y * 0.05f, -distance);
            cam.transform.position = ctx.camPos;
            cam.transform.LookAt(Vector3.zero);
            ctx.camRot = cam.transform.rotation;

            // Cache renderers list once — used in two places per frame (reset +
            // material swap). GetComponentsInChildren walks the hierarchy.
            ctx.renderers = ctx.instance.GetComponentsInChildren<Renderer>(true);

            // Snapshot original materials so we can reset between frames before
            // re-applying the skin with a new seed.
            ctx.originalMaterials = new Dictionary<Renderer, Material[]>();
            foreach (var r in ctx.renderers)
                ctx.originalMaterials[r] = (Material[])r.sharedMaterials.Clone();

            ctx.instUpgradable = ctx.instance.GetComponent<IUpgradable>() ?? gear;

            // Discover all forcing-eligible properties. Two buckets:
            //   - presetProps: SkinUpgradeProperty_Preset entries (preset isolation rotations)
            //   - modifierProps: chance-gated SkinUpgradePropertyRandStat entries (Hue/Trim/Sat/Chroma/...)
            // Always-on properties (Texture/Color/GunCrab/LuminanceBalance/FlipTrim/CharacterModel)
            // run unconditionally — we don't touch them.
            ctx.presetProps = new List<SkinUpgradeProperty_Preset>();
            ctx.modifierProps = new List<(UpgradeProperty, System.Reflection.FieldInfo)>();
            ctx.chanceSnapshot = new List<(UpgradeProperty, float)>();
            try
            {
                var enumerator = skin.GetProperties();
                while (enumerator.MoveNext())
                {
                    var p = enumerator.Current;
                    if (p == null) continue;
                    if (p is SkinUpgradeProperty_Preset preset)
                    {
                        ctx.presetProps.Add(preset);
                        ctx.chanceSnapshot.Add((preset, preset.chance));
                        continue;
                    }
                    if (p is SkinUpgradeProperty_Texture) continue; // deterministic
                    if (p is SkinUpgradeProperty_GunCrab) continue; // VFX prop spawn, deterministic per frame seed
                    // Treat anything with a `public float chance` field as chance-gated —
                    // covers both RandStat-derived subclasses (Hue/Trim/Sat/Coppertone/...)
                    // and standalone subclasses (Pixelated/Contrast/Emissive/Negative/TrickOrTreat).
                    var chanceField = p.GetType().GetField("chance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (chanceField == null || chanceField.FieldType != typeof(float)) continue;
                    var currentChance = (float)chanceField.GetValue(p);
                    ctx.modifierProps.Add((p, chanceField));
                    ctx.chanceSnapshot.Add((p, currentChance));
                    // Zero out at setup; per-rotation forcing toggles target back to 1.
                    chanceField.SetValue(p, 0f);
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"  preset/chance survey threw: {ex.Message}"); }

            // Build the rotation-target list: base + each preset (isolated) + each modifier (isolated).
            ctx.rotationTargets = new List<RotationTarget>();
            ctx.rotationTargets.Add(new RotationTarget
            {
                Slug = "base",
                Display = "(base)",
                PresetIdx = -1,
                ModifierIdx = -1,
            });
            for (int i = 0; i < ctx.presetProps.Count; i++)
            {
                var preset = ctx.presetProps[i].preset;
                var name = preset?.name ?? $"preset_{i}";
                ctx.rotationTargets.Add(new RotationTarget
                {
                    Slug = SanitizeName(name),
                    Display = name,
                    PresetIdx = i,
                    ModifierIdx = -1,
                });
            }
            // Track duplicate type names (a skin can have e.g. two Saturation entries
            // with different parameters) — disambiguate by appending an occurrence index.
            var typeCounts = new Dictionary<string, int>();
            for (int i = 0; i < ctx.modifierProps.Count; i++)
            {
                var typeName = ctx.modifierProps[i].prop.GetType().Name;
                var label = typeName.StartsWith("SkinUpgradeProperty_") ? typeName.Substring("SkinUpgradeProperty_".Length) : typeName;
                int count = typeCounts.TryGetValue(label, out var n) ? n : 0;
                typeCounts[label] = count + 1;
                var slug = count == 0 ? label : $"{label}_{count + 1}";
                ctx.rotationTargets.Add(new RotationTarget
                {
                    Slug = SanitizeName(slug),
                    Display = slug,
                    PresetIdx = -1,
                    ModifierIdx = i,
                });
            }

            // seedAffectsOutput remains true here only for the "frame 0 must run
            // Apply" gate; we now also re-apply on every segment boundary.
            ctx.seedAffectsOutput = true;
            ctx.lastSegmentIdx = -1;
            ctx.forcedSegmentIdx = -1;

            ctx.ready = true;
            yield break;
        }

        // Per-frame work: reset materials, re-apply skin with new seed, re-spawn
    }
}
