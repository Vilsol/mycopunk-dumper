namespace MycopunkDumper;

/// <summary>
/// An <c>IEncounter</c> entry — the catalog of "what spawns into a procedural
/// mission" (crater, pipeline, water, meatspawn, upgrade drone, lost saxitos,
/// arc gates, scorched transponder, …). 20 entries observed. Sourced from
/// <c>Global.Instance.encounters</c>, which is a mixed <c>UnityEngine.Object[]</c>:
/// some entries are <c>ScriptableObject</c> implementers, others are GameObject
/// prefabs whose component implements the interface. Keyed by <see cref="Name"/>
/// (the asset name).
///
/// The interesting per-encounter knobs (region masks, spawn weights, prefab refs,
/// per-subclass parameters like <c>CraterEncounter.radius</c>) all live in
/// <see cref="RawData"/>.
/// </summary>
public class EncounterEntry
{
    public string Name;             // asset name — doubles as @ref key ("encounter:<Name>")
    public string Subclass;         // concrete C# class name
    public int Index;               // position in Global.Instance.encounters[]
    public bool IsPrefab;           // true if backed by a GameObject prefab; false for SO impls

    /// <summary>
    /// Full encounter serialized via JsonUtility. Captures every subclass-specific
    /// `[SerializeField]` field (the actual data of interest). Object refs include
    /// <c>@ref</c> siblings.
    /// </summary>
    [Newtonsoft.Json.JsonConverter(typeof(NativeConverter))]
    public UnityEngine.Object RawData;
}
