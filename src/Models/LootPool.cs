namespace MycopunkDumper;

public class LootPoolEntry
{
    public string Name;            // ScriptableObject asset name
    public WeightedRef[] Upgrades; // weighted upgrade keys

    public class WeightedRef
    {
        public string Upgrade;     // upgrade key (matches a top-level `upgrades` map key)
        public int Weight;
    }
}
