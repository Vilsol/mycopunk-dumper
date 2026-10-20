namespace MycopunkDumper;

// Global mission-roll parameters (modifier count + side objectives).
public class MissionGeneration
{
    public float MissionModifierChance;
    public CurveData MissionModifierCountCurve;
    public WeightedEntry[] MissionModifiers;   // keyed by `missionModifiers` key
    public float SideObjectiveSpawnChance;
    public WeightedEntry[] SideObjectives;     // keyed by `objectives` key
}
