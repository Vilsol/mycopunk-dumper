namespace MycopunkDumper;

/// <summary>
/// A timed/seasonal global event (e.g. AmalgamationGlobalEvent — the Amalgamation Hunt).
/// Players make progress against `TotalRequiredProgress`; on completion, `Rewards` fire and
/// optionally an end-mission unlocks (`EndMission`).
/// </summary>
public class GlobalEventEntry
{
    public string ID;
    public string Subclass;          // "GlobalEvent" or a concrete subclass name (e.g. "AmalgamationGlobalEvent")
    public string EventID;
    public string StatID;
    public string Title;
    public string Color;
    public bool Deactivate;
    public bool AreStatsRelative;
    public bool Completed;
    public bool CompleteOnFullProgress;
    public bool MustParticipateToClaimRewards;
    public bool PreviewEndMission;
    public long EndDate;             // Unix timestamp (0 = no deadline)
    public long TotalRequiredProgress;
    public int ProgressOnMissionCompleted;
    public float ProgressOnMissionCompletedVisualMultiplier;
    public float ProgressDisplayMultiplier;
    public string EndMission;        // mission ID cross-ref (matches `missions` map)
    public string EndMissionContainer;
    public string OnStartRoachardLine;
    public string OnStartTextLog;
    public string OnEndTextLog;
    public ProgressStatEntry[] ProgressStats;
    public Gear.LevelUnlockEntry[] Rewards;
    /// <summary>
    /// Populated only when <see cref="Subclass"/> == <c>"CorpContestGlobalEvent"</c>.
    /// One entry per competing corporation: its weapon promotion and the
    /// <c>PlayerResource</c> ticket tracking per-corp contest progress.
    /// </summary>
    public CorpDataEntry[] CorpData;
    [Newtonsoft.Json.JsonConverter(typeof(NativeConverter))]
    public global::GlobalEvent RawData;   // full prefab dump — subclass-specific fields live here

    public class ProgressStatEntry
    {
        public string TitleID;
        public string StatID;
        public string Color;
        public string DisplayType;   // ProgressDisplayType enum stringified
    }

    public class CorpDataEntry
    {
        public string WeaponID;       // gear APIName (resolves in `gears`)
        public string TicketResource; // PlayerResource ID (resolves in `resources`) — the contest's per-corp currency
    }
}
