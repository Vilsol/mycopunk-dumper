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
            LegChance = e.legChance
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
