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

/// <summary>
/// Render-to-texture pass that captures 360° turntables for each (gear, skin,
/// modifier) combination using the game's actual shaders/materials. Triggered
/// by <c>$MYCOPUNK_DIR/render-skins.flag</c>; does nothing in headless mode.
///
/// Output: one folder per (gear, skin, rotation-target) under
/// <c>$MYCOPUNK_DIR/skin-previews/</c> containing <c>frame_NNN.jpg</c> files,
/// plus a <c>skin-previews.done</c> marker the mise task polls for. The
/// post-render bash step encodes each folder into a single mp4.
///
/// Implementation pattern lifted from <c>GearDetailsWindow.RefreshSkin</c>:
///   skinUpgrade.Apply(gear, seed, Player.LocalPlayer, skinMaterials)
/// then render the gear's renderers swapping their materials via
/// <c>appliedSkin.GetRenderMaterial(originalMat, skinMaterials)</c>.
/// </summary>
internal static partial class SkinRenderer
{
    private const int RenderWidth = 512;
    private const int RenderHeight = 512;
    private const int FramesPerSkin = 180;       // frames spanning a full 360° rotation (2° per step); seed varies per frame
    // How far to sweep _HueShift within a segment. Using a sine ease (0→peak→0)
    // so hue is at neutral at segment boundaries and gently dips through the
    // midpoint. Lower values are calmer; ranges around 10-15 work well for busy
    // patterns (Factory, Inferno, Graffiti) without strobing. Bump to 100 for
    // a full rainbow sweep per segment.
    private const float HueSweepRange = 5f;

    // VFX simulation step per render frame (seconds). Drives Constellation /
    // PumpCrab particle motion. Larger = faster particle activity per video
    // frame. 1/24 ≈ real time at 24fps; 1/8 was ~3× speed (too frantic for
    // Constellations). 1/20 is a gentle drift.
    private const float VfxSimStepSeconds = 1f / 20f;
    private const float WaitForMenuSeconds = 10f; // give Mycopunk time to load past the splash

    private static string _outDir;
    private static string _doneMarker;

    public static void Run()
    {
        var flag = Path.Combine(Paths.GameRootPath, "render-skins.flag");
        if (!File.Exists(flag)) return;
        if (Application.isBatchMode)
        {
            Plugin.Log.LogWarning("SkinRenderer: render-skins.flag set but the game is running in -batchmode -nographics. Remove those launch flags and re-run.");
            return;
        }

        _outDir = Path.Combine(Paths.GameRootPath, "skin-previews");
        _doneMarker = Path.Combine(Paths.GameRootPath, "skin-previews.done");
        Directory.CreateDirectory(_outDir);
        if (File.Exists(_doneMarker)) File.Delete(_doneMarker);

        // Spawn a host MonoBehaviour to run a coroutine — Unity needs an active
        // GameObject and component instance to drive coroutines.
        var host = new GameObject("MycopunkDumper.SkinRenderer");
        UnityEngine.Object.DontDestroyOnLoad(host);
        var driver = host.AddComponent<RenderDriver>();
        driver.StartCoroutine(driver.RenderAll());
    }

    // Cap concurrent encode+write workers — too many = OOM (each pending write
    // holds the raw RGB24 buffer in memory ≈ 786 KB). 8 is a good balance:
    // saturates ~4-8 cores during encode without ballooning RAM.
    private const int MaxPendingWrites = 8;

    // Reflection cache for SkinUpgradeProperty_GunCrab.SpawnInstance —
    // the GetMethod() call costs ~3-5 ms; resolving it once per subtype
    // and reusing the MethodInfo is essentially free.
    private static readonly Dictionary<Type, System.Reflection.MethodInfo> _spawnInstanceCache = new();

    private partial class RenderDriver : MonoBehaviour
    {
        // Outstanding async encode+write tasks. Drained at session end so the
        // skin-previews.done marker only fires after every JPG is on disk.
        private readonly List<Task> _pendingWrites = new List<Task>();

        // Pooled capture texture — reused across all frames in a session
        // (Screen.width/height don't change mid-run). Avoids a per-frame
        // Texture2D alloc + Destroy, plus the GC churn of LOH allocations.
        private Texture2D _captureTex;

        // Live progress text rendered via OnGUI at the top-left of the screen.
        // ReadPixels captures a CENTERED square `Min(sw, sh)` per side, so on a
        // 16:9 display this text lives in the left side-band and never makes it
        // into the captured PNGs.
        private string _statusText;

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(_statusText)) return;
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                normal = { textColor = Color.white },
                alignment = TextAnchor.UpperLeft,
                richText = false,
            };
            // Black drop-shadow for legibility against any background colour.
            var shadow = new GUIStyle(style) { normal = { textColor = Color.black } };
            GUI.Label(new Rect(11f, 11f, 600f, 600f), _statusText, shadow);
            GUI.Label(new Rect(10f, 10f, 600f, 600f), _statusText, style);
        }

        public IEnumerator RenderAll()
        {
            Plugin.Log.LogInfo($"SkinRenderer: waiting {WaitForMenuSeconds}s for menu to load…");
            yield return new WaitForSeconds(WaitForMenuSeconds);

            int rendered = 0, failed = 0, skipped = 0;
            try
            {
                // Strategy switch: URP refuses to honor our targetTexture (likely
                // a custom renderer override in Mycopunk). Co-opt the existing
                // main camera which is already rendering correctly to the screen,
                // hide the menu UI canvases for the duration, and capture the
                // screen via ReadPixels(Rect(0, 0, Screen.width, Screen.height)).
                // Restore everything when done.

                Camera cam = Camera.main ?? UnityEngine.Object.FindObjectOfType<Camera>();
                if (cam == null)
                {
                    Plugin.Log.LogError("SkinRenderer: no usable Camera found in the scene; aborting.");
                    yield break;
                }

                // Save state for later restoration.
                var savedPos = cam.transform.position;
                var savedRot = cam.transform.rotation;
                var savedParent = cam.transform.parent;
                var savedClearFlags = cam.clearFlags;
                var savedBg = cam.backgroundColor;
                var savedFov = cam.fieldOfView;
                var savedNear = cam.nearClipPlane;
                var savedFar = cam.farClipPlane;
                var savedMask = cam.cullingMask;

                // Detach from any animated parent rig.
                cam.transform.SetParent(null, worldPositionStays: true);

                // Disable URP TAA (sub-pixel jitter would show up as wobble).
                var urpData = cam.GetUniversalAdditionalCameraData();
                var savedAA = urpData.antialiasing;
                urpData.antialiasing = AntialiasingMode.None;

                // Freeze Time.timeScale so anything we missed (Rigidbody physics,
                // ParticleSystems with simulationSpace=World, FixedUpdate-driven
                // motion) doesn't drift the instance during WaitForEndOfFrame.
                var savedTimeScale = Time.timeScale;
                Time.timeScale = 0f;

                // Disable Vsync + uncap target frame rate. With Vsync on,
                // `WaitForEndOfFrame` returns once per display refresh (~16 ms
                // at 60 Hz). With it off, frames complete as fast as the GPU/CPU
                // can run them — typically ~5-10 ms for our simple scene, halving
                // the per-frame wallclock. Restored on teardown.
                var savedVsync = QualitySettings.vSyncCount;
                var savedTargetFps = Application.targetFrameRate;
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 0; // 0 = uncapped (>60fps possible)

                // Reconfigure for our render: solid dark background, layer 31 only.
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.08f, 0.08f, 0.10f, 1f);
                cam.fieldOfView = 30f;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 50f;
                cam.cullingMask = 1 << 31;

                // Hide every Canvas in the scene so the menu doesn't overlay our render.
                var canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
                var canvasOriginals = new List<(Canvas c, bool enabled)>();
                foreach (var c in canvases)
                {
                    canvasOriginals.Add((c, c.enabled));
                    c.enabled = false;
                }

                var lightGo = new GameObject("SkinRenderer.Light");
                lightGo.transform.position = new Vector3(2f, 5f, -2f);
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.2f;
                light.cullingMask = 1 << 31;

                var stageRoot = new GameObject("SkinRenderer.Stage");
                stageRoot.transform.position = Vector3.zero;
                SetLayerRecursively(stageRoot, 31);

                // Iterate gears + characters and their associated skin upgrades.
                var allUpgradables = new List<IUpgradable>();
                if (Global.Instance?.AllGear != null) allUpgradables.AddRange(Global.Instance.AllGear);
                if (Global.Instance?.Characters != null) allUpgradables.AddRange(Global.Instance.Characters);

                // Pre-count total rotations (= sum of [base + presets + chance-gated
                // modifiers] across unique skins) so the on-screen counter can show
                // "rotation N/M".
                int totalRotations = 0;
                foreach (var g in allUpgradables)
                {
                    if (g?.Info?.Upgrades == null) continue;
                    if (g is DropPod || g.Info.APIName == "droppod") continue;
                    var seen = new HashSet<string>();
                    foreach (var u in g.Info.Upgrades.OfType<SkinUpgrade>())
                    {
                        var key = u.Name ?? u.APIName;
                        if (!seen.Add(key)) continue;
                        int presetCount = 0;
                        int modCount = 0;
                        try
                        {
                            var pe = u.GetProperties();
                            while (pe.MoveNext())
                            {
                                var p = pe.Current;
                                if (p == null) continue;
                                if (p is SkinUpgradeProperty_Preset) { presetCount++; continue; }
                                if (p is SkinUpgradeProperty_Texture) continue;
                                if (p is SkinUpgradeProperty_GunCrab) continue;
                                var f = p.GetType().GetField("chance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                                if (f != null && f.FieldType == typeof(float)) modCount++;
                            }
                        }
                        catch { }
                        totalRotations += 1 + presetCount + modCount; // base + presets + modifiers
                    }
                }
                int currentRotationIdx = 0;

                foreach (var gear in allUpgradables)
                {
                    if (gear?.Info?.Upgrades == null) continue;
                    // DropPod's body mesh isn't on the prefab (loaded at mission
                    // spawn) so we can't render it from the menu. Skip outright.
                    if (gear is DropPod || gear.Info.APIName == "droppod") continue;
                    var skins = gear.Info.Upgrades.OfType<SkinUpgrade>().ToList();
                    if (skins.Count == 0) continue;

                    Plugin.Log.LogInfo($"SkinRenderer: {gear.Info.APIName} → {skins.Count} skins");
                    var seenNames = new HashSet<string>();
                    foreach (var skin in skins)
                    {
                        var dedupeKey = skin.Name ?? skin.APIName;
                        if (!seenNames.Add(dedupeKey)) { skipped++; continue; }

                        // Instantiate ONCE per skin; per frame just reset materials,
                        // re-apply skin with new seed, re-spin pivot, capture.
                        var ctx = new SkinRenderContext();
                        yield return StartCoroutine(SetupSkinRender(gear, skin, cam, stageRoot, ctx));
                        if (!ctx.ready) { skipped++; continue; }

                        // One full 360° rotation per RotationTarget. The list is
                        // [base, presets..., modifiers...] — each isolation rotation
                        // gets its own output folder so the bash post-pass encodes a
                        // separate mp4 per (skin, target).
                        int totalTargets = ctx.rotationTargets.Count;
                        for (int targetIdx = 0; targetIdx < totalTargets; targetIdx++)
                        {
                            currentRotationIdx++;
                            ctx.forcedSegmentIdx = targetIdx;
                            var target = ctx.rotationTargets[targetIdx];
                            var rotationDir = Path.Combine(_outDir, $"{gear.Info.APIName}_{skin.APIName}__{target.Slug}");
                            Directory.CreateDirectory(rotationDir);

                            for (int f = 0; f < FramesPerSkin; f++)
                            {
                                var angleDeg = f * (360f / FramesPerSkin);
                                float totalProgress = angleDeg / 360f;
                                float hueValue = totalProgress * HueSweepRange; // matches RenderFrame
                                _statusText =
                                    $"Rotation {currentRotationIdx}/{totalRotations}\n" +
                                    $"Gear: {gear.Info.APIName}\n" +
                                    $"Name: {skin.Name ?? skin.APIName}\n" +
                                    $"Target: {target.Display} ({targetIdx + 1}/{totalTargets})\n" +
                                    $"Hue: {hueValue:F1}\n" +
                                    $"Frame: {f + 1}/{FramesPerSkin}\n" +
                                    $"Total: {rendered}";
                                var seed = 1000 * skin.ID.ID + targetIdx * FramesPerSkin + f;
                                var path = Path.Combine(rotationDir, $"frame_{f:D3}.jpg");
                                var resultBox = new int[1] { -1 };
                                yield return StartCoroutine(RenderFrame(skin, seed, angleDeg, ctx, cam, path, resultBox));
                                if (resultBox[0] == 1) rendered++;
                                else if (resultBox[0] == 0) skipped++;
                                else failed++;
                            }
                        }
                        TeardownSkinRender(ctx);
                    }
                }

                // Drain outstanding async encode+write tasks before declaring
                // the run complete. Without this, the .done marker fires while
                // tasks are still flushing and the polling mise task reads a
                // partial JPG count.
                Plugin.Log.LogInfo($"SkinRenderer: capture done; draining {_pendingWrites.Count} pending writes…");
                while (_pendingWrites.Count > 0)
                {
                    _pendingWrites.RemoveAll(t => t.IsCompleted);
                    // WaitForSecondsRealtime is unaffected by Time.timeScale (which we
                    // pinned to 0 above to freeze physics/particles); WaitForSeconds
                    // would hang indefinitely here.
                    if (_pendingWrites.Count > 0) yield return new WaitForSecondsRealtime(0.1f);
                }
                if (!string.IsNullOrEmpty(_lastEncodeError))
                    Plugin.Log.LogWarning($"  some async encodes raised: {_lastEncodeError} (most recent)");

                Plugin.Log.LogInfo($"SkinRenderer: done. rendered={rendered} skipped={skipped} failed={failed}");

                // Release pooled capture texture.
                if (_captureTex != null) { UnityEngine.Object.Destroy(_captureTex); _captureTex = null; }

                // Restore camera + canvases. Re-parent first, then world transform.
                Time.timeScale = savedTimeScale;
                QualitySettings.vSyncCount = savedVsync;
                Application.targetFrameRate = savedTargetFps;
                urpData.antialiasing = savedAA;
                if (savedParent != null) cam.transform.SetParent(savedParent, worldPositionStays: true);
                cam.transform.position = savedPos;
                cam.transform.rotation = savedRot;
                cam.clearFlags = savedClearFlags;
                cam.backgroundColor = savedBg;
                cam.fieldOfView = savedFov;
                cam.nearClipPlane = savedNear;
                cam.farClipPlane = savedFar;
                cam.cullingMask = savedMask;
                foreach (var (c, e) in canvasOriginals)
                    if (c != null) c.enabled = e;
            }
            finally
            {
                File.WriteAllText(_doneMarker, $"rendered={rendered} skipped={skipped} failed={failed}\n");
            }

            // Quit so the polling mise task can pick the output up and so we
            // don't leave the renderer GameObject in the scene. Realtime wait —
            // timeScale may still be 0 here on certain restore-failure paths.
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit();
        }
    }
}
