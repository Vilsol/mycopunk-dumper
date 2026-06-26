namespace MycopunkDumper;

public class MissionEntry
{
    public string ID;
    public string Name;                  // public Name property (often same as MissionName)
    public string MissionName;           // internal _missionName field
    public string TypeName;              // public TypeName property (display)
    public string MissionTypeName;       // internal _missionTypeName field
    public string Description;
    public string Subclass;              // C# class name (AmalgamationMission, SatelliteSalvageMission, …)

    public string MissionType;           // MissionType enum stringified
    public string MissionFlags;          // MissionFlags enum stringified (comma-separated)
    public string CompatibleLevels;      // LevelFlags enum stringified

    public bool ShowHeader;
    public bool DisableWithoutCommand;
    public bool Selectable;
    public bool ShowInReplayMenu;
    public bool AutoStart;
    public bool StartFirstObjective;
    public bool ExtractAtEnd;
    public bool ResetWeekly;
    public bool DisableDefaultRewards;
    public bool ShowButDontGiveAdditionalRewards;
    public bool PlayStartVoicelineOnLateJoin;

    public int Index;
    public int MinIntensity;
    public int MinLevelToStart;

    public float OverrideExtractDuration;
    public float TeamReviveMultiplier;
    public float ExpectedDurationMultiplier;
    public float MissionXPMultiplier;
    public float MissionScriptMultiplier;
    public float PlayStartVoicelineDuringDropDelay;

    public string Color;                 // RGBA(r, g, b, a)
    public Upgrade.DIcon Icon;
    public Voiceline StartVoiceline;
    public SceneRef[] ValidScenes;

    public Gear.LevelUnlockEntry[] AdditionalRewards;
    public Gear.LevelUnlockEntry[] RepeatRewards;

    /// <summary>
    /// IncursionMission-only data that lives in <c>private static</c> tables, which
    /// JsonUtility (and therefore <c>RawData</c>) cannot reach. Null for every other mission.
    /// </summary>
    public IncursionData Incursion;

    public class IncursionData
    {
        /// <summary>Antimass Substrate granted the first time you reach each floor (and the reduced
        /// repeat amount if you already hit that floor this week). Source: IncursionMission.antimassByFloor.</summary>
        public AntimassFloorReward[] AntimassByFloor;
        /// <summary>Past the last table floor, you gain <c>AntimassPerExtraFloor</c> every
        /// <c>ExtraFloorInterval</c> floors. Source: IncursionMission.GiveFloorRewards.</summary>
        public int AntimassPerExtraFloor;
        public int ExtraFloorInterval;
        /// <summary>Floor counts at which the fungal-infestation layers escalate. Source: IncursionRoom.fungalFloorThresholds.</summary>
        public int[] FungalFloorThresholds;
    }

    public class AntimassFloorReward
    {
        public int Floor;
        public int Antimass;
        public int RepeatAntimass;
    }

    /// <summary>
    /// Full Mission ScriptableObject serialized via Unity's JsonUtility — captures every
    /// subclass-specific [SerializeField] field (e.g. AmalgamationMission.region/scene/globalEvent,
    /// CleanupDetailMission.cleanupDetailObjective/trailCount/bossObjective, etc.) plus all base
    /// fields. Object references (objectives, regions, …) become {"instanceID":N} pairs;
    /// NativeConverter post-processing adds an "@ref" sibling resolving to the catalog key.
    /// </summary>
    [Newtonsoft.Json.JsonConverter(typeof(NativeConverter))]
    public global::Mission RawData;

    public class Voiceline
    {
        public string ID;
        public string Text;     // resolved line from TextBlocks block 0
        public int Priority;
    }

    public class SceneRef
    {
        public string Scene;
        public string LocationName;
    }
}
