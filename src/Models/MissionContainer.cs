namespace MycopunkDumper;

/// <summary>
/// A mission-container variant — a mode that wraps the regular mission selection
/// with extra rules (XP/scrip multipliers, modifier rotation, first-vs-repeat
/// rewards). 2 entries observed: <c>DefaultMissionContainer</c> (vanilla play) and
/// <c>WeeklyOvertimeAssignment</c> (the weekly overtime mode with a scheduled
/// modifier calendar). Sourced from <c>Global.Instance.MissionContainers</c>.
/// Keyed by <see cref="Name"/> (asset name).
/// </summary>
public class MissionContainerEntry
{
    public string Name;             // gameObject/SO asset name (e.g. "DefaultMissionContainer", "Overtime Assignment")
    public string Subclass;         // C# class name
    public int Index;               // position in Global.Instance.MissionContainers

    public string AdditionalMissionFlags;   // MissionFlags enum string — flags ORed onto every mission this container wraps
    public string RemovedMissionFlags;      // MissionFlags enum string — flags ANDed off

    /// <summary>
    /// Full SO serialized via JsonUtility. Captures every subclass-specific
    /// `[SerializeField]` field — for <c>WeeklyOvertimeAssignment</c> that
    /// includes the full <c>weekData[]</c> calendar (start times, seeds, modifier
    /// instance IDs that resolve via <c>@ref</c>) plus first-completion
    /// <c>rewards</c> vs <c>repeatableRewards</c>, <c>resetInterval</c>,
    /// <c>minThreat</c>, <c>minIntensity</c>.
    /// </summary>
    [Newtonsoft.Json.JsonConverter(typeof(NativeConverter))]
    public global::MissionContainer RawData;
}
