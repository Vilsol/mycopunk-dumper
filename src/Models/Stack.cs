namespace MycopunkDumper;

public class Stack
{
    public string Name;                // _name (e.g. "scrap", "wildfire", "Energy Convergence")
    public string Color;
    public int MaxStacks;
    public float LoseStackInterval;    // seconds; how often a stack decays (0 = no periodic decay)
    public float TimeOutDuration;      // seconds; how long stacks last before all expire (0 = no timeout)
    public bool TimeoutOnLevelLoad;
    public bool DisplayOnHUD;
    public Upgrade.DIcon Icon;
}
