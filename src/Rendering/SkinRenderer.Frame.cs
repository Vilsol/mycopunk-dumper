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
            // Re-pin camera/pivot/instance per frame. The camera is pinned again in
            // PinCamera, since a menu script moves it later in the frame.
            cam.transform.position = ctx.camPos;
            cam.transform.rotation = ctx.camRot;
            _pinPos = ctx.camPos;
            _pinRot = ctx.camRot;
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
                var skinMaterials = new List<SkinOverrideMaterial>();
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

                // Crabs only change on re-Apply, so the VFX set is stable for the
                // whole rotation — collect once instead of walking the hierarchy per frame.
                ctx.vfx = new List<UnityEngine.VFX.VisualEffect>(ctx.instance.GetComponentsInChildren<UnityEngine.VFX.VisualEffect>(true));
                for (int i = 0; i < ctx.spawnedCrabs.Count; i++)
                    if (ctx.spawnedCrabs[i] != null)
                        ctx.vfx.AddRange(ctx.spawnedCrabs[i].GetComponentsInChildren<UnityEngine.VFX.VisualEffect>(true));
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
                    var mat = ctx.lastSkinMaterials[i].Material;
                    if (mat != null) mat.SetFloat(SkinUpgrade._HueShift, hueValue);
                }
            }

            // Drive VFX simulation manually since Time.timeScale = 0 freezes
            // the VisualEffect's automatic ticking. Without this, particle-based
            // skins (Constellation, PumpCrab, etc.) appear static across the
            // whole video. Use a FIXED step rather than unscaledDeltaTime so
            // simulation speed is independent of how fast the GPU actually rendered.
            for (int i = 0; i < ctx.vfx.Count; i++)
                if (ctx.vfx[i] != null) ctx.vfx[i].Simulate(VfxSimStepSeconds, 1);

            // Pivot rotation always changes per frame.
            ctx.pivot.transform.localRotation = Quaternion.Euler(0f, ctx.baseRotation + spinDegrees, 0f);

            // Throttle: if too many readbacks/encodes are pending, yield frames
            // until some complete. Prevents OOM when main thread out-paces workers.
            // Done before WaitForEndOfFrame so the capture below always grabs the
            // frame that was just rendered.
            while (_pendingWrites.Count + _pendingReadbacks >= MaxPendingWrites)
            {
                _pendingWrites.RemoveAll(t => t.IsCompleted);
                if (_pendingWrites.Count + _pendingReadbacks >= MaxPendingWrites) yield return null;
            }

            yield return new WaitForEndOfFrame();

            int sw = Screen.width, sh = Screen.height;
            int side = Mathf.Min(sw, sh);
            int x = (sw - side) / 2;
            int y = (sh - side) / 2;

            // GPU-side copy of the backbuffer + async readback, so the main thread
            // never stalls waiting on the GPU (ReadPixels did, every frame).
            if (_captureRT == null || _captureRT.width != sw || _captureRT.height != sh)
            {
                if (_captureRT != null) { _captureRT.Release(); UnityEngine.Object.Destroy(_captureRT); }
                _captureRT = new RenderTexture(sw, sh, 0, RenderTextureFormat.ARGB32);
            }
            ScreenCapture.CaptureScreenshotIntoRenderTexture(_captureRT);
            // The window manager may resize the window after SetResolution, so scale the
            // centered square to a fixed RenderSize² on the GPU rather than trusting Screen.
            if (_scaledRT == null)
                _scaledRT = new RenderTexture(RenderSize, RenderSize, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(_captureRT, _scaledRT,
                new Vector2((float)side / sw, (float)side / sh),
                new Vector2((float)x / sw, (float)y / sh));

            uint w = RenderSize, h = RenderSize;
            bool flip = SystemInfo.graphicsUVStartsAtTop;
            _pendingReadbacks++;
            AsyncGPUReadback.Request(_scaledRT, 0, TextureFormat.RGBA32, req =>
            {
                _pendingReadbacks--;
                if (req.hasError) { _lastEncodeError = $"GPU readback failed for {path}"; return; }
                var rawBytes = req.GetData<byte>().ToArray();
                // ImageConversion.EncodeArrayToJPG is documented thread-safe;
                // File.WriteAllBytes is OS-level and safe.
                _pendingWrites.Add(Task.Run(() =>
                {
                    try
                    {
                        if (flip) FlipRows(rawBytes, RenderSize * 4, RenderSize);
                        var jpg = ImageConversion.EncodeArrayToJPG(rawBytes, GraphicsFormat.R8G8B8A8_UNorm, w, h, 0, 90);
                        File.WriteAllBytes(path, jpg);
                    }
                    catch (Exception ex)
                    {
                        // Logger isn't thread-safe in BepInEx; eat it — the missing
                        // file shows up in the post-render JPG-count check.
                        System.Threading.Interlocked.Exchange(ref _lastEncodeError, ex.Message);
                    }
                }));
            });
            // Optimistically count as rendered; if the async write fails the
            // file simply won't exist and the JPG count summary will reflect it.
            resultBox[0] = 1;
        }
    }
}
