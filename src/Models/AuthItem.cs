namespace MycopunkDumper;

/// <summary>
/// AuthItem — the in-fiction "redemption code" inventory items. There are 4: one per playable
/// character (auth_bruiser, auth_glider, auth_scrapper, auth_wrangler). Each grants its
/// character + a starter upgrade when redeemed.
/// </summary>
public class AuthItemEntry
{
    public string ID;             // e.g. "auth_bruiser"
    public string Name;           // resolved via TextBlocks.GetString(id, 0)
    public string Color;
    public string Rarity;
    public string Character;      // character APIName
    public string Upgrade;        // upgrade key
}
