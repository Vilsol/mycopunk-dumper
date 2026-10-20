using System.Collections.Generic;

namespace MycopunkDumper;

// Global difficulty scaling. Arrays are indexed by threat level - 1 (8 entries).
public class Difficulty
{
    public Dictionary<string, PlayerScaled<float>[]> EnemyHealthScaling; // keyed by EnemyType (Grunt, Brute, Abomination, Boss)
    public PlayerScaled<float>[] EnemyDamageScaling;
    public PlayerScaled<float>[] EnemyWaveSize;
    public PlayerScaled<float>[] EnemyWaveInterval;
    public float[] EnemyMoveSpeedScaling;
    public float[] EnemyAttackSpeedScaling;
    public float[] LookForPartIntervalScaling;
    public float[] IntensityWaveInterval;       // by intensity (0..2)
    public float[] IntensityMinOpenWaveSlots;   // by intensity (0..2)
    public PlayerScaled<int> DefaultTeamReviveCount;
}
