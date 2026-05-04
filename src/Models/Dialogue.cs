namespace MycopunkDumper;

/// <summary>
/// A scripted multi-line dialogue exchange between Mission Control and the crew
/// (intro lines, drop-pod chatter, on-revive, on-killed, etc.). Source:
/// <c>Global.Instance.DialogueExchanges</c>. Triggered by gameplay events according
/// to <see cref="Trigger"/>; per-trigger probability is in
/// <see cref="DialogueData.TriggerChances"/>. Keyed by <see cref="ID"/>.
/// </summary>
public class DialogueEntry
{
    public string ID;                  // localization key root — lines resolve via TextBlocks.GetBlockVariant(id, char.APIName, lineIdx)
    public string Trigger;             // Trigger enum: None / Ambient / PlayerJoin / PlayerLeave / DropPodLaunch / DropPodFalling / DropPodLand / DropPodDoorOpen / DropPodReturnToHub / OpenInventory / Killed / KilledByPlayer / Revive
    public string MissionTypes;        // MissionType flag-enum: which mission types this exchange can play in
    public string LevelTypes;          // LevelFlags: Hub / NormalLevels / SpecialLevels
    public string ValidRegions;        // LevelFlags region mask
    public string MainCharacter;       // Dude asset name (Wrangler/Bruiser/Scrapper/Glider/Hunk/Roachard etc.)
    public string SecondaryCharacter;
    public LineEntry[] Lines;

    public class LineEntry
    {
        public string Character;       // Dude asset name — whose voice plays this line
        public float Delay;             // seconds to wait before playing this line
        public bool StartWithNext;      // if true, this line starts simultaneously with the previous
        public string Text;             // resolved line text from TextBlocks (per-character variant if defined; else default block)
    }
}

/// <summary>
/// Top-level container for dialogue data. Kept separate from the per-exchange
/// catalog so consumers can find both pieces in one place (and so the trigger
/// chance table — anonymous floats indexed by enum — has a labeled home).
/// </summary>
public class DialogueData
{
    public System.Collections.Generic.SortedDictionary<string, DialogueEntry> Exchanges;

    /// <summary>
    /// Per-trigger probability that an Ambient/event exchange fires. Indexed by
    /// <c>Mathf.Log2((int)Trigger)</c>, so:
    /// <c>[0]=None, [1]=Ambient, [2]=PlayerJoin, [3]=PlayerLeave, [4]=DropPodLaunch,
    /// [5]=DropPodFalling, [6]=DropPodLand, [7]=DropPodDoorOpen,
    /// [8]=DropPodReturnToHub, [9]=OpenInventory, [10]=Killed/KilledByPlayer,
    /// [11]=Revive</c>.
    /// </summary>
    public float[] TriggerChances;
}
