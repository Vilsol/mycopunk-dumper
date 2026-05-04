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
        // crab props, rotate pivot, wait one frame, capture, write JPG.
        private IEnumerator RenderFrame(SkinUpgrade skin, int seed, float spinDegrees, SkinRenderContext ctx, Camera cam, string path, int[] resultBox)
        {
            // Re-pin camera/pivot/instance per frame.
            cam.transform.position = ctx.camPos;
            cam.transform.rotation = ctx.camRot;
            ctx.pivot.transform.localPosition = Vector3.zero;
            ctx.instance.transform.localPosition = ctx.instanceLocalPos;

            float totalProgress = (float)((float)spinDegrees / 360f);
            int segmentIdx = ctx.forcedSegmentIdx;

            // Re-Apply only when the rotation target changes. Within a rotation
            // we just rotate and update the hue shader property.
            bool needApply = segmentIdx != ctx.lastSegmentIdx;
            if (needApply)
            {
                ctx.lastSegmentIdx = segmentIdx;
                ctx.applied = true;

                // Reset all chance gates, then force exactly one target on.
                var target = ctx.rotationTargets[segmentIdx];
                for (int i = 0; i < ctx.presetProps.Count; i++)
                    ctx.presetProps[i].chance = (i == target.PresetIdx) ? 1f : 0f;
                for (int i = 0; i < ctx.modifierProps.Count; i++)
                {
                    var (mProp, mField) = ctx.modifierProps[i];
                    mField.SetValue(mProp, (i == target.ModifierIdx) ? 1f : 0f);
                }

                // Reset materials to originals before re-applying.
                foreach (var kv in ctx.originalMaterials)
                    if (kv.Key != null) kv.Key.sharedMaterials = kv.Value;

                // Tear down previously-spawned crab props.
                for (int i = 0; i < ctx.spawnedCrabs.Count; i++)
                    if (ctx.spawnedCrabs[i] != null) UnityEngine.Object.Destroy(ctx.spawnedCrabs[i]);
                ctx.spawnedCrabs.Clear();

                var owner = Pigeon.Movement.Player.LocalPlayer;
                var skinMaterials = new List<Material>();
                try { skin.Apply(ctx.instUpgradable, seed, owner, skinMaterials); }
                catch (Exception ex) { Plugin.Log.LogWarning($"  skin.Apply threw: {ex.Message}"); }
                ManuallySpawnCrabProps(skin, ctx.instance, seed, ctx.spawnedCrabs);
                ctx.lastSkinMaterials = skinMaterials;

                // Substitute override materials onto the renderers.
                try
                {
                    for (int ri = 0; ri < ctx.renderers.Length; ri++)
                    {
                        var r = ctx.renderers[ri];
                        if (r == null) continue;
                        var mats = r.sharedMaterials;
                        bool changed = false;
                        for (int i = 0; i < mats.Length; i++)
                        {
                            var swapped = skin.GetRenderMaterial(mats[i], skinMaterials);
                            if (swapped != mats[i]) { mats[i] = swapped; changed = true; }
                        }
                        if (changed) r.sharedMaterials = mats;
                    }
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"  material swap failed: {ex.Message}"); }
            }

            // Linear ramp 0 → HueSweepRange across the entire 360° rotation.
            // Decoupled from the preset segments so the hue drifts gently in
            // the background while presets swap on top — far less seizure-y
            // than cycling hue once per wedge.
            float hueValue = totalProgress * HueSweepRange;
            if (ctx.lastSkinMaterials != null)
            {
                for (int i = 0; i < ctx.lastSkinMaterials.Count; i++)
                {
                    var mat = ctx.lastSkinMaterials[i];
                    if (mat != null) mat.SetFloat(SkinUpgrade._HueShift, hueValue);
                }
            }

            // Drive VFX simulation manually since Time.timeScale = 0 freezes
            // the VisualEffect's automatic ticking. Without this, particle-based
            // skins (Constellation, PumpCrab, etc.) appear static across the
            // whole video. Use a FIXED step rather than unscaledDeltaTime so
            // simulation speed is independent of how fast the GPU actually rendered.
            foreach (var ve in ctx.instance.GetComponentsInChildren<UnityEngine.VFX.VisualEffect>(true))
                if (ve != null) ve.Simulate(VfxSimStepSeconds, 1);
            for (int i = 0; i < ctx.spawnedCrabs.Count; i++)
            {
                var prop = ctx.spawnedCrabs[i];
                if (prop == null) continue;
                foreach (var ve in prop.GetComponentsInChildren<UnityEngine.VFX.VisualEffect>(true))
                    if (ve != null) ve.Simulate(VfxSimStepSeconds, 1);
            }

            // Pivot rotation always changes per frame.
            ctx.pivot.transform.localRotation = Quaternion.Euler(0f, ctx.baseRotation + spinDegrees, 0f);

            yield return new WaitForEndOfFrame();

            // Throttle: if too many encode+write tasks are pending, yield frames
            // until some complete. Prevents OOM when main thread out-paces workers.
            while (_pendingWrites.Count >= MaxPendingWrites)
            {
                _pendingWrites.RemoveAll(t => t.IsCompleted);
                if (_pendingWrites.Count >= MaxPendingWrites) yield return null;
            }

            int sw = Screen.width, sh = Screen.height;
            int side = Mathf.Min(sw, sh);
            int x = (sw - side) / 2;
            int y = (sh - side) / 2;

            // Main-thread work only: ReadPixels into the pooled Texture2D, copy
            // its raw bytes out to a managed byte[] safe to hand off to a worker.
            // Pool the Texture2D itself so we don't pay the GPU alloc + GC cost
            // every frame (~786 KB LOH allocation).
            if (_captureTex == null || _captureTex.width != side || _captureTex.height != side)
            {
                if (_captureTex != null) UnityEngine.Object.Destroy(_captureTex);
                _captureTex = new Texture2D(side, side, TextureFormat.RGB24, false);
            }
            _captureTex.ReadPixels(new Rect(x, y, side, side), 0, 0);
            // GetRawTextureData() returns a fresh managed byte[] copy each call —
            // safe to send to a worker thread (independent of the texture buffer
            // which we'll overwrite next frame).
            byte[] rawBytes = _captureTex.GetRawTextureData();

            uint w = (uint)side, h = (uint)side;
            // ── LEGACY (kept for restore) ─────────────────────────────────
            // Synchronous path — encode + write on the main thread. Adds
            // ~10-20 ms per frame to wallclock.
            //
            // bool wrote = false;
            // try
            // {
            //     File.WriteAllBytes(path, ImageConversion.EncodeArrayToJPG(
            //         rawBytes, GraphicsFormat.R8G8B8_UNorm, w, h, 0, 90));
            //     wrote = true;
            // }
            // catch (Exception ex) { Plugin.Log.LogError($"  encode/write failed for {path}: {ex.Message}"); }
            // resultBox[0] = wrote ? 1 : -1;
            // ──────────────────────────────────────────────────────────────

            // FAST PATH: dispatch to ThreadPool. ImageConversion.EncodeArrayToJPG
            // is documented thread-safe; File.WriteAllBytes is OS-level and safe.
            var task = Task.Run(() =>
            {
                try
                {
                    var jpg = ImageConversion.EncodeArrayToJPG(rawBytes, GraphicsFormat.R8G8B8_UNorm, w, h, 0, 90);
                    File.WriteAllBytes(path, jpg);
                }
                catch (Exception ex)
                {
                    // Logger isn't thread-safe in BepInEx; defer the message via
                    // the next-frame log call. For now eat it — the missing file
                    // shows up in the post-render PNG-count check.
                    System.Threading.Interlocked.Exchange(ref _lastEncodeError, ex.Message);
                }
            });
            _pendingWrites.Add(task);
            // Optimistically count as rendered; if the async write fails the
            // file simply won't exist and the JPG count summary will reflect it.
            resultBox[0] = 1;
        }
    }
}
