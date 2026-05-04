namespace MycopunkDumper;

/// <summary>
/// An objective prefab (NetworkBehaviour deriving from <c>ObjectiveBase</c>). The game
/// instantiates these onto a server scene during <c>Mission.SetupMission_Server</c> via
/// <c>Mission.SpawnAndAddObjective(prefab)</c>; missions reference them through serialized
/// fields (e.g. <c>CleanupDetailMission.cleanupDetailObjective</c>). 51 prefab classes
/// observed (<c>AmalgamationObjective</c>, <c>ExtractObjective</c>, …).
///
/// Discoverable at runtime via <c>Resources.FindObjectsOfTypeAll&lt;ObjectiveBase&gt;()</c>.
/// </summary>
public class ObjectiveEntry
{
    public string Name;       // gameObject.name (prefab asset name; doubles as the @ref key)
    public string Subclass;   // C# class name
    public string Title;      // public Title property (TextBlocks-resolved when localized)
    public bool AddWaypoint;
    public bool SetupUIOnActivate;
    public bool ShowCompleteMessage;
    public bool IsSideObjective;
    public VoicelineRef StartVoiceline;
    public VoicelineRef CompleteVoiceline;
    public ObjectiveInfoEntry[] ObjectiveInfoList;

    /// <summary>
    /// Full prefab serialized via JsonUtility — every <c>[SerializeField]</c> field on the
    /// concrete subclass (decommissioner counts, attack-to-spawn-relays maps, sub-objective
    /// references, …). Object references include <c>@ref</c> sibling for resolution.
    /// </summary>
    [Newtonsoft.Json.JsonConverter(typeof(NativeConverter))]
    public global::ObjectiveBase RawData;

    public class VoicelineRef
    {
        public string ID;            // TextBlocks localization key (also routes a Wwise event of the same id)
        public string Text;          // resolved line from TextBlocks block 0
        public int Priority;
    }

    public class ObjectiveInfoEntry
    {
        public string TitleID;             // localization key (e.g. "o_breach")
        public string Title;               // resolved from TitleID block 0 (e.g. "Breach The Door")
        public string DescriptionID;       // localization key (e.g. "o_breachwait")
        public string Description;         // resolved from DescriptionID block 0
        public bool ShowNumberProgress;
    }
}
