namespace MycopunkDumper;

public class Threat
{
    public string ID;                  // _threatName (e.g. "threat1".."threat7")
    public string Name;                // ThreatName property
    public string NumberLabel;         // ThreatNumberLabel
    public string Color;
    public Upgrade.DIcon Icon;
    public Gear.LevelUnlockEntry[] MissionRewards;
}
