namespace MycopunkDumper;

/// <summary>
/// Pattern Infusion (the HUB Pattern Infuser): spend Antimass Substrate to infuse one upgrade's
/// pattern into another. The cost is computed from hardcoded constants in
/// <c>PatternInfuserWindow</c> (compile-time inlined, not runtime-readable), so we transcribe them.
/// </summary>
public class PatternInfusion
{
    public int UnlockLevel;             // player level that unlocks the Infuser (InfuserInteractable: 13)
    public string ResourceID;           // resource spent — "antimass"
    public int BaseCost;                // 10
    public int CostPerCellDifference;   // +5 per cell-count difference between the two upgrades
    public int MinCost;                 // floor on the cell-difference term: max(base + diff*per, 1)
    public int CostPerRarityLevel;      // +2 per rarity tier of the target upgrade (Standard=0..Contraband=5)
    public string CostFormula;          // human-readable summary of the above
}
