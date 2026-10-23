using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MycopunkDumper;

partial class Plugin
{
    // Hub interactables and the Rhombomination boss only exist in scenes, not in loaded assets at Start().
    // Loaded additively (no networking) after the main dump is serialized, so scene Awake side effects can't leak into it.
    private const float SceneLoadTimeout = 120f; // shared across scenes; dump tasks wait up to 5min for data.json

    private static readonly (string Scene, string Category, Type[] Types)[] DumpScenes =
    [
        ("RhomProc", "boss", [typeof(Rhombomination)]),
        ("Hub", "hub", [typeof(SpeedBattery), typeof(ExplosiveRigidbody), typeof(CableCar), typeof(Ropevator), typeof(CargoElevator),
            typeof(MovingPiston), typeof(NetworkSpinningFan), typeof(HubCannon), typeof(SkillPointCrafter)]),
    ];

    private static IEnumerator LoadDumpScenes()
    {
        Application.backgroundLoadingPriority = ThreadPriority.High;
        var start = Time.realtimeSinceStartup;
        foreach (var (scene, _, _) in DumpScenes)
        {
            AsyncOperation op = null;
            try { op = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Additive); }
            catch (Exception ex) { Log.LogWarning($"Loading scene {scene} failed: {ex.Message}"); }
            var t = Time.realtimeSinceStartup;
            while (op != null && !op.isDone && Time.realtimeSinceStartup - start < SceneLoadTimeout) yield return null;
            Log.LogInfo($"Scene {scene}: load {(op?.isDone == true ? "done" : "NOT done")} in {Time.realtimeSinceStartup - t:F1}s");
        }
    }

    // Hub objects NRE every frame outside a real hub session (SpinningFan, EnemyManager, …) —
    // harmless for a dump that quits right away, but it floods the log for anything that keeps running.
    private static IEnumerator UnloadDumpScenes()
    {
        // Unloading spans several frames; deactivate every scene up front so none keeps ticking meanwhile.
        foreach (var (scene, _, _) in DumpScenes)
        {
            var s = SceneManager.GetSceneByName(scene);
            if (s.isLoaded) foreach (var go in s.GetRootGameObjects()) go.SetActive(false);
        }
        foreach (var (scene, _, _) in DumpScenes)
        {
            if (!SceneManager.GetSceneByName(scene).isLoaded) continue;
            var op = SceneManager.UnloadSceneAsync(scene);
            var t = Time.realtimeSinceStartup;
            while (op != null && !op.isDone && Time.realtimeSinceStartup - t < SceneLoadTimeout) yield return null;
        }
    }

    private static void AddSceneObjects(SortedDictionary<string, WorldObject> map)
    {
        void Add(string category, string source, GameObject go, Func<MonoBehaviour, bool> keep, Action<MonoBehaviour, SortedDictionary<string, object>> extra = null)
        {
            if (go == null) return;
            var key = DuplicateSuffix.Replace(go.name, "");
            if (map.ContainsKey(key)) return;
            map[key] = new WorldObject
            {
                Category = category,
                Source = source,
                Components = go.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c != null && keep(c)).Select(c =>
                {
                    var f = PartFields(c, go);
                    extra?.Invoke(c, f);
                    return new EnemyPartComponent { Path = TransformPath(c.transform, go.transform), Subclass = c.GetType().Name, Fields = f };
                }).ToArray()
            };
        }

        bool Gameplay(MonoBehaviour c) => c is ObjectTarget or SpikeProjectile or EyeBullet or ExplosiveRigidbody or LaserBullet or DamageOnHit;

        void RhomExtra(MonoBehaviour c, SortedDictionary<string, object> f)
        {
            if (c is not Rhombomination r) return;
            f["playerHealthMultiplier"] = PlayerValues(r.playerHealthMultiplier);
            f["threatHealthMultiplier"] = ThreatValues(r.threatHealthMultiplier);
            foreach (var (name, attack) in new (string, object)[] { ("fastSpikeAttack", r.fastSpikeAttack), ("explosiveSpewAttack", r.explosiveSpewAttack), ("lasersAttack", r.lasersAttack), ("suicideDroneAttack", r.suicideDroneAttack), ("iceWaveAttack", r.iceWaveAttack) })
            {
                if (attack == null) continue;
                var af = PartFields(attack, null);
                af["Subclass"] = attack.GetType().Name;
                foreach (var fi in attack.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                {
                    var go = fi.GetValue(attack) switch { GameObject g => g, Component cc => cc.gameObject, _ => null };
                    if (go == null) continue;
                    af[fi.Name] = DuplicateSuffix.Replace(go.name, "");
                    if (go.GetComponentsInChildren<MonoBehaviour>(true).Any(Gameplay)) Add("boss", $"Rhombomination.{name}.{fi.Name}", go, Gameplay);
                }
                f[name] = af;
            }
        }

        void CraftExtra(MonoBehaviour c, SortedDictionary<string, object> f)
        {
            if (c is CraftingInteractable ci && ci.cost != null)
                f["cost"] = ci.cost.Where(x => x.resource != null).Select(x => new Dictionary<string, object> { ["resource"] = x.resource.ID, ["count"] = x.count }).ToArray();
            RhomExtra(c, f);
        }

        foreach (var (sceneName, category, types) in DumpScenes)
        {
            var scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Log.LogWarning($"Scene {sceneName} not loaded; skipping its world objects");
                continue;
            }
            var found = 0;
            foreach (var rootGo in scene.GetRootGameObjects())
            foreach (var c in rootGo.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (c == null || !types.Contains(c.GetType())) continue;
                found++;
                Add(category, $"scene:{sceneName}", c.gameObject, x => types.Contains(x.GetType()), CraftExtra);
            }
            Log.LogInfo($"Scene {sceneName}: {found} matching components");
        }
    }
}
