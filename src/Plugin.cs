using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using Newtonsoft.Json;

namespace MycopunkDumper;

[MycoMod(null, ModFlags.IsClientSide)]
[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public partial class Plugin : BaseUnityPlugin
{
    private static readonly SortedDictionary<string, Upgrade> UpgradeMap = new();
    private static readonly SortedDictionary<string, Gear> GearMap = new();
    private static readonly SortedDictionary<string, CharacterEntry> CharMap = new();
    private static readonly SortedDictionary<string, Resource> ResourceMap = new();
    private static readonly SortedDictionary<string, Directive> DirectiveMap = new();
    private static readonly SortedDictionary<string, MissionEntry> MissionMap = new();
    private static readonly SortedDictionary<string, ObjectiveEntry> ObjectiveMap = new();
    private static readonly SortedDictionary<string, Region> RegionMap = new();
    private static readonly SortedDictionary<string, Enemy> EnemyMap = new();
    private static readonly SortedDictionary<string, StatusEffectEntry> StatusEffectMap = new();
    private static readonly SortedDictionary<string, Stack> StackMap = new();
    private static readonly SortedDictionary<string, Threat> ThreatMap = new();
    private static readonly SortedDictionary<string, MissionModifierEntry> MissionModifierMap = new();
    private static readonly SortedDictionary<string, LootPoolEntry> LootPoolMap = new();
    private static readonly SortedDictionary<string, Localization> LocalizationMap = new();
    private static PlayerBase PlayerBaseEntry;
    private static readonly SortedDictionary<string, AuthItemEntry> AuthItemMap = new();
    private static readonly SortedDictionary<string, PatternPathEntry> PatternPathMap = new();
    private static readonly SortedDictionary<string, ThreatProfile> ThreatProfileMap = new();
    private static readonly SortedDictionary<string, CustomWaveEntry> CustomWaveMap = new();
    private static readonly SortedDictionary<string, CollectableEntry> CollectableMap = new();
    private static readonly SortedDictionary<string, EnemyGroup> EnemyGroupMap = new();
    private static readonly SortedDictionary<string, GlobalEventEntry> GlobalEventMap = new();
    private static readonly SortedDictionary<string, PlayerUpgradeEntry> PlayerUpgradeMap = new();
    private static readonly SortedDictionary<string, RarityEntry> RarityMap = new();
    private static readonly SortedDictionary<string, MissionContainerEntry> MissionContainerMap = new();
    private static readonly SortedDictionary<string, EncounterEntry> EncounterMap = new();
    private static readonly SortedDictionary<string, QuipEntry> QuipMap = new();
    private static Crafting CraftingEntry;
    private static DialogueData DialogueEntry;
    private static readonly SortedDictionary<string, UpgradePresetEntry> UpgradePresetMap = new();
    private static readonly SortedDictionary<string, GridProfileEntry> GridProfileMap = new();
    private static readonly SortedDictionary<string, PlanetEntry> PlanetMap = new();

    internal static BepInEx.Logging.ManualLogSource Log;

    private void Awake()
    {
        Log = Logger;
        NativeConverter.Logger = Logger;
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }

    private void Start()
    {
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is started!");

        Probe.Run();
        SkinRenderer.Run();   // no-op unless render-skins.flag is present and rendering is enabled

        UpgradeMap.Clear();
        GearMap.Clear();
        CharMap.Clear();
        ResourceMap.Clear();
        DirectiveMap.Clear();
        MissionMap.Clear();
        ObjectiveMap.Clear();
        RegionMap.Clear();
        EnemyMap.Clear();
        StatusEffectMap.Clear();
        StackMap.Clear();
        ThreatMap.Clear();
        MissionModifierMap.Clear();
        LootPoolMap.Clear();
        LocalizationMap.Clear();
        PlayerBaseEntry = null;
        AuthItemMap.Clear();
        PatternPathMap.Clear();
        ThreatProfileMap.Clear();
        CustomWaveMap.Clear();
        CollectableMap.Clear();
        EnemyGroupMap.Clear();
        GlobalEventMap.Clear();
        PlayerUpgradeMap.Clear();
        RarityMap.Clear();
        MissionContainerMap.Clear();
        EncounterMap.Clear();
        QuipMap.Clear();
        CraftingEntry = null;
        DialogueEntry = null;
        UpgradePresetMap.Clear();
        GridProfileMap.Clear();
        PlanetMap.Clear();
        NativeConverter.InstanceRefs.Clear();

        if (Global.Instance == null)
        {
            Logger.LogError("Global.Instance is null; aborting dump");
            return;
        }

        // Build instance-ID → ref-key index BEFORE serialization so NativeConverter can rewrite
        // {"instanceID":N} blobs in JsonUtility output. We index every type whose semantic key
        // we expose at the top level of the dump.
        foreach (var u in UnityEngine.Resources.FindObjectsOfTypeAll<global::Upgrade>())
        {
            if (u == null) continue;
            NativeConverter.InstanceRefs[u.GetInstanceID()] = "upgrade:" + UpgradeKey(u.ID);
        }
        foreach (var r in UnityEngine.Resources.FindObjectsOfTypeAll<PlayerResource>())
        {
            if (r == null || string.IsNullOrEmpty(r.ID)) continue;
            NativeConverter.InstanceRefs[r.GetInstanceID()] = "resource:" + r.ID;
            ResourceMap[r.ID] = BuildResource(r);
        }
        foreach (var d in UnityEngine.Resources.FindObjectsOfTypeAll<DirectiveData>())
        {
            if (d == null) continue;
            NativeConverter.InstanceRefs[d.GetInstanceID()] = "directive:" + d.ID;
        }
        foreach (var m in UnityEngine.Resources.FindObjectsOfTypeAll<Mission>())
        {
            if (m == null || string.IsNullOrEmpty(m.ID)) continue;
            NativeConverter.InstanceRefs[m.GetInstanceID()] = "mission:" + m.ID;
        }
        foreach (var o in UnityEngine.Resources.FindObjectsOfTypeAll<ObjectiveBase>())
        {
            if (o == null) continue;
            var n = o.gameObject?.name ?? o.GetType().Name;
            // First write wins — multiple components on the same prefab share gameObject.name;
            // we want the canonical Objective component, not duplicates from sub-components.
            if (!NativeConverter.InstanceRefs.ContainsKey(o.GetInstanceID()))
                NativeConverter.InstanceRefs[o.GetInstanceID()] = "objective:" + n;
        }
        foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<WorldRegion>())
        {
            if (w == null) continue;
            NativeConverter.InstanceRefs[w.GetInstanceID()] = "region:" + w.ID;
        }
        foreach (var e in UnityEngine.Resources.FindObjectsOfTypeAll<EnemyClass>())
        {
            if (e == null) continue;
            NativeConverter.InstanceRefs[e.GetInstanceID()] = "enemy:" + e.ID;
        }
        foreach (var s in UnityEngine.Resources.FindObjectsOfTypeAll<StatusEffectData>())
        {
            if (s == null) continue;
            var id = GetPrivateField<string>(s, "effectID");
            if (!string.IsNullOrEmpty(id)) NativeConverter.InstanceRefs[s.GetInstanceID()] = "statusEffect:" + id;
        }
        foreach (var p in UnityEngine.Resources.FindObjectsOfTypeAll<PlayerStackData>())
        {
            if (p == null) continue;
            var n = GetPrivateField<string>(p, "_name") ?? p.name;
            if (!string.IsNullOrEmpty(n)) NativeConverter.InstanceRefs[p.GetInstanceID()] = "stack:" + n;
        }
        foreach (var t in UnityEngine.Resources.FindObjectsOfTypeAll<ThreatData>())
        {
            if (t == null) continue;
            var n = GetPrivateField<string>(t, "_threatName") ?? t.name;
            if (!string.IsNullOrEmpty(n)) NativeConverter.InstanceRefs[t.GetInstanceID()] = "threat:" + n;
        }
        foreach (var mm in UnityEngine.Resources.FindObjectsOfTypeAll<MissionModifier>())
        {
            if (mm == null) continue;
            NativeConverter.InstanceRefs[mm.GetInstanceID()] = "missionModifier:" + mm.ID;
        }
        foreach (var lp in UnityEngine.Resources.FindObjectsOfTypeAll<LootPool>())
        {
            if (lp == null) continue;
            NativeConverter.InstanceRefs[lp.GetInstanceID()] = "lootPool:" + lp.name;
        }
        // Mission containers — small fixed array on Global; index for @ref
        if (Global.Instance.MissionContainers != null)
            foreach (var mc in Global.Instance.MissionContainers)
                if (mc != null) NativeConverter.InstanceRefs[mc.GetInstanceID()] = "missionContainer:" + mc.name;
        // Encounters — Global.encounters is UnityEngine.Object[] (mixed SO+GO);
        // both have GetInstanceID(). Asset name doubles as @ref key.
        var encArr = GetPrivateField<UnityEngine.Object[]>(Global.Instance, "encounters");
        if (encArr != null)
            foreach (var e in encArr)
                if (e != null) NativeConverter.InstanceRefs[e.GetInstanceID()] = "encounter:" + e.name;
        // Quips — master player-emote catalog. Keyed by id; same label can
        // repeat across characters so id is the canonical handle.
        if (Global.Instance.Quips != null)
            foreach (var q in Global.Instance.Quips)
                if (q != null) NativeConverter.InstanceRefs[q.GetInstanceID()] = "quip:" + GetPrivateField<int>(q, "id");
        // UpgradePreset — referenced from SkinUpgradeProperty_Preset.preset; resolves
        // raw instanceIDs into named preset themes ("Coppertone", "Bloodmetal", …).
        foreach (var up in UnityEngine.Resources.FindObjectsOfTypeAll<UpgradePreset>())
            if (up != null) NativeConverter.InstanceRefs[up.GetInstanceID()] = "upgradePreset:" + up.name;
        foreach (var u in Global.Instance.AllGear ?? Array.Empty<IUpgradable>())
        {
            if (u?.Info == null) continue;
            var apiName = u.Info.APIName ?? u.GetType().Name;
            NativeConverter.InstanceRefs[u.Info.GetInstanceID()] = "gear:" + apiName;
        }
        foreach (var c in Global.Instance.Characters ?? Array.Empty<Character>())
        {
            if (c?.Info == null) continue;
            var apiName = c.Info.APIName ?? c.GetType().Name;
            NativeConverter.InstanceRefs[c.Info.GetInstanceID()] = "character:" + apiName;
        }
        Logger.LogInfo($"Built instance-ref index: {NativeConverter.InstanceRefs.Count} entries");

        // LootPool dump — typed access via WeightedArray<Upgrade>
        foreach (var lp in UnityEngine.Resources.FindObjectsOfTypeAll<LootPool>())
        {
            if (lp == null || string.IsNullOrEmpty(lp.name) || LootPoolMap.ContainsKey(lp.name)) continue;
            LootPoolMap[lp.name] = BuildLootPool(lp);
        }

        // Player singleton — base movement/health/wallrun/etc on the player MonoBehaviour prefab.
        var players = UnityEngine.Resources.FindObjectsOfTypeAll<Pigeon.Movement.Player>();
        if (players.Length > 0)
        {
            PlayerBaseEntry = new PlayerBase { Name = players[0].name, RawData = players[0] };
        }

        // AuthItem (4) — codex / redemption-code items
        foreach (var a in UnityEngine.Resources.FindObjectsOfTypeAll<AuthItem>())
        {
            if (a == null) continue;
            var id = GetPrivateField<string>(a, "id");
            if (string.IsNullOrEmpty(id) || AuthItemMap.ContainsKey(id)) continue;
            var ch = GetPrivateField<Character>(a, "character");
            var up = GetPrivateField<global::Upgrade>(a, "upgrade");
            AuthItemMap[id] = new AuthItemEntry
            {
                ID = id,
                Name = a.Name,
                Color = a.Color.ToString(),
                Rarity = a.Rarity.ToString(),
                Character = ch?.Info?.APIName,
                Upgrade = up != null ? UpgradeKey(up.ID) : null
            };
        }

        // PatternPath (7) — special hex patterns granting rewards
        foreach (var pp in UnityEngine.Resources.FindObjectsOfTypeAll<PatternPath>())
        {
            if (pp == null) continue;
            var key = pp.ID.ToString();
            if (PatternPathMap.ContainsKey(key)) continue;
            var entry = new PatternPathEntry
            {
                ID = pp.ID,
                Name = pp.name,
                Rarities = pp.Rarities.ToString(),
                Pattern = pp.Pattern
            };
            var rewards = GetPrivateField<LevelUnlockList>(pp, "rewards").Properties;
            if (rewards != null)
                entry.Rewards = rewards.Where(x => x != null).Select(BuildLevelUnlock).ToArray();
            PatternPathMap[key] = entry;
            NativeConverter.InstanceRefs[pp.GetInstanceID()] = "patternPath:" + key;
        }

        // ThreatModifierProfile — per-tier scaling
        foreach (var tp in UnityEngine.Resources.FindObjectsOfTypeAll<ThreatModifierProfile>())
        {
            if (tp == null || ThreatProfileMap.ContainsKey(tp.name)) continue;
            var threats = GetPrivateField<float[]>(tp.ThreatModifier, "threats");
            ThreatProfileMap[tp.name] = new ThreatProfile { Name = tp.name, Threats = threats };
            NativeConverter.InstanceRefs[tp.GetInstanceID()] = "threatProfile:" + tp.name;
        }

        // CustomWave (covers GenericCustomWave + any subclass)
        foreach (var cw in UnityEngine.Resources.FindObjectsOfTypeAll<CustomWave>())
        {
            if (cw == null || CustomWaveMap.ContainsKey(cw.name)) continue;
            CustomWaveMap[cw.name] = BuildCustomWave(cw);
            NativeConverter.InstanceRefs[cw.GetInstanceID()] = "customWave:" + cw.name;
        }

        // CollectableProfile
        foreach (var cp in UnityEngine.Resources.FindObjectsOfTypeAll<CollectableProfile>())
        {
            if (cp == null) continue;
            var id = cp.APIName;
            if (string.IsNullOrEmpty(id) || CollectableMap.ContainsKey(id)) continue;
            CollectableMap[id] = BuildCollectable(cp);
            NativeConverter.InstanceRefs[cp.GetInstanceID()] = "collectable:" + id;
        }

        // EnemyClassGroup
        foreach (var eg in UnityEngine.Resources.FindObjectsOfTypeAll<EnemyClassGroup>())
        {
            if (eg == null || EnemyGroupMap.ContainsKey(eg.name)) continue;
            EnemyGroupMap[eg.name] = BuildEnemyGroup(eg);
            NativeConverter.InstanceRefs[eg.GetInstanceID()] = "enemyGroup:" + eg.name;
        }

        // PlayerUpgrade SOs — character abilities (DefaultUpgrade) and other non-grid player upgrades.
        // These are subclasses of `Upgrade` (so they're already in `upgrades` map) but the typed
        // base Upgrade DTO doesn't expose their per-ability data structs (Cooldown, ability tuning).
        // Map character → PlayerUpgrade via `Character.DefaultUpgrade` and dump the prefab.
        var defaultUpgradeByPrefab = new Dictionary<UnityEngine.Object, string>();
        foreach (var c in Global.Instance.Characters ?? Array.Empty<Character>())
        {
            if (c?.DefaultUpgrade != null && c.Info != null)
                defaultUpgradeByPrefab[c.DefaultUpgrade] = c.Info.APIName;
        }
        // Find all PlayerUpgrade SO subclasses by name pattern in the global catalog
        foreach (var u in UnityEngine.Resources.FindObjectsOfTypeAll<global::Upgrade>())
        {
            if (u == null) continue;
            var t = u.GetType();
            // Heuristic: include character ability upgrades + skin upgrades' PlayerUpgrade siblings.
            // Skip generic types whose data is already in Properties.
            if (t.Name == "GenericGunUpgrade" || t.Name == "GenericPlayerUpgrade" || t.Name == "SkinUpgrade") continue;
            if (t.Name == "Upgrade") continue;
            var key = u.name;
            if (string.IsNullOrEmpty(key) || PlayerUpgradeMap.ContainsKey(key)) continue;
            defaultUpgradeByPrefab.TryGetValue(u, out var character);
            PlayerUpgradeMap[key] = new PlayerUpgradeEntry
            {
                Name = u.name,
                Subclass = t.Name,
                Character = character,
                RawData = u
            };
        }

        // GlobalEvent (and subclasses, e.g. AmalgamationGlobalEvent)
        foreach (var ge in UnityEngine.Resources.FindObjectsOfTypeAll<global::GlobalEvent>())
        {
            if (ge == null) continue;
            var id = GetPrivateField<string>(ge, "id") ?? ge.name;
            if (string.IsNullOrEmpty(id) || GlobalEventMap.ContainsKey(id)) continue;
            GlobalEventMap[id] = BuildGlobalEvent(ge);
            NativeConverter.InstanceRefs[ge.GetInstanceID()] = "globalEvent:" + id;
        }

        // Rarity table (Global.Instance.Rarities — struct array, not SOs).
        BuildRarities();
        // Mission containers (Default + WeeklyOvertimeAssignment).
        if (Global.Instance.MissionContainers != null)
            for (int i = 0; i < Global.Instance.MissionContainers.Length; i++)
            {
                var mc = Global.Instance.MissionContainers[i];
                if (mc == null) continue;
                MissionContainerMap[mc.name] = new MissionContainerEntry
                {
                    Name = mc.name,
                    Subclass = mc.GetType().Name,
                    Index = i,
                    AdditionalMissionFlags = GetPrivateField<MissionFlags>(mc, "additionalMissionFlags").ToString(),
                    RemovedMissionFlags = GetPrivateField<MissionFlags>(mc, "removedMissionFlags").ToString(),
                    RawData = mc
                };
            }
        // Encounter catalog (Global.Instance.encounters — mixed SO/GO).
        if (encArr != null)
            for (int i = 0; i < encArr.Length; i++)
            {
                var e = encArr[i];
                if (e == null) continue;
                EncounterMap[e.name] = new EncounterEntry
                {
                    Name = e.name,
                    Subclass = e.GetType().Name,
                    Index = i,
                    IsPrefab = e is UnityEngine.GameObject,
                    RawData = e
                };
            }
        // Master quip catalog (Global.Instance.Quips — 63 entries). Keyed by
        // numeric id (stringified) since the same `_label` can repeat across
        // characters (e.g. each character has its own `e_dance`).
        if (Global.Instance.Quips != null)
            for (int i = 0; i < Global.Instance.Quips.Length; i++)
            {
                var q = Global.Instance.Quips[i];
                if (q == null) continue;
                var id = GetPrivateField<int>(q, "id").ToString();
                if (QuipMap.ContainsKey(id)) continue;
                QuipMap[id] = BuildQuipEntry(q, i, GetPrivateField<string>(q, "_label") ?? q.name);
            }
        // Crafting price table (singleton CraftingWindow MonoBehaviour).
        BuildCrafting();
        // UpgradePreset catalog — small SO catalog referenced by skins.
        foreach (var up in UnityEngine.Resources.FindObjectsOfTypeAll<UpgradePreset>())
        {
            if (up == null || string.IsNullOrEmpty(up.name) || UpgradePresetMap.ContainsKey(up.name)) continue;
            var rcRange = up.RandomContainerRange;
            // Resolve the preset's own UpgradePropertyList — same shape as
            // per-skin Modifiers, so a consumer can answer "what does Glittering
            // do?" without digging through RawData.
            var presetMods = new List<Upgrade.DSkinModifier>();
            try
            {
                var pe = up.Properties.GetEnumerator();
                while (pe.MoveNext())
                {
                    if (pe.Current == null) continue;
                    presetMods.Add(BuildSkinModifier(pe.Current));
                }
            }
            catch (Exception ex) { Logger.LogWarning($"UpgradePreset {up.name} property walk failed: {ex.Message}"); }
            UpgradePresetMap[up.name] = new UpgradePresetEntry
            {
                Name = up.name,
                OverrideNameModifier = up.OverrideNameModifier,
                NameModifierColor = up.NameModifierColor,
                ShowNameInStats = up.ShowNameInStats,
                RandomContainerRangeMin = rcRange.min,
                RandomContainerRangeMax = rcRange.max,
                Modifiers = presetMods.ToArray(),
                RawData = up
            };
        }
        // GridProfile — per-player-level upgrade-grid size curves.
        foreach (var gp in UnityEngine.Resources.FindObjectsOfTypeAll<GridProfile>())
        {
            if (gp == null || string.IsNullOrEmpty(gp.name) || GridProfileMap.ContainsKey(gp.name)) continue;
            var sizes = gp.GetGridSizes();
            GridProfileMap[gp.name] = new GridProfileEntry
            {
                GridSizes = (sizes ?? Array.Empty<GridProfile.GridSize>())
                    .Select(s => new GridProfileEntry.GridSizeEntry { Level = s.level, Width = s.width, Height = s.height })
                    .ToArray()
            };
        }

        // Planet — biome composition arrays.
        foreach (var pl in UnityEngine.Resources.FindObjectsOfTypeAll<Planet>())
        {
            if (pl == null || string.IsNullOrEmpty(pl.name) || PlanetMap.ContainsKey(pl.name)) continue;
            PlanetMap[pl.name] = new PlanetEntry { BiomeData = pl.PlanetBiomeData };
        }

        // Dialogue catalog + per-trigger probabilities.
        BuildDialogue();

        // Localization dump — TextBlocks.strings is the master key→text dictionary loaded from CSV.
        // Every game-displayed string (stat labels, upgrade names/descriptions, mission text, …)
        // resolves through it. Key examples: "force" → "Force", "dam_min" → "Min Damage",
        // "scrip" → "Gats", and per-upgrade: <upgrade.APIName> → [Name, Description].
        foreach (var kv in TextBlocks.strings)
        {
            if (string.IsNullOrEmpty(kv.Key) || kv.Value?.blocks == null) continue;
            var entry = new Localization { ID = kv.Value.id };
            entry.Blocks = kv.Value.blocks
                .Where(b => b != null)
                .Select(b => new Localization.LocalizationBlock { Text = b.text, UniqueID = b.uniqueID })
                .ToArray();
            LocalizationMap[kv.Key] = entry;
        }

        foreach (var dd in UnityEngine.Resources.FindObjectsOfTypeAll<DirectiveData>())
        {
            if (dd == null) continue;
            DirectiveMap[dd.ID.ToString()] = BuildDirective(dd);
        }

        if (Global.Instance.Missions != null)
        {
            foreach (var m in Global.Instance.Missions)
            {
                if (m == null || string.IsNullOrEmpty(m.ID)) continue;
                MissionMap[m.ID] = BuildMission(m);
            }
        }
        foreach (var o in UnityEngine.Resources.FindObjectsOfTypeAll<ObjectiveBase>())
        {
            if (o == null) continue;
            var key = o.gameObject?.name ?? o.GetType().Name;
            if (string.IsNullOrEmpty(key) || ObjectiveMap.ContainsKey(key)) continue;
            ObjectiveMap[key] = BuildObjective(o, key);
        }
        if (Global.Instance.Regions != null)
        {
            foreach (var r in Global.Instance.Regions)
            {
                if (r == null) continue;
                RegionMap[r.ID.ToString()] = BuildRegion(r);
            }
        }

        // Wiki/calculator-grade catalogs (all SOs discoverable via Resources.FindObjectsOfTypeAll)
        foreach (var e in UnityEngine.Resources.FindObjectsOfTypeAll<EnemyClass>())
        {
            if (e == null) continue;
            // Skip developer/test entries that pollute the enemy catalog: TestBrute,
            // the literal "//:ERROR://" placeholder, and three legacy ID=0 entries
            // (abomination, b_barrel, g_bomber) that are leftovers from earlier builds.
            var iname = GetPrivateField<string>(e, "_name") ?? e.name ?? "";
            if (e.ID == 0 && (iname == "abomination" || iname == "b_barrel" || iname == "g_bomber")) continue;
            if (iname == "TestBrute" || iname == "//:ERROR://") continue;
            var key = e.ID != 0 ? e.ID.ToString() : iname;
            if (string.IsNullOrEmpty(key) || EnemyMap.ContainsKey(key)) continue;
            EnemyMap[key] = BuildEnemy(e);
        }
        foreach (var s in UnityEngine.Resources.FindObjectsOfTypeAll<StatusEffectData>())
        {
            if (s == null) continue;
            var key = GetPrivateField<string>(s, "effectID");
            if (string.IsNullOrEmpty(key) || StatusEffectMap.ContainsKey(key)) continue;
            StatusEffectMap[key] = BuildStatusEffect(s);
        }
        foreach (var p in UnityEngine.Resources.FindObjectsOfTypeAll<PlayerStackData>())
        {
            if (p == null) continue;
            var key = GetPrivateField<string>(p, "_name") ?? p.name;
            if (string.IsNullOrEmpty(key) || StackMap.ContainsKey(key)) continue;
            StackMap[key] = BuildStack(p);
        }
        foreach (var t in UnityEngine.Resources.FindObjectsOfTypeAll<ThreatData>())
        {
            if (t == null) continue;
            var key = GetPrivateField<string>(t, "_threatName") ?? t.name;
            if (string.IsNullOrEmpty(key) || ThreatMap.ContainsKey(key)) continue;
            ThreatMap[key] = BuildThreat(t);
        }
        foreach (var m in UnityEngine.Resources.FindObjectsOfTypeAll<MissionModifier>())
        {
            if (m == null) continue;
            var key = m.ID.ToString();
            if (string.IsNullOrEmpty(key) || MissionModifierMap.ContainsKey(key)) continue;
            MissionModifierMap[key] = BuildMissionModifier(m);
        }

        foreach (var upgradable in Global.Instance.AllGear)
        {
            ProcessUpgrades(upgradable);
            var apiName = upgradable.Info?.APIName ?? upgradable.GetType().Name;
            if (!GearMap.ContainsKey(apiName)) GearMap[apiName] = BuildGear(upgradable);
        }

        foreach (var character in Global.Instance.Characters)
        {
            ProcessUpgrades(character);
            var apiName = character.Info?.APIName ?? character.GetType().Name;
            if (!CharMap.ContainsKey(apiName)) CharMap[apiName] = BuildCharacter(character);
        }

        ProcessUpgrades(Global.Instance);

        // Skin preview path post-pass — needs ApplicableTo to be fully populated.
        PopulateSkinPreviews();

        Logger.LogInfo($"Found {UpgradeMap.Count} upgrades, {GearMap.Count} gears, {CharMap.Count} characters, {ResourceMap.Count} resources, {DirectiveMap.Count} directives, {MissionMap.Count} missions, {ObjectiveMap.Count} objectives, {RegionMap.Count} regions, {EnemyMap.Count} enemies, {StatusEffectMap.Count} status effects, {StackMap.Count} stacks, {ThreatMap.Count} threats, {MissionModifierMap.Count} mission modifiers, {LootPoolMap.Count} loot pools, {LocalizationMap.Count} localization keys, {AuthItemMap.Count} auth items, {PatternPathMap.Count} patterns, {ThreatProfileMap.Count} threat profiles, {CustomWaveMap.Count} waves, {CollectableMap.Count} collectables, {EnemyGroupMap.Count} enemy groups, {RarityMap.Count} rarities, {MissionContainerMap.Count} mission containers, {EncounterMap.Count} encounters, {QuipMap.Count} quips, dialogue={(DialogueEntry?.Exchanges?.Count ?? 0)}, player={(PlayerBaseEntry != null)}");

        var outputPath = Path.Combine(Paths.GameRootPath, "data.json");
        using var writer = new StreamWriter(outputPath, false);
        string gameVersion = null, buildId = null;
        try { gameVersion = Global.Version; } catch (Exception ex) { Logger.LogWarning($"Global.Version failed: {ex.Message}"); }
        try { buildId = Global.BuildID; } catch (Exception ex) { Logger.LogWarning($"Global.BuildID failed: {ex.Message}"); }
        Logger.LogInfo($"Game version: {gameVersion} (build {buildId})");

        var dump = new DumpFile
        {
            gameVersion = new GameVersion
            {
                Version = gameVersion,
                BuildID = buildId,
                DumpedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
            },
            upgrades = UpgradeMap,
            gears = GearMap,
            characters = CharMap,
            resources = ResourceMap,
            directives = DirectiveMap,
            missions = MissionMap,
            objectives = ObjectiveMap,
            regions = RegionMap,
            enemies = EnemyMap,
            statusEffects = StatusEffectMap,
            stacks = StackMap,
            threats = ThreatMap,
            missionModifiers = MissionModifierMap,
            lootPools = LootPoolMap,
            localization = LocalizationMap,
            player = PlayerBaseEntry,
            authItems = AuthItemMap,
            patternPaths = PatternPathMap,
            threatProfiles = ThreatProfileMap,
            customWaves = CustomWaveMap,
            collectables = CollectableMap,
            enemyGroups = EnemyGroupMap,
            globalEvents = GlobalEventMap,
            playerUpgrades = PlayerUpgradeMap,
            rarities = RarityMap,
            missionContainers = MissionContainerMap,
            encounters = EncounterMap,
            quips = QuipMap,
            crafting = CraftingEntry,
            dialogue = DialogueEntry,
            upgradePresets = UpgradePresetMap,
            levelMilestones = BuildLevelMilestones(),
            patternInfusion = new PatternInfusion
            {
                UnlockLevel = 13,
                ResourceID = "antimass",
                BaseCost = 10,
                CostPerCellDifference = 5,
                MinCost = 1,
                CostPerRarityLevel = 2,
                CostFormula = "max(BaseCost + cellCountDifference * CostPerCellDifference, MinCost) + targetRarity * CostPerRarityLevel"
            },
            directiveManager = UnityEngine.Resources.FindObjectsOfTypeAll<DirectiveManager>()
                .Where(d => d != null).Select(BuildDirectiveManager).FirstOrDefault(),
            gridProfiles = GridProfileMap,
            planets = PlanetMap,
            statusEffectGlobals = new StatusEffectGlobals(),
            formulas = new Formulas
            {
                XPCurves = new[]
                {
                    new Formulas.XPCurve { Context = "character_1_to_10", Description = "Player character XP, levels 1..10. Formula: 16.8 * (level+1)^2.1 + 23.", LevelOffset = 0, Coefficient = 16.8f, Power = 2.1f, Add = 23 },
                    new Formulas.XPCurve { Context = "character_11_plus", Description = "Player character XP, levels 11+. Formula uses (level-10) as input.", LevelOffset = 10, Coefficient = 60f, Power = 1.5f, Add = 2137 },
                    new Formulas.XPCurve { Context = "gear_1_to_5", Description = "Per-gear XP, levels 1..5. Formula: 50 * (level+1)^1.9 + 0.", LevelOffset = 0, Coefficient = 50f, Power = 1.9f, Add = 0 },
                    new Formulas.XPCurve { Context = "gear_6_plus", Description = "Per-gear XP, levels 6+. Formula uses (level-5) as input.", LevelOffset = 5, Coefficient = 33f, Power = 1.8f, Add = 1064 }
                }
            },
            achievements = new[] {
                "4_MODS", "ABOMINATION", "FULL_CREW", "HORNET_MELEE", "KILL_PLAYER",
                "LASSO_PLAYER", "PLAYER_HOOP", "SAXITO", "SWINGER", "UPGRADE",
                "UPGRADE_OPTIMIZER", "cranius"
            },
            steamStats = new[] {
                "HOOPS", "MissionsCompleted", "MissionsFailed", "SAXITO", "appliedStatusEffect",
                "damageDealt", "damageTaken", "deaths", "enemiesKilled", "epicUpgradesCollected",
                "exoticUpgradesCollected", "friendlyFireDamageDealt", "healingDealt", "level",
                "maxCompletedThreat", "otherPlayerDeaths", "pipesPushed", "playersKilled",
                "rareUpgradesCollected", "redactedUpgradesCollected", "roachardHiCount",
                "saxitosPunched", "selfDeaths", "standardUpgradesCollected", "targetsKilled",
                "upgradesCollected"
            }
        };
        var settings = new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore,
            FloatFormatHandling = FloatFormatHandling.String,
            Error = (_, args) =>
            {
                Logger.LogError($"Serialization error at {args.ErrorContext.Path}: {args.ErrorContext.Error.Message}");
                args.ErrorContext.Handled = true;
            }
        };

        // Serialize to a JToken tree first so we can post-process to inject
        // `Plain*` siblings next to any rich-text Name/Description/Title fields.
        var jtokenWriter = new Newtonsoft.Json.Linq.JTokenWriter();
        JsonSerializer.Create(settings).Serialize(jtokenWriter, dump);
        var root = jtokenWriter.Token;
        InjectPlainTextSiblings(root);
        writer.Write(root.ToString(Formatting.None));

        Logger.LogInfo($"Wrote {outputPath}");
    }
}
