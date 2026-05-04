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
        // Strip filesystem-unfriendly chars from a preset name so it can safely
        // become a directory name. Spaces become underscores; everything else
        // outside [A-Za-z0-9_.-] is dropped.
        private static string SanitizeName(string s)
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

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++)
                SetLayerRecursively(go.transform.GetChild(i).gameObject, layer);
        }

        /// <summary>
        /// Compute world-space bounds from MeshFilter.sharedMesh data, transformed
        /// through each filter's localToWorldMatrix. More reliable than
        /// Renderer.bounds when the prefab's renderers are tied to runtime-only
        /// state (drop pod's case — Renderer.bounds collapses but the mesh is
        /// still there).
        /// </summary>
        private static Bounds? ComputeMeshBounds(GameObject root)
        {
            Bounds? acc = null;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                var local = mesh.bounds;
                var min = local.min;
                var max = local.max;
                var m = mf.transform.localToWorldMatrix;
                // Transform 8 corners and grow bounds.
                Vector3[] corners = {
                    new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z),
                    new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z),
                    new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z),
                    new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z)
                };
                var first = m.MultiplyPoint3x4(corners[0]);
                var b = new Bounds(first, Vector3.zero);
                for (int i = 1; i < 8; i++) b.Encapsulate(m.MultiplyPoint3x4(corners[i]));
                if (acc.HasValue) { var combined = acc.Value; combined.Encapsulate(b); acc = combined; }
                else acc = b;
            }
            // Fallback: SkinnedMeshRenderer also has bounds (handy for character rigs).
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null) continue;
                var b = smr.bounds;
                if (b.size.magnitude < 0.001f) continue;
                if (acc.HasValue) { var combined = acc.Value; combined.Encapsulate(b); acc = combined; }
                else acc = b;
            }
            return acc;
        }

        /// <summary>
        /// Manually instantiate any <c>SkinUpgradeProperty_GunCrab</c> (or its
        /// subclass <c>_VFXCrab</c>) prop attached to the skin. The game's
        /// in-engine path runs through <c>gun.GunCrabParent</c> + <c>gun.GunCrabLocation</c>
        /// in <c>SkinUpgradeProperty_GunCrab.SpawnInstance</c>; we do a light-weight
        /// version of the same so the crab/eye/pump/roach prop shows up in our
        /// preview.
        /// </summary>
        private void ManuallySpawnCrabProps(SkinUpgrade skin, GameObject instance, int seed, List<GameObject> spawned = null)
        {
            try
            {
                var enumerator = skin.GetProperties();
                int propIdx = 0;
                while (enumerator.MoveNext())
                {
                    propIdx++;
                    if (!(enumerator.Current is SkinUpgradeProperty_GunCrab gc) || gc.crab == null) continue;
                    var gun = instance.GetComponentInChildren<Gun>(true);
                    if (gun == null) continue;

                    // Call the game's own SpawnInstance via reflection. _VFXCrab
                    // overrides it to do the full VisualEffect setup (SetMesh, color
                    // overrides, Reinit+Play) — replicating that ourselves dropped
                    // pieces and made the constellation stars appear tiny/distant.
                    var rand = new Pigeon.Math.Random(global::Upgrade.ModifyPropertySeed(seed, propIdx));
                    // Cached MethodInfo lookup — first hit per subtype resolves it,
                    // subsequent calls reuse the cached value.
                    if (!_spawnInstanceCache.TryGetValue(gc.GetType(), out var spawn))
                    {
                        spawn = gc.GetType().GetMethod(
                            "SpawnInstance",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                        _spawnInstanceCache[gc.GetType()] = spawn;
                    }
                    GameObject prop = null;
                    if (spawn != null)
                    {
                        var args = new object[] { gun, rand };
                        try { prop = spawn.Invoke(gc, args) as GameObject; rand = (Pigeon.Math.Random)args[1]; }
                        catch (Exception ex) { Plugin.Log.LogWarning($"  SpawnInstance threw for {gc.GetType().Name}: {ex.InnerException?.Message ?? ex.Message}"); }
                    }
                    if (prop == null) continue;
                    SetLayerRecursively(prop, 31);
                    foreach (var r in prop.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
                    if (spawned != null) spawned.Add(prop);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"  ManuallySpawnCrabProps threw for {skin.APIName}: {ex.Message}");
            }
        }
    }
}
