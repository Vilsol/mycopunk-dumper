namespace MycopunkDumper;

// Global.MissionEvents entry — a random mid-mission event (weather, amalgamation spawn).
public class MissionEventEntry
{
    public string Name;
    public string Subclass;
    public int Index;                      // position in Global.MissionEvents
    public int Weight;
    public bool IsPrefab;                  // GameObject whose IMissionEvent is a component

    // WeatherMissionEvent
    public WeightedEntry[] Weather;        // keyed by WeatherManager.WeatherType
    public MinMax<float> RainDuration;     // seconds
    public MinMax<float> FogDuration;
    public MinMax<float> SulfurRainDuration;
    public MinMax<float> DebrisStormDuration;

    // AmalgamationMissionEvent
    public string AmalgamationClass;       // enemy key (matches `enemies`)

    [Newtonsoft.Json.JsonConverter(typeof(NativeConverter))]
    public UnityEngine.Object RawData;
}
