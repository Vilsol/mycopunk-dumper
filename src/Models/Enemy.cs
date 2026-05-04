namespace MycopunkDumper;

public class Enemy
{
    public string ID;                       // EnemyClass.ID stringified
    public string Name;                     // public Name property (display name)
    public string APIName;                  // public APIName property
    public string InternalName;             // _name field — enemy class identifier (e.g. "b_lasercopter")
    public string Tags;                     // EnemyTags flag enum stringified
    public string LevelFlags;               // LevelFlags enum stringified
    public float OverrideUpgradeDropChance;
    public float OverclockChance;
    public float ShellHealthMultiplier;
    public bool CanBeDespawned;
    public int MinLegs;
    public int MaxLegs;
    public float ArmChance;
    public float LegChance;
    public EnemyConfig Config;
    public Gear.LevelUnlockEntry[] CustomLoot;   // additional drops awarded when this enemy is killed
}

public class EnemyConfig
{
    public string EnemyType;                // EnemyType enum stringified
    public int OverrideEnemySlots;
    public int AgentSize;
    public float NavRadius;
    public bool DontRandRotateCore;
    public float MaxFlankTime;
    public float MinFlankDistance;
    public float TryFlankTime;
    public float FlankChance;
    public float MoveSpeed;
    public float RigidbodySpeedMultiplier;
    public float TurnSpeed;
    public float OverrideAcceleration;
    public float OverrideStoppingDistance;
    public bool AllowRigidbodyMovement;
    public float BalanceDurationBeforeDisablingRigidbodyMovement;
    public int MinLegsToBalance;
    public float RagdollForceThreshold;
    public float HitStunChance;
    public float AddedHeight;
    public bool DisableNavigation;
    public int NavPriority;
    public bool DontDespawnWhenUnableToPath;
    public bool DontLowerTowardTargetWhenFlying;
    public float OverrideThrusterHoverHeight;
    public bool CanAttackMultipleTargets;
    public int MaxConcurrentMeleeAttacks;
    public float MeleeInterval;
    public float SwitchTargetInterval;
    public float SwitchTargetChance;
    public float LookForPartDistance;
    public float LookForPartInterval;
    public float RegrowLimbCooldown;
    public float RegrowLimbDuration;
    public bool OnlyUseCoreHealthForHealthbar;
}
