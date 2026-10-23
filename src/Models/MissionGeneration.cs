namespace MycopunkDumper;

// Global mission-roll parameters (modifier count + side objectives).
public class MissionGeneration
{
    public float MissionModifierChance;
    public CurveData MissionModifierCountCurve;
    public WeightedEntry[] MissionModifiers;   // keyed by `missionModifiers` key
    public float SideObjectiveSpawnChance;
    public WeightedEntry[] SideObjectives;     // keyed by `objectives` key
    public MissionItem[] MissionItems;         // Global.MissionItems — rolled once per mission
    public string[] HeavyWeapons;              // Global.HeavyWeapons as `gears` keys — GearBeacon picks one uniformly
    public float HeavyWeaponCrateLifetime;

    public class MissionItem
    {
        public string Prefab;
        public float SpawnChance;
        public MinMax<int> Count;
        public float ExclusionRadius;
    }
}
