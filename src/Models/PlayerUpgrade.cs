namespace MycopunkDumper;

/// <summary>
/// A non-grid upgrade that lives on a player character — typically a movement ability
/// (AirDashUpgrade for Wrangler, NoseDiveUpgrade for Bruiser, JetpackUpgrade for Scrapper, …)
/// or a passive flag-bearing modifier. Distinct from grid `Upgrade`s in that these are loaded
/// onto the Player MonoBehaviour at character spawn rather than equipped on the upgrade grid.
/// Each character's `DefaultUpgrade` field references one of these.
/// </summary>
public class PlayerUpgradeEntry
{
    public string Name;             // ScriptableObject asset name
    public string Subclass;         // C# class name (e.g. "NoseDiveUpgrade")
    public string Character;        // Character APIName whose `DefaultUpgrade` points here, if any
    [Newtonsoft.Json.JsonConverter(typeof(NativeConverter))]
    public global::Upgrade RawData; // full prefab serialization — captures Cooldown + nested ability Data structs
}
