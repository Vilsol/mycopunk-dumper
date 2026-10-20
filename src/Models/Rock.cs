namespace MycopunkDumper;

/// <summary>
/// Pet-rock source from <c>RockManager.rocks</c>. Array index == <c>PetRock.SourceID</c>.
/// </summary>
public class Rock
{
    public int SourceID;
    public string Asteroid;              // Asteroid prefab name
    public string Skin;                  // key into `upgradePresets`
}
