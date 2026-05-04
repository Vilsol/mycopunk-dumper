namespace MycopunkDumper;

/// <summary>
/// Hardcoded formula constants and progression rules extracted from the decompiled source.
/// These are compile-time inlined (`const float`) and not runtime-readable as instance fields,
/// so we transcribe them directly. Source: PlayerData.XPNeededForNextLevel call sites.
/// </summary>
public class Formulas
{
    /// <summary>
    /// XP required for next level: <c>coefficient * (currentLevel + 1)^power + add</c>.
    /// Mycopunk uses 4 different parameter sets — two for character, two for gear, switched at a level boundary.
    /// </summary>
    public XPCurve[] XPCurves;

    public class XPCurve
    {
        public string Context;          // "character_1_to_10" | "character_11_plus" | "gear_1_to_5" | "gear_6_plus"
        public string Description;
        public int LevelOffset;         // subtract from currentLevel before applying formula (0 for the base bracket)
        public float Coefficient;
        public float Power;
        public int Add;
    }
}
