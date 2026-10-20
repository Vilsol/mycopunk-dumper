namespace MycopunkDumper;

// Hazard or interactable prefab (hornet, hostile turret, shrooms, collectables, launch pad, …). Same component shape as EnemyPartPrefab.
public class WorldObject
{
    public string Category;                      // "hazard" | "interactable"
    public string Source;                        // Global field / gear field / component type it was found through
    public EnemyPartComponent[] Components;
}
