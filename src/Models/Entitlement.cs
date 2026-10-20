namespace MycopunkDumper;

/// <summary>
/// DLC/supporter pack from <c>Global.Instance.EntitlementPacks</c>. Owning it unlocks
/// <see cref="UnlockWithPack"/> once (gated by <see cref="PlayerFlagTag"/>) and any skin whose
/// <c>RequiredEntitlement</c> points here.
/// </summary>
public class Entitlement
{
    public string Name;                  // SO asset name (key)
    public uint SteamID;                 // Steam DLC app id
    public string PS5ID;
    public bool AvailableOnSteam;
    public string PlayerFlagTag;         // PlayerData flag set on first unlock
    public Upgrade.DIcon NametagIcon;
    public Item[] UnlockWithPack;

    public class Item
    {
        public string Upgrade;           // key into `upgrades`
        public int Seed;
    }
}
