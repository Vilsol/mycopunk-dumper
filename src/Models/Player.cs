namespace MycopunkDumper;

/// <summary>
/// Singleton dump of the player MonoBehaviour prefab. Every base movement / health / wallrun /
/// slide / clamber / jump / gravity / acceleration / deceleration / fall stat lives here.
/// One entry per game (the player rig is shared across all characters; per-character variation
/// comes from upgrades and skill tree, not the base prefab).
/// </summary>
public class PlayerBase
{
    public string Name;
    [Newtonsoft.Json.JsonConverter(typeof(NativeConverter))]
    public Pigeon.Movement.Player RawData;
}
