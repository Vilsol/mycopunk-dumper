namespace MycopunkDumper;

/// <summary>
/// One entry in the player-level milestone chain shown when you click your player level in the
/// inventory screen. Built by walking <c>LevelMilestones.FirstMilestone()</c> — the chain is
/// constructed in code (not a ScriptableObject), so it can only be read at runtime.
/// </summary>
public class LevelMilestoneEntry
{
    public int Level;
    public LevelMilestoneItem[] Items;

    public class LevelMilestoneItem
    {
        public string Label;   // HoverInfo.Item.Name — already resolved through TextBlocks
        public string Icon;    // icon texture name, when present
    }
}
