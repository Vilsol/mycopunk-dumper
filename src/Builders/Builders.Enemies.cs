using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using Newtonsoft.Json;

namespace MycopunkDumper;

partial class Plugin
{
    private static Enemy BuildEnemy(EnemyClass e)
    {
        var en = new Enemy
        {
            ID = e.ID.ToString(),
            Name = e.Name,
            APIName = e.APIName,
            InternalName = GetPrivateField<string>(e, "_name"),
            Tags = e.tags.ToString(),
            LevelFlags = e.levelFlags.ToString(),
            OverrideUpgradeDropChance = e.overrideUpgradeDropChance,
            OverclockChance = e.overclockChance,
            ShellHealthMultiplier = e.shellHealthMultiplier,
            CanBeDespawned = e.canBeDespawned,
            MinLegs = e.minLegs,
            MaxLegs = e.maxLegs,
            ArmChance = e.armChance,
            LegChance = e.legChance,
            EnemyLimitSlots = e.EnemyLimitSlots,
            LegHealthMultiplier = e.legHealthMultiplier,
            DontSpawnShellChildren = e.dontSpawnShellChildren,
            ArmCount = new PlayerScaled<MinMax<int>>
            {
                Values = e.armCount.values?.Select(v => new MinMax<int> { Min = v.x, Max = v.y }).ToArray(),
                PerExtraPlayer = e.armCount.multiplier
            },
            Cores = CatalogParts(e.cores),
            Legs = CatalogParts(e.legs),
            Arms = CatalogParts(e.arms),
            AttachmentGroups = e.attachmentGroups?.Select(pl => { CatalogParts(pl.parts); return BuildPartList(pl); }).ToArray(),
            SpawnWithEnemy = e.SpawnWithEnemy?.Where(g => g != null).Select(g => { CatalogPart(g); return g.name; }).ToArray()
        };

        var c = e.config;
        en.Config = new EnemyConfig
        {
            EnemyType = c.enemyType.ToString(),
            OverrideEnemySlots = c.overrideEnemySlots,
            AgentSize = c.agentSize,
            NavRadius = c.navRadius,
            DontRandRotateCore = c.dontRandRotateCore,
            MaxFlankTime = c.maxFlankTime,
            MinFlankDistance = c.minFlankDistance,
            TryFlankTime = c.tryFlankTime,
            FlankChance = c.flankChance,
            MoveSpeed = c.moveSpeed,
            RigidbodySpeedMultiplier = c.rigidbodySpeedMultiplier,
            TurnSpeed = c.turnSpeed,
            OverrideAcceleration = c.overrideAcceleration,
            OverrideStoppingDistance = c.overrideStoppingDistance,
            AllowRigidbodyMovement = c.allowRigidbodyMovement,
            BalanceDurationBeforeDisablingRigidbodyMovement = c.balanceDurationBeforeDisablingRigidbodyMovement,
            MinLegsToBalance = c.minLegsToBalance,
            RagdollForceThreshold = c.ragdollForceThreshold,
            HitStunChance = c.hitStunChance,
            AddedHeight = c.addedHeight,
            DisableNavigation = c.disableNavigation,
            NavPriority = c.navPriority,
            DontDespawnWhenUnableToPath = c.DontDespawnWhenUnableToPath,
            DontLowerTowardTargetWhenFlying = c.dontLowerTowardTargetWhenFlying,
            OverrideThrusterHoverHeight = c.overrideThrusterHoverHeight,
            CanAttackMultipleTargets = c.canAttackMultipleTargets,
            MaxConcurrentMeleeAttacks = c.maxConcurrentMeleeAttacks,
            MeleeInterval = c.meleeInterval,
            SwitchTargetInterval = c.switchTargetInterval,
            SwitchTargetChance = c.switchTargetChance,
            LookForPartDistance = c.lookForPartDistance,
            LookForPartInterval = c.lookForPartInterval,
            RegrowLimbCooldown = c.regrowLimbCooldown,
            RegrowLimbDuration = c.regrowLimbDuration,
            OnlyUseCoreHealthForHealthbar = c.onlyUseCoreHealthForHealthbar
        };

        // CustomLoot: extra drops fired when this enemy is killed (LevelUnlockList)
        var customLoot = e.CustomLoot.Properties;
        if (customLoot != null) en.CustomLoot = customLoot.Where(x => x != null).Select(BuildLevelUnlock).ToArray();

        return en;
    }

    // Enemy part prefabs: generic reflection over serialized gameplay fields of EnemyPart/EnemyIKLeg/Weakpoint
    // components (Unity-serialized = public or [SerializeField]), plus consts (e.g. EnemyCore overclock explosion).
    private static readonly System.Text.RegularExpressions.Regex PartFieldSkip =
        new("(?i)sound|muzzle|shake|flare|deathEffect|bob");

    private static WeightedEntry[] CatalogParts(Pigeon.WeightedArray<UnityEngine.GameObject> wa)
    {
        foreach (var n in wa?.items ?? []) CatalogPart(n?.value);
        return WeightedValues(wa, g => g?.name);
    }

    private static void CatalogPart(UnityEngine.GameObject go)
    {
        if (go == null) return;
        if (EnemyPartMap.ContainsKey(go.name)) return;
        var entry = new EnemyPartPrefab();
        EnemyPartMap[go.name] = entry;   // reserve before recursing into referenced prefabs
        if (go.GetComponent<IArmTip>() != null) entry.MountedOn = Global.Instance.GunArmLarge?.gameObject.name;
        var comps = new List<EnemyPartComponent>();
        foreach (var c in go.GetComponentsInChildren<UnityEngine.MonoBehaviour>(true))
        {
            if (c is not (EnemyPart or EnemyIKLeg or Weakpoint)) continue;
            comps.Add(new EnemyPartComponent
            {
                Path = TransformPath(c.transform, go.transform),
                Subclass = c.GetType().Name,
                Fields = PartFields(c, go)
            });
        }
        entry.Components = comps.ToArray();
    }

    private static string TransformPath(UnityEngine.Transform t, UnityEngine.Transform root)
    {
        var parts = new List<string>();
        for (; t != null && t != root; t = t.parent) parts.Add(t.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static SortedDictionary<string, object> PartFields(UnityEngine.Object c, UnityEngine.GameObject root)
    {
        var fields = new SortedDictionary<string, object>();
        const BindingFlags bf = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (var t = c.GetType(); t != null && t.Assembly == typeof(EnemyPart).Assembly; t = t.BaseType)
        {
            foreach (var f in t.GetFields(bf))
            {
                var name = f.Name.StartsWith("<") ? f.Name.Substring(1, f.Name.IndexOf('>') - 1) : f.Name;   // [field: SerializeField] auto-props
                if (fields.ContainsKey(name) || PartFieldSkip.IsMatch(name)) continue;
                object v;
                if (f.IsLiteral) v = f.GetRawConstantValue();
                else if (f.IsStatic || f.IsInitOnly || f.IsNotSerialized) continue;
                else if (!f.IsPublic && f.GetCustomAttribute<UnityEngine.SerializeField>() == null) continue;
                else v = f.GetValue(c);
                v = v switch
                {
                    float or int or bool => v,
                    Enum en => en.ToString(),
                    DamageData d => DamageValues(d),
                    Range<float> r => RangeValues(r),
                    Range<int> r => RangeValues(r),
                    UnityEngine.Vector2 v2 => new[] { v2.x, v2.y },
                    UnityEngine.Vector2Int v2i => new[] { v2i.x, v2i.y },
                    // Prefab refs (leg segments, arm tips) — separate prefabs only, not children of this one.
                    UnityEngine.GameObject or EnemyPart or EnemyIKLeg => PartRef((UnityEngine.Object)v, root),
                    ChildReference[] { Length: > 0 } cr => cr.Select(x => PartRef(x.gameObject, root)).Where(x => x != null).ToArray(),
                    PlayerStackData ps => GetPrivateField<string>(ps, "_name") ?? ps.name,   // `stacks` key
                    PlayerResource pr => pr.ID,                                              // `resources` key
                    _ => null
                };
                if (v != null) fields[name] = v;
            }
        }
        return fields;
    }

    private static string PartRef(UnityEngine.Object o, UnityEngine.GameObject root)
    {
        var go = o switch { UnityEngine.GameObject g => g, UnityEngine.Component cc => cc.gameObject, _ => null };
        if (go == null || (root != null && go.transform.root == root.transform)) return null;
        var prefab = go.transform.root.gameObject;
        if (prefab.GetComponentInChildren<EnemyPart>(true) == null && prefab.GetComponentInChildren<EnemyIKLeg>(true) == null) return null;
        CatalogPart(prefab);
        return prefab.name;
    }

    // Per-effect tuning — DamageMultiplier and FullSaturationLifetime are hardcoded in C# subclasses.
    // Keyed by effectID. DamageMultiplier: 1.0 default, 1.2 for Decay, 1.35 for Rot.
    // FullSaturationLifetime: 3s default, overrides per-effect.

    private static CustomWaveEntry BuildCustomWave(CustomWave cw)
    {
        var entry = new CustomWaveEntry
        {
            Name = cw.name,
            Subclass = cw.GetType().Name,
            AddAttachments = cw.AddAttachments,
            OverrideIndividualEnemies = cw.OverrideIndividualEnemies,
        };

        // GenericCustomWave-specific fields are private — pull via reflection.
        entry.Tags = (GetPrivateField<object>(cw, "tags") ?? cw.OverrideTags()).ToString();
        entry.ExcludeTags = (GetPrivateField<object>(cw, "excludeTags") ?? cw.ExcludeTags()).ToString();
        entry.Types = (GetPrivateField<object>(cw, "types") ?? cw.OverrideEnemyTypes()).ToString();
        entry.EnemyCountMultiplier = GetPrivateField<float>(cw, "enemyCountMultiplier");
        entry.OverclockChanceMultiplier = GetPrivateField<float>(cw, "overclockChanceMultiplier");
        entry.MinRoomSize = (GetPrivateField<object>(cw, "minRoomSize") ?? cw.MinRoomSize()).ToString();
        entry.MinOuroRooms = GetPrivateField<int>(cw, "minOuroRooms");

        // Tag/type chance arrays — each item is FlagMultiplier<T> { flag, multiplier }
        var tagChances = GetPrivateField<System.Array>(cw, "tagChances");
        if (tagChances != null)
        {
            var list = new List<CustomWaveEntry.TagWeight>();
            foreach (var item in tagChances)
            {
                if (item == null) continue;
                list.Add(new CustomWaveEntry.TagWeight
                {
                    Tag = GetPrivateField<object>(item, "flag")?.ToString(),
                    Multiplier = GetPrivateField<float>(item, "multiplier")
                });
            }
            entry.TagChances = list.ToArray();
        }
        var typeChances = GetPrivateField<System.Array>(cw, "typeChances");
        if (typeChances != null)
        {
            var list = new List<CustomWaveEntry.TypeWeight>();
            foreach (var item in typeChances)
            {
                if (item == null) continue;
                list.Add(new CustomWaveEntry.TypeWeight
                {
                    Type = GetPrivateField<object>(item, "flag")?.ToString(),
                    Multiplier = GetPrivateField<float>(item, "multiplier")
                });
            }
            entry.TypeChances = list.ToArray();
        }

        switch (cw)
        {
            case SpecialFirstEnemyWave s:
                entry.SpecialEnemy = EnemyKey(s.specialEnemy);
                entry.NumSpecialEnemies = s.numSpecialEnemies;
                break;
            case AddAttachmentsWave a:
                entry.PartChance = a.partChance;
                entry.PartCount = RangeValues(a.partCount);
                entry.Parts = CatalogParts(a.parts);
                break;
            case ClassListEnemyWave c:
                entry.Classes = WeightedValues(c.classes, EnemyKey);
                break;
        }
        return entry;
    }


    private static EnemyGroup BuildEnemyGroup(EnemyClassGroup eg)
    {
        var entry = new EnemyGroup
        {
            Name = eg.name,
            EnemyType = eg.enemyType.ToString()
        };
        var items = eg.enemyClasses.items;
        if (items != null)
        {
            var list = new List<EnemyGroup.WeightedEnemy>();
            foreach (var it in items)
            {
                if (it.value == null) continue;
                list.Add(new EnemyGroup.WeightedEnemy
                {
                    Enemy = it.value.ID.ToString(),
                    Weight = it.weight
                });
            }
            entry.Enemies = list.ToArray();
        }
        return entry;
    }
}
