namespace MycopunkDumper;

public class Directive
{
    public int ID;
    public string Name;            // resolved from the first property's NameID, block 0 (e.g. "Cleanse", "Elemental Testing")
    public string Description;     // resolved from the first property's NameID, block 1
    public bool CanBeChosen;
    public Upgrade.DIcon Icon;
    public TierWeightsEntry TierWeights;
    public DirectivePropertyEntry[] Properties;
    public Gear.LevelUnlockEntry[] AdditionalRewards;

    public class TierWeightsEntry
    {
        public float Tier1;
        public float Tier2;
        public float Tier3;
        public float Tier4;
    }

    public class DirectivePropertyEntry
    {
        public string Type;             // C# class name, e.g. "DirectiveProperty_KillDeaths"
        public string Label;            // Prettified class name, e.g. "Kill Deaths"
        public string NameID;           // localization key (DirectiveProperty.NameID), e.g. "dp_kill", "dp_kill_e"
        public string Name;             // resolved from NameID block 0 (display name)
        public string Description;      // resolved from NameID block 1
        [Newtonsoft.Json.JsonConverter(typeof(NativeConverter))]
        public global::DirectiveProperty Raw;
    }
}
