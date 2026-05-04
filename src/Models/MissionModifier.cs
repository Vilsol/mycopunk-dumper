namespace MycopunkDumper;

public class MissionModifierEntry
{
    public int ID;
    public string ModifierName;        // internal modifierName ("m_fastgrunts", "m_fog", "m_mortar")
    public string APIName;
    public string Name;                // localized
    public string Description;         // localized
    public string TitleAndDescription;
    public string Subclass;            // "MissionModifierGeneric" | "MissionModifierWaveModifier"
    public string Flags;               // ModifierFlags enum stringified
    public string Danger;              // Danger enum stringified ("None", "Low", "Medium", "High", ...)
    public bool CanStack;
    public float XPMultiplier;
    public string Color;
    public string TextColor;
    public Upgrade.DIcon Icon;
    public string[] IncompatibleMissions;     // mission IDs
    public int[] IncompatibleModifiers;       // modifier IDs
}
