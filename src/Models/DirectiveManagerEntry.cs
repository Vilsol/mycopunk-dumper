namespace MycopunkDumper;

/// <summary>
/// The <c>DirectiveManager</c> ScriptableObject — the meta layer above individual directives
/// (which are dumped in the <c>directives</c> section). Holds the per-tier reward-scaling
/// multipliers and the tiered reward pools rolled as you climb the directive ladder.
/// </summary>
public class DirectiveManagerEntry
{
    public int TierCount;                       // const 4
    public int PageRewardInterval;              // const 5

    // TierModifier<float> — one value per directive tier (index 0..3).
    public float[] KillsMultiplier;
    public float[] HeavyKillsMultiplier;
    public float[] MissionsMultiplier;

    // Tier0..Tier3 reward pools, indexed by tier.
    public Gear.LevelUnlockEntry[][] TierRewards;
    public Gear.LevelUnlockEntry[] Page10Rewards;
}
