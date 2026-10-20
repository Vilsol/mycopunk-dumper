namespace MycopunkDumper;

// Flattened EnemyClass.PartList — a weighted attachment group rolled once per enemy spawn.
public class EnemyPartList
{
    public float Chance;               // roll <= Chance to attach this group
    public int Min;
    public int Max;
    public string OverrideAttachMode;  // AttachMode enum stringified
    public WeightedEntry[] Parts;      // keyed by part prefab name
}
