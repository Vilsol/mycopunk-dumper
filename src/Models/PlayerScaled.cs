namespace MycopunkDumper;

// Flattened PlayerModifier<T>: Values[n-1] for n = 1..4 players; beyond 4 the game uses
// Values[3] + PerExtraPlayer * (n - 4) (rounded for ints).
public class PlayerScaled<T>
{
    public T[] Values;
    public float PerExtraPlayer;
}
