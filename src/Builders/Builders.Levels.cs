using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MycopunkDumper;

partial class Plugin
{
    // Per-scene data (LevelData, EnemyManager, collectables, datalog placements) for every build scene.
    // Hub is inventoried during its normal worldObjects load; the rest are swept one at a time after the
    // worldObjects scenes unload, with roots deactivated in sceneLoaded so no Start/Update ever runs.
    private static float _sceneBudgetStart;
    private static bool _sweeping;
    private static readonly List<Scene> SweepPending = [];
    private static readonly SortedDictionary<string, Level> LevelMap = new();
    private static readonly SortedDictionary<string, SortedDictionary<string, object>> SceneEnemyManagers = new();
    private static readonly SortedDictionary<string, List<DataLogSource>> DataLogSources = new();

    private static float SceneBudgetLeft => SceneLoadTimeout - (Time.realtimeSinceStartup - _sceneBudgetStart);

    private static void HookSceneInventory()
    {
        _sceneBudgetStart = Time.realtimeSinceStartup;
        LevelMap.Clear();
        SceneEnemyManagers.Clear();
        DataLogSources.Clear();
        SweepPending.Clear();
        // Before any scene load, so only prefab assets match.
        foreach (var l in Resources.FindObjectsOfTypeAll<TextLogInteractable>())
            if (l != null && !l.gameObject.scene.IsValid())
                AddLogSource(l.logID, new DataLogSource { Kind = "prefab", Prefab = l.transform.root.name });
        SceneManager.sceneLoaded += OnInventorySceneLoaded;
    }

    private static void AddLogSource(string id, DataLogSource src)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (!DataLogSources.TryGetValue(id, out var list)) DataLogSources[id] = list = [];
        list.Add(src);
    }

    private static void OnInventorySceneLoaded(Scene scene, LoadSceneMode mode)
    {
        try
        {
            if (_sweeping || DumpScenes.All(d => d.Scene != scene.name))
            {
                foreach (var go in scene.GetRootGameObjects()) go.SetActive(false);
                SweepPending.Add(scene);
            }
            // Now, not after the load op: Awake-time Destroy() (e.g. PunchCollectables this save already collected) is still pending.
            InventoryScene(scene);
        }
        catch (Exception ex) { Log.LogWarning($"Inventory of scene {scene.name} failed: {ex}"); }
    }

    private static void InventoryScene(Scene scene)
    {
        var comps = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MonoBehaviour>(true)).Where(c => c != null).ToArray();
        var logs = comps.OfType<TextLogInteractable>().Where(l => !string.IsNullOrEmpty(l.logID)).GroupBy(l => l.logID).OrderBy(g => g.Key, StringComparer.Ordinal).ToArray();
        foreach (var list in DataLogSources.Values) list.RemoveAll(s => s.Kind == "scene" && s.Scene == scene.name);   // re-swept scene
        foreach (var g in logs) AddLogSource(g.Key, new DataLogSource { Kind = "scene", Scene = scene.name, Count = g.Count() });

        var em = comps.OfType<EnemyManager>().FirstOrDefault();
        if (em != null) SceneEnemyManagers[scene.name] = EnemyManagerFields(em);

        var ld = comps.OfType<LevelData>().FirstOrDefault();
        if (ld == null) return;
        LevelMap[scene.name] = new Level
        {
            BuildIndex = scene.buildIndex,
            Path = scene.path,
            LocationName = ld.LocationName,
            Region = ld.Region == null ? null : Array.IndexOf(Global.Instance.Regions, ld.Region) >= 0 ? ld.Region.ID.ToString() : ld.Region.name,
            LevelFlags = ld.LevelFlags.ToString(),
            MaxRailgunAngle = ld.MaxRailgunAngle,
            Collectables = new SortedDictionary<string, int>(comps.OfType<PunchCollectable>().Where(p => p.profile != null)
                .GroupBy(p => p.profile.APIName).ToDictionary(g => g.Key, g => g.Count()), StringComparer.Ordinal),
            DataLogs = logs.Select(g => g.Key).ToArray(),
        };
    }

    private static SortedDictionary<string, object> EnemyManagerFields(EnemyManager em)
    {
        var f = PartFields(em, null);
        f["enemyClassGroups"] = WeightedValues(em.enemyClassGroups, g => g != null ? g.name : null);
        f["customWaves"] = WeightedValues(em.customWaves, w => w != null ? w.name : null);
        f["GruntClasses"] = em.GruntClasses != null ? em.GruntClasses.name : null;
        f["bossClass"] = EnemyKey(em.bossClass);
        f["enemyTypeLimits"] = em.enemyTypeLimits;
        f["enemyIntensitySettings"] = em.enemyIntensitySettings?.Select(w => PartFields(w, null)).ToArray();
        f["enemyIntensityDecayCooldownMultiplier"] = ThreatValues(em.enemyIntensityDecayCooldownMultiplier);
        f["globalEnemyIntensityIncreaseSpeedMultiplier"] = ThreatValues(em.globalEnemyIntensityIncreaseSpeedMultiplier);
        // Scene-only SOs never reach the startup customWaves/enemyGroups catalogs.
        var waves = em.customWaves?.items?.Select(n => n?.value).Where(w => w != null && !CustomWaveMap.ContainsKey(w.name)).ToArray() ?? [];
        if (waves.Length > 0) f["sceneCustomWaves"] = new SortedDictionary<string, CustomWaveEntry>(waves.GroupBy(w => w.name).ToDictionary(g => g.Key, g => BuildCustomWave(g.First())), StringComparer.Ordinal);
        var groups = em.enemyClassGroups?.items?.Select(n => n?.value).Where(g => g != null && !EnemyGroupMap.ContainsKey(g.name)).ToArray() ?? [];
        if (groups.Length > 0) f["sceneEnemyGroups"] = new SortedDictionary<string, EnemyGroup>(groups.GroupBy(g => g.name).ToDictionary(g => g.Key, g => BuildEnemyGroup(g.First())), StringComparer.Ordinal);
        foreach (var k in f.Where(kv => kv.Value == null).Select(kv => kv.Key).ToArray()) f.Remove(k);
        return f;
    }

    private static Scene DdolScene()
    {
        var go = new GameObject("dumper-ddol");
        DontDestroyOnLoad(go);
        var s = go.scene;
        DestroyImmediate(go);
        return s;
    }

    private static IEnumerator SweepBuildScenes()
    {
        _sweeping = true;
        var ddol = DdolScene();
        var ddolBefore = new HashSet<GameObject>(ddol.GetRootGameObjects());
        var t0 = Time.realtimeSinceStartup;
        for (var i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            var name = Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(i));
            // RhomProc's LevelData doesn't survive its active worldObjects load, so it gets swept again here.
            if (SceneManager.GetSceneByBuildIndex(i).isLoaded || LevelMap.ContainsKey(name)) continue;
            if (SceneBudgetLeft <= 0)
            {
                Log.LogWarning($"Scene budget exhausted; skipping build scene {i} {name} and later");
                break;
            }
            AsyncOperation op = null;
            try { op = SceneManager.LoadSceneAsync(i, LoadSceneMode.Additive); }
            catch (Exception ex) { Log.LogWarning($"Loading scene {name} failed: {ex.Message}"); }
            while (op != null && !op.isDone && SceneBudgetLeft > 0) yield return null;
            if (op != null && !op.isDone) Log.LogWarning($"Scene {name}: load NOT done within budget");

            foreach (var s in SweepPending.ToArray())
            {
                if (!s.IsValid() || !s.isLoaded) { SweepPending.Remove(s); continue; }
                AsyncOperation uop = null;
                try { uop = SceneManager.UnloadSceneAsync(s); }
                catch (Exception ex) { Log.LogWarning($"Unloading scene {s.name} failed: {ex.Message}"); }
                while (uop != null && !uop.isDone && SceneBudgetLeft > 0) yield return null;
                if (uop == null || uop.isDone) SweepPending.Remove(s);
            }
        }
        SceneManager.sceneLoaded -= OnInventorySceneLoaded;
        _sweeping = false;
        // Scene Awakes can park objects in DontDestroyOnLoad (StatusEffectManager, OutlineModifier).
        foreach (var go in ddol.GetRootGameObjects().Where(g => !ddolBefore.Contains(g))) go.SetActive(false);
        var ua = Resources.UnloadUnusedAssets();
        while (!ua.isDone && SceneBudgetLeft > 0) yield return null;
        Log.LogInfo($"Build-scene sweep: {LevelMap.Count} levels in {Time.realtimeSinceStartup - t0:F1}s ({SceneBudgetLeft:F0}s budget left)");
    }

    private static EnemyManagerEntry BuildEnemyManagers()
    {
        static string Json(object o) => JsonConvert.SerializeObject(o);
        var common = SceneEnemyManagers.GroupBy(kv => Json(kv.Value)).OrderByDescending(g => g.Count()).ThenBy(g => g.First().Key, StringComparer.Ordinal).FirstOrDefault();
        var entry = new EnemyManagerEntry { Default = common?.First().Value ?? new(), Scenes = new(StringComparer.Ordinal) };
        foreach (var (scene, fields) in SceneEnemyManagers)
        {
            var diff = new SortedDictionary<string, object>(StringComparer.Ordinal);
            foreach (var k in fields.Keys.Union(entry.Default.Keys))
            {
                fields.TryGetValue(k, out var v);
                entry.Default.TryGetValue(k, out var dv);
                if (Json(v) != Json(dv)) diff[k] = v;
            }
            entry.Scenes[scene] = diff;
        }
        return entry;
    }

    private static SortedDictionary<string, DataLog> BuildDataLogs()
    {
        foreach (var m in Resources.FindObjectsOfTypeAll<IncursionMission>())
            if (m != null && m.dataLogs != null)
                foreach (var l in m.dataLogs) AddLogSource(l.ID, new DataLogSource { Kind = "incursion", Mission = m.ID, MinFloor = l.MinFloor });
        foreach (var ge in Resources.FindObjectsOfTypeAll<global::GlobalEvent>().Where(e => e != null).OrderBy(e => e.name, StringComparer.Ordinal))
        {
            var id = GetPrivateField<string>(ge, "id") ?? ge.name;
            AddLogSource(ge.OnStartTextLog, new DataLogSource { Kind = "globalEvent", GlobalEvent = id, Trigger = "start" });
            AddLogSource(ge.OnEndTextLog, new DataLogSource { Kind = "globalEvent", GlobalEvent = id, Trigger = "end" });
        }

        // Same bundle + LogDates parsing as ImageLogWindow.Setup; reuse its handle if a window already opened it.
        var bundle = ImageLogWindow.bundle;
        var ownBundle = bundle == null;
        if (ownBundle) bundle = AssetBundle.LoadFromFile(AssetBundlePaths.Get("datalogs"));
        var dates = new Dictionary<string, string>();
        var textures = new Dictionary<string, Texture2D>();
        if (bundle == null) Log.LogWarning("datalogs bundle failed to open; image logs have no texture info");
        else
        {
            var logDates = bundle.LoadAsset<TextAsset>("LogDates");
            if (logDates != null)
                foreach (var line in logDates.text.Split('\n'))
                {
                    var c = line.IndexOf(',');
                    if (c > 0) dates[line.Substring(0, c)] = line.Substring(c + 1).Trim();
                }
            foreach (var t in bundle.LoadAllAssets<Texture2D>()) textures[t.name] = t;
        }

        var ids = new SortedSet<string>(DataLogSources.Keys, StringComparer.Ordinal);
        // Textures no known source references still show up in a codex that has them discovered.
        foreach (var name in textures.Keys.Where(n => ids.All(id => id.Substring(id.LastIndexOf('/') + 1) != n))) ids.Add(name);

        var map = new SortedDictionary<string, DataLog>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            var slash = id.LastIndexOf('/');
            var name = id.Substring(slash + 1);
            var log = new DataLog { Sources = DataLogSources.TryGetValue(id, out var src) ? src : [] };
            if (slash >= 0)
            {
                log.Folder = id.Substring(0, slash);
                log.FolderLabel = TextBlocks.TryGetString(log.Folder, 0, out var fl) ? fl : null;
            }
            if (TextBlocks.strings.ContainsKey(id))
            {
                log.Kind = "text";
                log.Title = TextBlocks.TryGetString(id, 0, out var title) ? title : null;
                log.Body = TextBlocks.TryGetString(id, 1, out var body) ? body : null;
                log.CodexLabel = log.Title;
            }
            else
            {
                log.Kind = "image";
                log.Date = dates.TryGetValue(id, out var d) ? d : null;
                var datalog = TextBlocks.GetString("datalog");
                log.Title = log.Date != null ? datalog + " // " + log.Date : datalog;
                // CodexWindow fakes a file extension for image entries.
                log.CodexLabel = slash >= 0 ? name + ".pdf" : id + new Pigeon.Math.Random(id.GetHashCode()).Next(3) switch { 0 => ".jpg", 1 => ".png", _ => ".pdf" };
                if (textures.TryGetValue(name, out var tex))
                    log.Image = new DataLog.DataLogImage
                    {
                        Asset = tex.name,
                        Width = tex.width,
                        Height = tex.height,
                        Format = tex.format.ToString(),
                        Readable = tex.isReadable,
                    };
            }
            map[id] = log;
        }
        if (ownBundle && bundle != null) bundle.Unload(true);
        return map;
    }

    private static IEnumerator DumpSceneSections(JToken root, JsonSerializerSettings settings)
    {
        yield return UnloadDumpScenes();
        yield return SweepBuildScenes();
        var ser = JsonSerializer.Create(settings);
        try
        {
            var managers = BuildEnemyManagers();
            root["levels"] = JToken.FromObject(LevelMap, ser);
            root["enemyManager"] = JToken.FromObject(managers, ser);
        }
        catch (Exception ex) { Log.LogError($"Levels / enemy managers failed: {ex}"); }
        var t = Time.realtimeSinceStartup;
        try { root["dataLogs"] = JToken.FromObject(BuildDataLogs(), ser); }
        catch (Exception ex) { Log.LogError($"Data logs failed: {ex}"); }
        Log.LogInfo($"Data logs in {Time.realtimeSinceStartup - t:F1}s");
    }
}
