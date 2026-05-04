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
        private static string _lastEncodeError;

        /// <summary>
        /// Destroy the per-skin staged content. Called at the end of each skin's
        /// frame loop.
        /// </summary>
        private void TeardownSkinRender(SkinRenderContext ctx)
        {
            // Restore chance values we mutated for forced-preset cycling. The
            // SkinUpgradeProperty objects MAY be shared across skins in some
            // SerializeReference setups, so leaving them at chance=0 / 1 would
            // leak state into subsequent skins.
            if (ctx.chanceSnapshot != null)
            {
                // Map prop → cached FieldInfo so we can restore non-RandStat
                // subclasses (Pixelated/Negative/Contrast/Emissive/TrickOrTreat)
                // whose `chance` field isn't reachable through a common base type.
                var fieldByProp = new Dictionary<UpgradeProperty, System.Reflection.FieldInfo>();
                if (ctx.modifierProps != null)
                    foreach (var (p, f) in ctx.modifierProps) fieldByProp[p] = f;

                foreach (var (p, original) in ctx.chanceSnapshot)
                {
                    if (p == null) continue;
                    if (p is SkinUpgradeProperty_Preset preset) { preset.chance = original; continue; }
                    if (fieldByProp.TryGetValue(p, out var field)) { field.SetValue(p, original); continue; }
                    if (p is SkinUpgradePropertyRandStat rs) rs.chance = original;
                }
            }
            foreach (var p in ctx.spawnedCrabs) if (p != null) UnityEngine.Object.Destroy(p);
            ctx.spawnedCrabs.Clear();
            if (ctx.pivot != null) UnityEngine.Object.Destroy(ctx.pivot);
        }
    }
}
