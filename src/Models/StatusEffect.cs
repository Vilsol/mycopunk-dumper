namespace MycopunkDumper;

public class StatusEffectEntry
{
    public string ID;                  // effectID (e.g. "el_acid", "el_decay")
    public string Name;                // _effectName
    public string ColoredName;         // coloredEffectName (rich-text formatted)
    public string VerbName;            // active verb ("burning")
    public string PastVerbName;        // past verb ("burned")
    public string IconTextTag;         // inline rich-text sprite tag (e.g. "<sprite index=3>")
    public bool StopsHealthRegeneration;
    public float MinStageValue;        // threshold at which the effect counts as "applied"
    public uint ElementSwitchID;       // Wwise switch ID
    public int NumStages;              // static — total stages defined for this effect type
    public string TrailColor;
    public string LaserColor;
    public string TextColor;
    public string IconColor;
    public string MuzzleFlashColor;
    public Upgrade.DIcon Icon;

    // Decompilation-derived combat constants (hardcoded in StatusEffect.cs and per-element subclasses).
    // These aren't runtime-readable as plain fields (most are `const` or set inside virtual overrides
    // triggered only when the effect is applied to a target) — values are extracted from source.
    public StatusEffectTuning Tuning;

    public class StatusEffectTuning
    {
        public float DamageMultiplier;        // applied to non-self damage when this effect is fully saturated (Decay 1.2x, Rot 1.35x metal targets)
        public float FullSaturationLifetime;  // seconds the effect persists after reaching full saturation
        public float DecayDelay;              // seconds before saturation begins decaying after last hit (default 3s; 0.5s for Immunity)
        public float DecaySpeed;              // saturation decay rate (default 0.3 / s)
    }
}

/// <summary>Global combat constants that apply across all status effects (from base StatusEffect.cs).</summary>
public class StatusEffectGlobals
{
    public float SaturationAddMultiplier = 0.1f;   // converts damageEffectAmount → saturation gain (effectively /10)
    public float FullSaturationDamage = 10f;        // damage dealt when reaching full saturation against enemy targets
    public float PlayerDamageOnTick = 0.165f;       // DoT tick damage when fully saturated, against player targets
    public float ExplosionDamage = 60f;             // AoE explosion damage multiplier on enemies
    public float ExplosionPlayerDamage = 10f;       // AoE explosion damage multiplier on players
    public float ExplosiveDamageMultiplier = 2f;    // ITarget.ExplosiveDamageMultiplier — flat multiplier on `DamageFlags.Explosive` damage
}

