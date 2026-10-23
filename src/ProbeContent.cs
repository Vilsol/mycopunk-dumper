using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MycopunkDumper;

// Post-dump probe for content FindObjectsOfTypeAll never sees at startup: every build scene and every
// StreamingAssets asset bundle. Writes scenes.txt, scenes/<scene>.txt, scene-components.txt, scene-catalogs.txt,
// bundles.txt, bundles/<bundle>.txt, bundle-assets.txt and datalogs.txt into the probe dir.
internal static class ProbeContent
{
    private const float LoadTimeout = 90f;
    private const float UnloadTimeout = 60f;
    private const float BundleTimeout = 120f;
    private const int SampleChars = 800;

    private static string _root;
    private static readonly List<Scene> Pending = [];
    // type -> (total count, scenes); MonoBehaviours only
    private static readonly SortedDictionary<string, (int Count, SortedSet<string> Where)> SceneComponents = new();
    private static readonly SortedDictionary<string, (int Count, SortedSet<string> Where)> SceneCatalogs = new();
    private static readonly SortedDictionary<string, (int Count, SortedSet<string> Where)> BundleAssets = new();
    private static readonly HashSet<string> Sampled = [];
    // datalog id -> where it is referenced
    private static readonly SortedDictionary<string, SortedSet<string>> LogRefs = new();

    public static IEnumerator Run(string root)
    {
        _root = root;
        yield return Scenes();
        yield return Bundles();
    }

    private static void Bump(SortedDictionary<string, (int Count, SortedSet<string> Where)> map, string type, int n, string where)
    {
        if (!map.TryGetValue(type, out var v)) v = (0, new SortedSet<string>());
        v.Where.Add(where);
        map[type] = (v.Count + n, v.Where);
    }

    private static void AddLogRef(string id, string where)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (!LogRefs.TryGetValue(id, out var s)) LogRefs[id] = s = new SortedSet<string>();
        s.Add(where);
    }

    private static string Safe(string name) => Regex.Replace(name, @"[^\w.-]+", "_");

    private static string Trunc(string s, int n) => s == null ? "" : s.Length <= n ? s : s.Substring(0, n) + $"… (+{s.Length - n} chars)";

    private static Dictionary<string, int> SoCounts() =>
        Resources.FindObjectsOfTypeAll<ScriptableObject>().Where(o => o != null)
            .GroupBy(o => o.GetType().FullName).ToDictionary(g => g.Key, g => g.Count());

    private static string TransformPath(Transform t)
    {
        var parts = new List<string>();
        for (; t != null; t = t.parent) parts.Add(t.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    // ---------------- scenes ----------------

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Awake/OnEnable already ran; deactivating before Start/Update keeps gameplay scenes inert.
        try { foreach (var go in scene.GetRootGameObjects()) go.SetActive(false); }
        catch (Exception e) { Plugin.Log.LogWarning($"[probe] deactivating {scene.name} failed: {e.Message}"); }
        Pending.Add(scene);
    }

    private static Scene DdolScene()
    {
        var go = new GameObject("probe-ddol");
        Object.DontDestroyOnLoad(go);
        var s = go.scene;
        Object.DestroyImmediate(go);
        return s;
    }

    private static IEnumerator Scenes()
    {
        Directory.CreateDirectory(Path.Combine(_root, "scenes"));
        var summary = new StringBuilder();
        summary.AppendLine("# Build scenes, loaded additively one at a time (roots deactivated in sceneLoaded), inventoried, unloaded.");
        summary.AppendLine("# idx  status  loadSec  roots  components  newSOTypes  path  [error]");
        var startupMb = new HashSet<string>(Resources.FindObjectsOfTypeAll<MonoBehaviour>().Where(m => m != null).Select(m => m.GetType().FullName));
        Application.backgroundLoadingPriority = ThreadPriority.High;
        SceneManager.sceneLoaded += OnSceneLoaded;

        var count = SceneManager.sceneCountInBuildSettings;
        Plugin.Log.LogInfo($"[probe] {count} build scenes");
        for (var i = 0; i < count; i++)
        {
            var path = SceneUtility.GetScenePathByBuildIndex(i);
            var name = Path.GetFileNameWithoutExtension(path);
            if (SceneManager.GetSceneByBuildIndex(i).isLoaded)
            {
                summary.AppendLine($"{i,3}  skipped(already-loaded)  {path}");
                continue;
            }

            string status = "ok", error = null;
            int roots = 0, comps = 0, newSo = 0;
            var t0 = Time.realtimeSinceStartup;
            Dictionary<string, int> soBefore = [];
            Scene ddol = default;
            var ddolBefore = new HashSet<GameObject>();
            try
            {
                soBefore = SoCounts();
                ddol = DdolScene();
                ddolBefore.UnionWith(ddol.GetRootGameObjects());
            }
            catch (Exception e) { error = $"baseline: {e.Message}"; }
            AsyncOperation op = null;
            try { op = SceneManager.LoadSceneAsync(i, LoadSceneMode.Additive); }
            catch (Exception e) { error = e.Message; }
            if (op == null) { status = "load-failed"; error ??= "LoadSceneAsync returned null"; }
            while (op != null && !op.isDone && Time.realtimeSinceStartup - t0 < LoadTimeout) yield return null;
            var loadSec = Time.realtimeSinceStartup - t0;
            if (op != null && !op.isDone) { status = "load-timeout"; error = $"not done after {LoadTimeout}s"; }

            var scene = SceneManager.GetSceneByBuildIndex(i);
            if (scene.IsValid() && scene.isLoaded)
            {
                try
                {
                    var newDdol = !ddol.IsValid() ? [] : ddol.GetRootGameObjects().Where(g => !ddolBefore.Contains(g)).ToArray();
                    foreach (var g in newDdol) g.SetActive(false);
                    (roots, comps, newSo) = InventoryScene(scene, soBefore, startupMb, newDdol, Pending.Where(s => s != scene).Select(s => s.name).ToArray());
                }
                catch (Exception e) { status = "inventory-failed"; error = e.ToString().Split('\n')[0]; }
            }
            else if (status == "ok") { status = "not-loaded"; error = "scene invalid/not loaded after op"; }

            // A timed-out load may still finish later; its sceneLoaded lands in Pending and is unloaded on a later pass.
            foreach (var s in Pending.ToArray())
            {
                if (!s.IsValid() || !s.isLoaded) { Pending.Remove(s); continue; }
                AsyncOperation uop = null;
                try { uop = SceneManager.UnloadSceneAsync(s); }
                catch (Exception e) { error = (error == null ? "" : error + "; ") + $"unload {s.name}: {e.Message}"; }
                var tu = Time.realtimeSinceStartup;
                while (uop != null && !uop.isDone && Time.realtimeSinceStartup - tu < UnloadTimeout) yield return null;
                if (uop != null && !uop.isDone) error = (error == null ? "" : error + "; ") + $"unload {s.name} timeout";
                else Pending.Remove(s);
            }
            var ua = Resources.UnloadUnusedAssets();
            var tua = Time.realtimeSinceStartup;
            while (!ua.isDone && Time.realtimeSinceStartup - tua < 30f) yield return null;

            summary.AppendLine($"{i,3}  {status}  {loadSec,6:F1}  {roots,5}  {comps,7}  {newSo,4}  {path}{(error == null ? "" : "  [" + error + "]")}");
            Plugin.Log.LogInfo($"[probe] scene {i} {name}: {status} {loadSec:F1}s {comps} components {error}");
            WriteSceneSummaries(summary);
        }
        SceneManager.sceneLoaded -= OnSceneLoaded;
        WriteSceneSummaries(summary);
    }

    private static void WriteSceneSummaries(StringBuilder summary)
    {
        try
        {
            File.WriteAllText(Path.Combine(_root, "scenes.txt"), summary.ToString());
            File.WriteAllText(Path.Combine(_root, "scene-components.txt"),
                "# MonoBehaviour types in build scenes: totalCount  type  scenes\n" +
                string.Concat(SceneComponents.OrderByDescending(x => x.Value.Count).Select(x => $"{x.Value.Count,6}  {x.Key}  {string.Join(",", x.Value.Where)}\n")));
            File.WriteAllText(Path.Combine(_root, "scene-catalogs.txt"),
                "# ScriptableObject types that became newly visible while a scene was loaded: addedCount  type  scenes\n" +
                string.Concat(SceneCatalogs.OrderByDescending(x => x.Value.Count).Select(x => $"{x.Value.Count,6}  {x.Key}  {string.Join(",", x.Value.Where)}\n")));
        }
        catch (Exception e) { Plugin.Log.LogWarning($"[probe] writing scene summaries failed: {e.Message}"); }
    }

    private static (int, int, int) InventoryScene(Scene scene, Dictionary<string, int> soBefore, HashSet<string> startupMb, GameObject[] newDdol, string[] alsoLoaded)
    {
        var sb = new StringBuilder();
        var rootGos = scene.GetRootGameObjects();
        sb.AppendLine($"# scene {scene.name} ({scene.path}) buildIndex={scene.buildIndex} roots={rootGos.Length}");
        if (alsoLoaded.Length > 0) sb.AppendLine($"# also loaded alongside: {string.Join(", ", alsoLoaded)}");
        if (newDdol.Length > 0) sb.AppendLine($"# new DontDestroyOnLoad roots (deactivated): {string.Join(", ", newDdol.Select(g => g.name))}");

        sb.AppendLine("\n## roots (name  childTransforms)");
        foreach (var g in rootGos.Take(300)) sb.AppendLine($"  {g.name}  {g.GetComponentsInChildren<Transform>(true).Length}");
        if (rootGos.Length > 300) sb.AppendLine($"  … {rootGos.Length - 300} more");

        var all = rootGos.SelectMany(g => g.GetComponentsInChildren<Component>(true)).ToArray();
        var missing = all.Count(c => c == null);
        var byType = all.Where(c => c != null).GroupBy(c => c.GetType()).OrderByDescending(g => g.Count()).ThenBy(g => g.Key.FullName).ToArray();
        sb.AppendLine($"\n## components (count  type  [*=not loaded at startup])  missing-scripts={missing}");
        foreach (var g in byType)
        {
            var isMb = typeof(MonoBehaviour).IsAssignableFrom(g.Key);
            var fresh = isMb && !startupMb.Contains(g.Key.FullName);
            sb.AppendLine($"{g.Count(),6}  {g.Key.FullName}{(fresh ? "  *" : "")}");
            if (isMb) Bump(SceneComponents, g.Key.FullName, g.Count(), scene.name);
        }

        var soAfter = SoCounts();
        var newSo = soAfter.Where(kv => kv.Value > soBefore.GetValueOrDefault(kv.Key)).OrderBy(kv => kv.Key).ToArray();
        sb.AppendLine("\n## ScriptableObjects newly visible (added  type  sampleNames)");
        foreach (var kv in newSo)
        {
            var added = kv.Value - soBefore.GetValueOrDefault(kv.Key);
            Bump(SceneCatalogs, kv.Key, added, scene.name);
            var names = Resources.FindObjectsOfTypeAll<ScriptableObject>().Where(o => o != null && o.GetType().FullName == kv.Key).Select(o => o.name).Take(8);
            sb.AppendLine($"{added,6}  {kv.Key}  {string.Join(", ", names)}");
        }

        var logs = all.OfType<TextLogInteractable>().ToArray();
        if (logs.Length > 0)
        {
            sb.AppendLine("\n## TextLogInteractable (logID  path)");
            foreach (var l in logs)
            {
                sb.AppendLine($"  {l.logID}  {TransformPath(l.transform)}");
                AddLogRef(l.logID, $"scene:{scene.name}");
            }
        }

        // One JsonUtility sample per game MonoBehaviour type that isn't loaded at startup, first scene only.
        sb.AppendLine("\n## samples (game types not loaded at startup; first occurrence across all scenes)");
        foreach (var g in byType)
        {
            var t = g.Key;
            if (!typeof(MonoBehaviour).IsAssignableFrom(t) || startupMb.Contains(t.FullName)) continue;
            if (!t.Assembly.GetName().Name.StartsWith("Assembly-CSharp") || !Sampled.Add(t.FullName)) continue;
            var c = g.First();
            string json;
            try { json = JsonUtility.ToJson(c); } catch (Exception e) { json = $"<json failed: {e.Message}>"; }
            sb.AppendLine($"### {t.FullName} @ {TransformPath(c.transform)}");
            sb.AppendLine(Trunc(json, SampleChars));
        }

        File.WriteAllText(Path.Combine(_root, "scenes", Safe(scene.name) + ".txt"), sb.ToString());
        return (rootGos.Length, all.Length, newSo.Length);
    }

    // ---------------- asset bundles ----------------

    private static string Describe(Object o)
    {
        switch (o)
        {
            case Texture2D t: return $"{t.width}x{t.height} {t.format} mips={t.mipmapCount}";
            case Texture2DArray ta: return $"{ta.width}x{ta.height}x{ta.depth} {ta.format}";
            case Texture t: return $"{t.width}x{t.height}";
            case Sprite s: return $"rect={s.rect} tex={(s.texture != null ? s.texture.name : "null")}";
            case Mesh m: return $"verts={m.vertexCount} submeshes={m.subMeshCount} bounds={m.bounds.size}";
            case TextAsset ta: return $"{ta.bytes.Length} bytes";
            case Material mat: return $"shader={(mat.shader != null ? mat.shader.name : "null")}";
            case AudioClip a: return $"{a.length:F1}s ch={a.channels} hz={a.frequency}";
            case GameObject go: return "components: " + string.Join(", ", go.GetComponentsInChildren<Component>(true).Where(c => c != null).GroupBy(c => c.GetType().Name).Select(g => $"{g.Key}×{g.Count()}"));
            case ScriptableObject so:
                try { return Trunc(JsonUtility.ToJson(so), 1500); } catch (Exception e) { return $"<json failed: {e.Message}>"; }
            default: return o.GetType().FullName;
        }
    }

    private static IEnumerator Bundles()
    {
        Directory.CreateDirectory(Path.Combine(_root, "bundles"));
        var summary = new StringBuilder();
        summary.AppendLine("# StreamingAssets asset bundles: name  status  fileMB  assetNames  loadedObjects  [error]");
        var dir = Path.GetDirectoryName(AssetBundlePaths.Get("x"));
        string[] files;
        try { files = Directory.GetFiles(dir).Where(f => Path.GetExtension(f) == "").OrderBy(f => f).ToArray(); }
        catch (Exception e) { files = []; summary.AppendLine($"# listing {dir} failed: {e.Message}"); }

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            string status = "ok", error = null;
            int nNames = 0, nObjs = 0;
            var t0 = Time.realtimeSinceStartup;
            AssetBundleCreateRequest req = null;
            try { req = AssetBundle.LoadFromFileAsync(file); } catch (Exception e) { error = e.Message; }
            while (req != null && !req.isDone && Time.realtimeSinceStartup - t0 < BundleTimeout) yield return null;
            var bundle = req != null && req.isDone ? req.assetBundle : null;
            if (bundle == null) { status = "open-failed"; error ??= req == null ? "no request" : req.isDone ? "assetBundle null" : "timeout"; }

            AssetBundleRequest all = null;
            string[] assetNames = [];
            if (bundle != null)
            {
                try
                {
                    assetNames = bundle.GetAllAssetNames();
                    nNames = assetNames.Length;
                    if (!bundle.isStreamedSceneAssetBundle) all = bundle.LoadAllAssetsAsync();
                }
                catch (Exception e) { status = "load-failed"; error = e.Message; }
            }
            var t1 = Time.realtimeSinceStartup;
            while (all != null && !all.isDone && Time.realtimeSinceStartup - t1 < BundleTimeout) yield return null;
            if (all != null && !all.isDone) { status = "load-timeout"; error = $"LoadAllAssets not done after {BundleTimeout}s"; }

            if (bundle != null)
            {
                try
                {
                    var objs = all != null && all.isDone ? all.allAssets : [];
                    nObjs = objs.Length;
                    InventoryBundle(name, file, bundle, assetNames, objs, Time.realtimeSinceStartup - t0);
                    if (name == "datalogs") InventoryDatalogs(bundle, objs);
                }
                catch (Exception e) { status = "inventory-failed"; error = e.ToString().Split('\n')[0]; }
                try { bundle.Unload(true); } catch (Exception e) { error = (error == null ? "" : error + "; ") + $"unload: {e.Message}"; }
            }

            long size = 0;
            try { size = new FileInfo(file).Length; } catch { }
            summary.AppendLine($"{name,-20}  {status}  {size / 1048576.0,7:F1}  {nNames,5}  {nObjs,6}{(error == null ? "" : "  [" + error + "]")}");
            Plugin.Log.LogInfo($"[probe] bundle {name}: {status} {nObjs} objects {error}");
            try
            {
                File.WriteAllText(Path.Combine(_root, "bundles.txt"), summary.ToString());
                File.WriteAllText(Path.Combine(_root, "bundle-assets.txt"),
                    "# Asset types loaded from StreamingAssets bundles: count  type  bundles\n" +
                    string.Concat(BundleAssets.OrderByDescending(x => x.Value.Count).Select(x => $"{x.Value.Count,6}  {x.Key}  {string.Join(",", x.Value.Where)}\n")));
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[probe] writing bundle summaries failed: {e.Message}"); }
        }
    }

    private static void InventoryBundle(string name, string file, AssetBundle bundle, string[] assetNames, Object[] objs, float sec)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# bundle {name} ({file}) streamedScenes={bundle.isStreamedSceneAssetBundle} loadSec={sec:F1}");
        if (bundle.isStreamedSceneAssetBundle) sb.AppendLine("## scene paths: " + string.Join(", ", bundle.GetAllScenePaths()));
        sb.AppendLine($"\n## asset names ({assetNames.Length})");
        foreach (var a in assetNames.OrderBy(x => x)) sb.AppendLine("  " + a);

        var byType = objs.Where(o => o != null).GroupBy(o => o.GetType()).OrderByDescending(g => g.Count()).ThenBy(g => g.Key.FullName).ToArray();
        sb.AppendLine($"\n## loaded objects by type ({objs.Length})");
        foreach (var g in byType)
        {
            sb.AppendLine($"{g.Count(),6}  {g.Key.FullName}");
            Bump(BundleAssets, g.Key.FullName, g.Count(), name);
        }

        sb.AppendLine("\n## samples (up to 5 per type)");
        foreach (var g in byType)
        {
            sb.AppendLine($"### {g.Key.FullName}");
            foreach (var o in g.Take(5))
            {
                string d;
                try { d = Describe(o); } catch (Exception e) { d = $"<describe failed: {e.Message}>"; }
                sb.AppendLine($"  {o.name}  {d}");
                if (o is TextAsset ta) sb.AppendLine(Trunc(ta.text, 3000));
            }
        }
        File.WriteAllText(Path.Combine(_root, "bundles", Safe(name) + ".txt"), sb.ToString());
    }

    // Image datalogs: ImageLogWindow loads Texture2D <id> from this bundle and titles it with LogDates.txt
    // ("id,date" lines). Text datalogs are TextBlocks entries (key=id, [0]=title, [1]=body) — already in `localization`.
    private static void InventoryDatalogs(AssetBundle bundle, Object[] objs)
    {
        foreach (var m in Resources.FindObjectsOfTypeAll<IncursionMission>())
            if (m.dataLogs != null)
                foreach (var l in m.dataLogs) AddLogRef(l.ID, $"IncursionMission:{m.name}(minFloor={l.MinFloor})");
        foreach (var e in Resources.FindObjectsOfTypeAll<GlobalEvent>())
        {
            AddLogRef(e.OnStartTextLog, $"GlobalEvent:{e.name}.OnStartTextLog");
            AddLogRef(e.OnEndTextLog, $"GlobalEvent:{e.name}.OnEndTextLog");
        }
        foreach (var l in Resources.FindObjectsOfTypeAll<TextLogInteractable>()) AddLogRef(l.logID, $"prefab:{l.gameObject.name}");

        var sb = new StringBuilder();
        var dates = new SortedDictionary<string, string>();
        var logDates = objs.OfType<TextAsset>().FirstOrDefault(t => t.name == "LogDates");
        if (logDates != null)
            foreach (var line in logDates.text.Split('\n'))
            {
                var i = line.IndexOf(',');
                if (i > 0) dates[line.Substring(0, i)] = line.Substring(i + 1).Trim();
            }
        var textures = objs.OfType<Texture2D>().ToDictionary(t => t.name, t => t);

        sb.AppendLine("# Datalogs. Image logs = Texture2D in the datalogs bundle (title 'datalog // <LogDates date>'); text logs = TextBlocks key.");
        sb.AppendLine($"\n## LogDates.txt ({dates.Count} entries: id  date)");
        foreach (var kv in dates) sb.AppendLine($"  {kv.Key}  {kv.Value}");

        sb.AppendLine($"\n## image textures ({textures.Count}: name  size  hasDate)");
        foreach (var kv in textures.OrderBy(x => x.Key)) sb.AppendLine($"  {kv.Key}  {kv.Value.width}x{kv.Value.height}  {dates.ContainsKey(kv.Key)}");

        sb.AppendLine($"\n## referenced log ids ({LogRefs.Count}: id  kind  refs  title | body-excerpt)");
        foreach (var kv in LogRefs)
        {
            var id = kv.Key;
            string kind, extra = "";
            if (TextBlocks.strings != null && TextBlocks.strings.ContainsKey(id))
            {
                kind = "text";
                string title = null, body = null;
                try { title = TextBlocks.GetString(id, 0); body = TextBlocks.GetString(id, 1); } catch { }
                extra = $"  {Trunc(title?.Replace('\n', ' '), 120)} | {Trunc(body?.Replace('\n', ' '), 160)}";
            }
            else
            {
                Object tex = null;
                try { tex = bundle.LoadAsset<Texture2D>(id); } catch { }
                kind = tex != null ? "image" : "missing";
                if (dates.TryGetValue(id, out var d)) extra = $"  date={d}";
            }
            sb.AppendLine($"  {id}  {kind}  [{string.Join("; ", kv.Value)}]{extra}");
        }

        var refd = new HashSet<string>(LogRefs.Keys.Select(k => k.Substring(k.LastIndexOf('/') + 1)));
        sb.AppendLine("\n## image textures not referenced by any scanned scene/prefab/mission/event");
        foreach (var n in textures.Keys.Where(n => !refd.Contains(n) && n != "LogDates").OrderBy(x => x)) sb.AppendLine("  " + n);

        File.WriteAllText(Path.Combine(_root, "datalogs.txt"), sb.ToString());
    }
}
