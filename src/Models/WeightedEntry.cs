namespace MycopunkDumper;

// One Pigeon.WeightedArray<T> node. Key is the value's dump key (rarity name, enemy ID, prefab name, enum name, …).
public class WeightedEntry
{
    public string Key;
    public int Weight;
}
