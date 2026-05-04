namespace MycopunkDumper;

/// <summary>
/// A player emote/quip catalog entry. Source: <c>Global.Instance.Quips</c> (63
/// entries observed). Per-character: each <see cref="Character"/> value
/// corresponds to one of the 4 playable characters (Wrangler, Bruiser, Scrapper,
/// Glider) — the equipped-emote slots reference these by id.
///
/// Keyed by <see cref="Label"/> (the localization key, also acts as the canonical
/// API name). The previously-existing per-character <c>quips</c> arrays under
/// <c>characters[].Quips</c> remain for "which quips can character X use"
/// queries; this top-level catalog is the master index.
/// </summary>
public class QuipEntry
{
    public int ID;
    public int Index;                  // position in Global.Instance.Quips
    public string Label;               // _label (also doubles as the localization key + api name)
    public string Character;           // CharacterType enum stringified
    public string QuipType;            // QuipType flags (None, Dance, DisableInVehicle)
    public string VoicelineTextID;     // localization key for the spoken voiceline (resolves via `localization`)
    public string VoicelineText;       // resolved text from VoicelineTextID, block 0 (the actual line spoken)

    public bool TriggerOnFire;
    public bool CancelOnFire;
    public bool PlayThirdPersonAnimationForOwner;
    public bool HideThirdPersonWeapon;
    public bool IsThirdPersonAdditive;
    public float MinAnimationInterval;

    public string AnimationFirstPersonClip;   // PlayerAnimation.Key.clip asset name
    public string AnimationThirdPersonClip;

    public Upgrade.DIcon Icon;
}
