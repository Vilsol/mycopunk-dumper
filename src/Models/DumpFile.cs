using System.Collections.Generic;

namespace MycopunkDumper;

public class DumpFile
{
    public GameVersion gameVersion;                                    // Application.version + Steam build ID, captured at dump time
    public SortedDictionary<string, Upgrade> upgrades;
    public SortedDictionary<string, Gear> gears;
    public SortedDictionary<string, CharacterEntry> characters;
    public SortedDictionary<string, Resource> resources;
    public SortedDictionary<string, Directive> directives;
    public SortedDictionary<string, MissionEntry> missions;
    public SortedDictionary<string, ObjectiveEntry> objectives;
    public SortedDictionary<string, Region> regions;
    public SortedDictionary<string, Enemy> enemies;
    public SortedDictionary<string, StatusEffectEntry> statusEffects;
    public SortedDictionary<string, Stack> stacks;
    public SortedDictionary<string, Threat> threats;
    public SortedDictionary<string, MissionModifierEntry> missionModifiers;
    public SortedDictionary<string, LootPoolEntry> lootPools;
    public SortedDictionary<string, Localization> localization;

    public PlayerBase player;                                          // singleton — base player movement/health/wallrun stats
    public SortedDictionary<string, AuthItemEntry> authItems;          // 4 — codex/redemption code items
    public SortedDictionary<string, PatternPathEntry> patternPaths;    // 7 — special hex-grid patterns with rewards
    public SortedDictionary<string, ThreatProfile> threatProfiles;     // per-threat-level scaling curves
    public SortedDictionary<string, CustomWaveEntry> customWaves;      // 25 — wave compositions
    public SortedDictionary<string, CollectableEntry> collectables;    // world-collectable definitions
    public SortedDictionary<string, EnemyGroup> enemyGroups;           // weighted groupings of enemies for spawn waves
    public SortedDictionary<string, GlobalEventEntry> globalEvents;    // timed events (Amalgamation Hunts, contests, …)
    public SortedDictionary<string, PlayerUpgradeEntry> playerUpgrades; // PlayerUpgrade SOs — character abilities (DefaultUpgrade) etc.
    public StatusEffectGlobals statusEffectGlobals;                    // shared status-effect tuning constants
    public Formulas formulas;                                          // hardcoded XP / progression formulas
    public string[] achievements;                                      // Steam achievement IDs scraped from source
    public string[] steamStats;                                        // Steam stat IDs scraped from source

    public SortedDictionary<string, RarityEntry> rarities;             // 6 — master pricing/cost table per rarity tier
    public SortedDictionary<string, MissionContainerEntry> missionContainers; // 2 — DefaultMissionContainer + WeeklyOvertimeAssignment (weekly modifier calendar)
    public SortedDictionary<string, EncounterEntry> encounters;        // 20 — IEncounter prefabs/SOs (procedural mission spawn rules)
    public SortedDictionary<string, QuipEntry> quips;                  // 63 — master player emote/quip catalog
    public Crafting crafting;                                          // singleton — master crafting price table
    public DialogueData dialogue;                                      // dialogue exchanges + per-trigger probabilities
    public SortedDictionary<string, UpgradePresetEntry> upgradePresets; // named visual-modifier presets referenced by SkinUpgradeProperty_Preset

    public LevelMilestoneEntry[] levelMilestones;                      // per-player-level unlock chain (LevelMilestones)
    public PatternInfusion patternInfusion;                            // Pattern Infuser cost formula (hardcoded constants)
    public DirectiveManagerEntry directiveManager;                    // directive tier multipliers + tiered reward pools
    public SortedDictionary<string, GridProfileEntry> gridProfiles;    // per-level upgrade-grid size curves
    public SortedDictionary<string, PlanetEntry> planets;             // planet biome-composition arrays
}

/// <summary>
/// Game version metadata captured at dump time. Sourced from <c>Global.Version</c>
/// (= <c>UnityEngine.Application.version</c>, the value also shown on the start menu)
/// and <c>Global.BuildID</c> (= <c>Online.GetBuildID().ToString()</c>, the Steam build ID).
/// </summary>
public class GameVersion
{
    public string Version;   // semantic-ish version string (e.g. "1.2.0")
    public string BuildID;   // Steam build ID; "0" or empty when not running under Steam
    public string DumpedAt;  // ISO-8601 UTC timestamp when this dump was produced
}
