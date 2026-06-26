namespace MycopunkDumper;

/// <summary>
/// A <c>Planet</c> ScriptableObject — its biome composition. <c>BiomeData</c> is the raw
/// <c>planetBiomeData</c> int array the world generator uses to lay out regions on the planet.
/// </summary>
public class PlanetEntry
{
    public int[] BiomeData;
}
