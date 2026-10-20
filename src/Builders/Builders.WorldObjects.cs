using System.Linq;
using UnityEngine;
using System.Collections.Generic;

namespace MycopunkDumper;

partial class Plugin
{
    private static readonly System.Text.RegularExpressions.Regex DuplicateSuffix = new(@" \(\d+\)$");

    private static bool IsWorldComponent(MonoBehaviour c) =>
        c is ObjectTarget or ClientCollectable or LaserBullet or RocketSalvoBullet or VolatileExplosive or DamageOnHit
            or OverclockedExplosion or AntigravityField or LaunchPad or SafetyBubble or GrapplePole;

    private static SortedDictionary<string, WorldObject> BuildWorldObjects()
    {
        var map = new SortedDictionary<string, WorldObject>();

        void Add(string category, string source, Object o)
        {
            var go = o switch { GameObject g => g, Component c => c.gameObject, _ => null };
            if (go == null) return;
            var key = DuplicateSuffix.Replace(go.name, "");   // "LaunchPad (1)" — copies inside one prefab
            if (map.ContainsKey(key)) return;
            map[key] = new WorldObject
            {
                Category = category,
                Source = source,
                Components = go.GetComponentsInChildren<MonoBehaviour>(true).Where(IsWorldComponent).Select(c => new EnemyPartComponent
                {
                    Path = TransformPath(c.transform, go.transform),
                    Subclass = c.GetType().Name,
                    Fields = PartFields(c, go)
                }).ToArray()
            };
        }

        // Prefabs not reachable from Global; scene instances skipped.
        void AddAll<T>(string category) where T : MonoBehaviour
        {
            foreach (var c in Resources.FindObjectsOfTypeAll<T>())
                if (c != null && !c.gameObject.scene.IsValid()) Add(category, typeof(T).Name, c);
        }

        var g = Global.Instance;
        Add("hazard", "Global.HornetPrefab", g.HornetPrefab);
        Add("hazard", "Global.HardonTurret", g.HardonTurret);
        Add("hazard", "Global.MortarBullet", g.MortarBullet);
        for (var i = 0; i < (g.laserBullets?.Length ?? 0); i++) Add("hazard", $"Global.laserBullets[{i}]", g.laserBullets[i]);
        foreach (var n in g.Explodunguses ?? []) Add("hazard", "Global.Explodunguses", n);
        AddAll<ExplodingTarget>("hazard");
        AddAll<VolatileExplosive>("hazard");
        AddAll<DamageOnHit>("hazard");
        foreach (var d in Resources.FindObjectsOfTypeAll<AcidFlyTrapData>())
        {
            if (d == null || map.ContainsKey(d.name)) continue;
            map[d.name] = new WorldObject
            {
                Category = "hazard",
                Source = nameof(AcidFlyTrapData),
                Components = [new EnemyPartComponent { Path = "", Subclass = nameof(AcidFlyTrapData), Fields = PartFields(d, null) }]
            };
            var f = map[d.name].Components[0].Fields;
            f["BulletScale"] = (d.BulletData ?? []).Select(b => b.Scale).ToArray();
            f["BulletExplosionSize"] = (d.BulletData ?? []).Select(b => b.ExplosionSize).ToArray();
        }

        Add("interactable", "Global.HealCollectableLargePrefab", g.HealCollectableLargePrefab);
        Add("interactable", "Global.HealCollectableSmallPrefab", g.HealCollectableSmallPrefab);
        Add("interactable", "Global.GenericResourceCollectable", g.GenericResourceCollectable);
        Add("interactable", "Global.TossResourceCollectable", g.TossResourceCollectable);
        Add("interactable", "Global.OverclockedExplosion", g.OverclockedExplosion);
        Add("interactable", "Global.AntigravityField", g.AntigravityField);
        Add("interactable", "ScrapperGear.grapplePolePrefab", g.AllGear?.OfType<ScrapperGear>().FirstOrDefault()?.GrapplePolePrefab);
        AddAll<AmmoCollectable>("interactable");
        AddAll<LaunchPad>("interactable");
        AddAll<SafetyBubble>("interactable");
        return map;
    }
}
