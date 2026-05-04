using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using Newtonsoft.Json;

namespace MycopunkDumper;

/// <summary>
/// Runtime introspection helper. Triggered by presence of a flag file
/// (<c>$MYCOPUNK_DIR/probe.flag</c>) — does nothing otherwise. Writes
/// structured output to <c>$MYCOPUNK_DIR/probe/</c>:
///
///   types.md         — public + non-public members of every probed type
///   catalogs.txt     — counts of all ScriptableObject catalogs found in memory
///   subclasses.txt   — observed concrete subclasses for polymorphic bases
///   samples/&lt;Type&gt;.json — one JsonUtility-serialized sample per type
///
/// Invoke <see cref="Probe.Run"/> from <c>Plugin.Start()</c>. Add new probe
/// targets by editing the <c>Run</c> method below — the helpers handle
/// reflection and output formatting.
/// </summary>
internal static class Probe
{
    private const BindingFlags AllInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags AllStatic   = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags Declared    = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static string _root;
    private static StringBuilder _types;
    private static StringBuilder _catalogs;
    private static StringBuilder _subclasses;

    public static void Run()
    {
        var flag = Path.Combine(Paths.GameRootPath, "probe.flag");
        if (!File.Exists(flag)) return;

        _root = Path.Combine(Paths.GameRootPath, "probe");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "samples"));

        _types = new StringBuilder();
        _catalogs = new StringBuilder();
        _subclasses = new StringBuilder();

        try
        {
            // ----- Types to introspect -----
            // Add new probe targets here.
            DumpType<Mission>();
            DumpType<MissionContainer>();
            DumpType<WorldRegion>();
            DumpType<DirectiveData>();
            DumpType<DirectiveInstance>();
            DumpType<PlayerResource>();
            DumpType<global::Upgrade>();
            DumpType<UpgradeInstance>();
            DumpType<UpgradeProperty>();
            DumpType<DirectiveProperty>();
            DumpType<LevelUnlock>();
            DumpType<GearInfo>();
            DumpType<Character>();
            DumpType<HexMap>();
            DumpType<StatData>();

            // ----- ScriptableObject catalogs -----
            // Lists every distinct SO type that has live instances.
            EnumerateCatalogs();

            // ----- Polymorphic subclass discovery + sampling -----
            DiscoverSubclassesFromUpgrades<UpgradeProperty>("UpgradeProperty",
                up => up?.Properties.properties ?? Array.Empty<UpgradeProperty>());
            DiscoverSubclasses<DirectiveProperty>("DirectiveProperty",
                UnityEngine.Resources.FindObjectsOfTypeAll<DirectiveData>()
                    .SelectMany(d => d?.Directives.properties ?? Array.Empty<DirectiveProperty>()));
            DiscoverSubclasses<LevelUnlock>("LevelUnlock",
                UnityEngine.Resources.FindObjectsOfTypeAll<DirectiveData>()
                    .SelectMany(d => d?.AdditionalRewards.Properties ?? Array.Empty<LevelUnlock>())
                    .Concat(Global.Instance == null ? Array.Empty<LevelUnlock>() :
                        Global.Instance.AllGear.Concat<IUpgradable>(Global.Instance.Characters)
                            .SelectMany(g => g?.Info?.LevelUnlocks.Properties ?? Array.Empty<LevelUnlock>()))
                    .Concat(Global.Instance?.Missions == null ? Array.Empty<LevelUnlock>() :
                        Global.Instance.Missions.SelectMany(m =>
                            (m?.AdditionalRewards.Properties ?? Array.Empty<LevelUnlock>())
                            .Concat((LevelUnlock[])(m?.GetType().GetField("RepeatRewards", AllInstance)?.GetValue(m) is LevelUnlockList rr ? rr.Properties : null) ?? Array.Empty<LevelUnlock>())))
                    .Concat(UnityEngine.Resources.FindObjectsOfTypeAll<MissionContainer>()
                        .SelectMany(mc => mc == null ? Array.Empty<LevelUnlock>() : new LevelUnlock[][] {
                            (LevelUnlock[])(mc.GetType().GetField("rewards", AllInstance)?.GetValue(mc) is LevelUnlockList r1 ? r1.Properties : null) ?? Array.Empty<LevelUnlock>(),
                            (LevelUnlock[])(mc.GetType().GetField("repeatableRewards", AllInstance)?.GetValue(mc) is LevelUnlockList r2 ? r2.Properties : null) ?? Array.Empty<LevelUnlock>()
                        }.SelectMany(x => x))));

            DiscoverSubclasses<Mission>("Mission",
                Global.Instance?.Missions ?? Array.Empty<Mission>());

            SampleScriptableObjects<Mission>();
            SampleScriptableObjects<WorldRegion>();
            SampleScriptableObjects<DirectiveData>();
            SampleScriptableObjects<PlayerResource>();
            SampleScriptableObjects<MissionContainer>();

            // High-value wiki/calculator candidates surfaced by the catalog scan.
            // Resolve types by name (some live in nested namespaces); skip silently if missing.
            SampleByName("EnemyClass");
            SampleByName("StatusEffectData");
            SampleByName("PlayerStackData");
            SampleByName("ThreatData");
            SampleByName("MissionModifierGeneric");
            SampleByName("MissionModifierWaveModifier");
            SampleByName("UpgradePreset");
            SampleByName("GenericCustomWave");
            SampleByName("SurfaceData");
            SampleByName("LootPool");
            SampleByName("DialogueExchange");
            SampleByName("AmalgamationGlobalEvent");
            SampleByName("GlobalEvent");
            SampleByName("PatternPath");
            SampleByName("Dude");
            SampleByName("EnemyWaveObjective");
            SampleByName("ObjectiveBase");
            SampleByName("Voiceline");
            SampleByName("CharacterAnimationKey");
            SampleByName("Player");
            SampleByName("AuthItem");
            SampleByName("ThreatModifierProfile");
            SampleByName("CollectableProfile");
            SampleByName("EnemyClassGroup");
            SampleByName("CustomEnemySpawner");
            SampleByName("WeatherMissionEvent");
            SampleByName("AmalgamationMissionEvent");
            DumpTypeByName("Pigeon.Movement.Player");
            DumpTypeByName("Player");
            SampleByName("SkillTreeUpgradeUI");
            try
            {
                var t = FindTypeByName("SkillTreeUpgradeUI");
                if (t != null)
                {
                    var arr = UnityEngine.Resources.FindObjectsOfTypeAll(t);
                    _types.AppendLine($"## SkillTreeUpgradeUI count: {arr.Length}");
                }
            }
            catch (Exception e) { _types.AppendLine($"skill tree count failed: {e.Message}"); }

            DumpTypeByName("EnemyClass");
            DumpTypeByName("StatusEffectData");
            DumpTypeByName("PlayerStackData");
            DumpTypeByName("ThreatData");
            DumpTypeByName("MissionModifierGeneric");
            DumpTypeByName("MissionModifierWaveModifier");
            DumpTypeByName("UpgradePreset");
            DumpTypeByName("GenericCustomWave");
            DumpTypeByName("SurfaceData");
            DumpTypeByName("LootPool");
            DumpTypeByName("DialogueExchange");
            DumpTypeByName("MissionModifier");
            DumpTypeByName("CustomWave");
            DumpTypeByName("Config"); // nested in EnemyClass — may not be found by name alone
            // Try to find EnemyClass+Config
            var ecType = FindTypeByName("EnemyClass");
            if (ecType != null)
            {
                var cfgField = ecType.GetField("config");
                if (cfgField != null) DumpType(cfgField.FieldType);
            }

            // Per-gear UnlockDirective binding probe
            ProbeUnlockDirectives();

            // Find GearInfo SOs not reachable via Global.Instance.AllGear/Characters
            try
            {
                var dumped = new HashSet<GearInfo>();
                if (Global.Instance != null)
                {
                    foreach (var u in Global.Instance.AllGear.Concat<IUpgradable>(Global.Instance.Characters))
                        if (u?.Info != null) dumped.Add(u.Info);
                }
                var all = UnityEngine.Resources.FindObjectsOfTypeAll<GearInfo>();
                var unreachable = all.Where(g => g != null && !dumped.Contains(g)).ToList();
                var sb = new StringBuilder();
                sb.AppendLine($"# GearInfo not reachable via Global.AllGear/Characters: {unreachable.Count} of {all.Length} total");
                sb.AppendLine();
                foreach (var g in unreachable)
                {
                    try { sb.AppendLine($"- ID={g.ID} APIName={g.APIName} Name={g.Name} TypeName={g.TypeName} ({g.GetType().Name}) assetName={g.name}"); }
                    catch (Exception ex) { sb.AppendLine($"- {g.GetType().Name} assetName={g.name} (read failed: {ex.Message})"); }
                }
                File.WriteAllText(Path.Combine(_root, "unreachable-gearinfo.txt"), sb.ToString());
            }
            catch (Exception e) { _types.AppendLine($"unreachable-gear probe failed: {e.Message}"); }

            // Dump JsonUtility of one IUpgradable per concrete subclass so we can see what
            // [SerializeField] fields are exposed on the prefab MonoBehaviour itself.
            try
            {
                if (Global.Instance != null)
                {
                    var sb = new StringBuilder();
                    var seenGearTypes = new HashSet<Type>();
                    foreach (var u in Global.Instance.AllGear.Concat<IUpgradable>(Global.Instance.Characters))
                    {
                        if (u == null) continue;
                        var t = u.GetType();
                        if (!seenGearTypes.Add(t)) continue;
                        sb.AppendLine($"### {t.FullName}  ({u.Info?.APIName})");
                        try { sb.AppendLine($"  json: {UnityEngine.JsonUtility.ToJson(u)}"); } catch (Exception e) { sb.AppendLine($"  json failed: {e.Message}"); }
                        sb.AppendLine();
                    }
                    File.WriteAllText(Path.Combine(_root, "gear-prefab-data.txt"), sb.ToString());
                }
            }
            catch (Exception e) { _types.AppendLine($"gear prefab data probe failed: {e.Message}"); }

            DumpTypeByName("Dude");
            SampleByName("Dude");

            // Look for: localization SOs, attributes on UpgradeProperty subclasses, methods
            // that might return per-property display names.
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# UpgradeProperty subclass introspection (looking for display-name source)");
                sb.AppendLine();

                // 1) Any Localization-related types and counts
                sb.AppendLine("## Localization-related catalogs");
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try { types = asm.GetTypes(); } catch { continue; }
                    foreach (var t in types)
                    {
                        if (t.IsAbstract || !typeof(UnityEngine.Object).IsAssignableFrom(t)) continue;
                        var n = t.FullName ?? "";
                        if (!(n.Contains("Localiz") || n.Contains("StringTable") || n.Contains("L10n") || n.Contains("I18n") || n.Contains("Lang") || n.Contains("TextAsset"))) continue;
                        try { var arr = UnityEngine.Resources.FindObjectsOfTypeAll(t); if (arr.Length > 0) sb.AppendLine($"  {arr.Length,5}  {n}"); } catch { }
                    }
                }
                sb.AppendLine();

                // 2) Walk UpgradeProperty subclasses, list methods that return a string and aren't inherited from base
                sb.AppendLine("## UpgradeProperty subclasses with string-returning methods");
                var ups = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
                    .Where(t => !t.IsAbstract && typeof(UpgradeProperty).IsAssignableFrom(t))
                    .ToArray();
                sb.AppendLine($"  total subclasses: {ups.Length}");
                var stringMethods = new HashSet<string>();
                foreach (var t in ups)
                {
                    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        if (m.ReturnType == typeof(string) && m.GetParameters().Length == 0)
                            stringMethods.Add($"{t.Name}.{m.Name}");
                    }
                }
                foreach (var s in stringMethods.OrderBy(x => x).Take(40)) sb.AppendLine($"  {s}");
                if (stringMethods.Count > 40) sb.AppendLine($"  … +{stringMethods.Count - 40} more");
                sb.AppendLine();

                // 3) Base class methods/attrs that subclasses override
                sb.AppendLine("## UpgradeProperty BASE: virtual string-returning methods (inherited surface)");
                foreach (var m in typeof(UpgradeProperty).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (m.IsVirtual && m.ReturnType == typeof(string))
                        sb.AppendLine($"  virtual {m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})");
                }
                sb.AppendLine();

                // 4) Custom attributes on subclasses
                sb.AppendLine("## Custom attributes on a sample of UpgradeProperty subclasses");
                int counter = 0;
                foreach (var t in ups.Take(15))
                {
                    var attrs = t.GetCustomAttributes(false);
                    if (attrs.Length == 0) continue;
                    sb.Append($"  {t.Name}: ");
                    foreach (var a in attrs) sb.Append(a.GetType().Name + " ");
                    sb.AppendLine();
                    counter++;
                }
                if (counter == 0) sb.AppendLine("  (none have custom attributes on the class itself)");
                sb.AppendLine();

                // 5) Look for things like a "TooltipName"/"DisplayName" string field on subclasses
                sb.AppendLine("## String FIELDS on UpgradeProperty subclasses (any per-property literal that might be a label)");
                var fieldNamesSeen = new SortedDictionary<string, int>();
                foreach (var t in ups)
                {
                    foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        if (f.FieldType == typeof(string))
                            fieldNamesSeen[f.Name] = fieldNamesSeen.GetValueOrDefault(f.Name, 0) + 1;
                    }
                }
                foreach (var kv in fieldNamesSeen.OrderByDescending(x => x.Value)) sb.AppendLine($"  {kv.Value,4}  field {kv.Key}");
                sb.AppendLine();

                // 6) Inspect StatData.ToStringBuilder via reflection: what string fields does it use?
                sb.AppendLine("## How tooltips are constructed: try invoking ModifyProperties on a sample");
                var sampleProp = Global.Instance?.AllGear
                    .Where(g => g?.Info?.Upgrades != null)
                    .SelectMany(g => g.Info.Upgrades)
                    .Where(u => u?.Properties.properties != null && u.Properties.properties.Length > 0)
                    .Select(u => new { u, p = u.Properties.properties[0] })
                    .FirstOrDefault();
                if (sampleProp != null)
                {
                    sb.AppendLine($"  sample upgrade: {sampleProp.u.Name} → property {sampleProp.p.GetType().Name}");
                    // Try ModifyProperties via reflection
                    var mp = sampleProp.p.GetType().GetMethod("ModifyProperties", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (mp != null) sb.AppendLine($"  ModifyProperties found at {mp.DeclaringType.Name} (params: {string.Join(", ", mp.GetParameters().Select(x => x.ParameterType.Name))})");
                }

                // 7) Actually invoke ModifyProperties on a few real (upgrade, upgradable) pairs and dump the result
                sb.AppendLine();
                sb.AppendLine("## Live tooltip capture: ModifyProperties output for sample upgrades");
                var rand = new Pigeon.Math.Random(0);
                var captured = 0;
                foreach (var g in (Global.Instance?.AllGear ?? Array.Empty<IUpgradable>()).Concat(Global.Instance?.Characters ?? Array.Empty<Character>()))
                {
                    if (g?.Info?.Upgrades == null) continue;
                    foreach (var up in g.Info.Upgrades)
                    {
                        if (up?.Properties.properties == null || up.Properties.properties.Length == 0) continue;
                        foreach (var prop in up.Properties.properties)
                        {
                            if (prop == null) continue;
                            try
                            {
                                string str = "";
                                var inst = new UpgradeInstance(up, g);
                                prop.ModifyProperties(ref str, rand, g, inst);
                                if (!string.IsNullOrEmpty(str))
                                {
                                    sb.AppendLine($"  [{up.Name} on {g.Info.APIName}] property={prop.GetType().Name}");
                                    sb.AppendLine($"    ModifyProperties → {str.Replace("\n", "\\n")}");
                                    if (++captured >= 30) break;
                                }
                            }
                            catch (Exception e) { sb.AppendLine($"    failed: {e.Message}"); }
                        }
                        if (captured >= 30) break;
                    }
                    if (captured >= 30) break;
                }
                if (captured == 0) sb.AppendLine("  (every property's ModifyProperties returned an empty string — they only contribute via GetStatData)");
                sb.AppendLine();

                File.WriteAllText(Path.Combine(_root, "property-naming-investigation.txt"), sb.ToString());
            }
            catch (Exception e) { _types.AppendLine($"naming probe failed: {e.Message}\n{e.StackTrace}"); }

            // Voiceline / dialogue catalog enumeration
            try
            {
                var voicelineTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
                    .Where(t => !t.IsAbstract && typeof(UnityEngine.ScriptableObject).IsAssignableFrom(t)
                                && (t.Name.Contains("Voice") || t.Name.Contains("Dialogue")))
                    .ToArray();
                var sb = new StringBuilder();
                sb.AppendLine("# Voiceline / dialogue SO catalogs");
                foreach (var t in voicelineTypes)
                {
                    var arr = UnityEngine.Resources.FindObjectsOfTypeAll(t);
                    sb.AppendLine($"  {arr.Length,4}  {t.FullName}");
                }
                File.WriteAllText(Path.Combine(_root, "voiceline-catalogs.txt"), sb.ToString());
            }
            catch (Exception e) { _types.AppendLine($"voiceline probe failed: {e.Message}"); }

            // ===== Wiki/calculator candidates surfaced by 5 parallel scouts =====
            // Each block is independent and try/catched so a failure in one doesn't
            // abort the rest. Outputs go into named files so the human can read each
            // domain in isolation.

            // -- 1. Voicelines / dialogue --
            // DialogueExchange already sampled above; promote to a member dump and
            // add the master Quip catalog (Global.Instance.Quips) which we'd
            // missed entirely. Both are SO-clean — no Unity-native fields, so
            // standard Newtonsoft will serialize them fine when we wire DTOs.
            DumpTypeByName("DialogueExchange");
            DumpTypeByName("DialogueExchange+Line");
            SampleByName("QuipData");
            DumpTypeByName("QuipData");
            SafeDumpFile("dialogue-event-chances.txt", () =>
            {
                var s = new StringBuilder();
                s.AppendLine("# Global.DialogueEventChances (per-trigger probability, indexed by Mathf.Log2((int)Trigger))");
                if (Global.Instance != null)
                {
                    var f = typeof(Global).GetField("DialogueEventChances", AllInstance);
                    if (f?.GetValue(Global.Instance) is Array arr)
                        for (int i = 0; i < arr.Length; i++) s.AppendLine($"  [{i}] = {arr.GetValue(i)}");
                    else s.AppendLine("  (field not found)");
                }
                return s.ToString();
            });

            // -- 2. Encounters / spawn rules / biomes --
            // IEncounter is THE missing layer — controls "what spawns into a procedural
            // mission" with per-region masks. 11 concrete subclasses + biome SOs.
            DumpTypeByName("IEncounter");
            foreach (var n in new[] {
                "MeatspawnEncounter", "PipelineEncounter", "CraterEncounter", "UpgradeDroneEncounter",
                "SpawnObjectsEncounter", "SpawnPropEncounter", "ArcGatesEncounter", "ScorchedTransponderEncounter",
                "RelayTowersEncounter", "WaterEncounter", "FlyingRingsEncounter", "LostSaxitosEncounter"
            })
            {
                DumpTypeByName(n);
                SampleByName(n);
            }
            // Biome catalog hanging off WorldRegion.WorldProfiles
            foreach (var n in new[] { "WorldProfile", "Biome", "BiomeGroup", "BiomeGroupList", "TerrainProfile" })
            {
                DumpTypeByName(n);
                SampleByName(n);
            }
            // CustomWave subclass discovery — already partially probed; force polymorphic walk
            foreach (var n in new[] { "AddAttachmentsWave", "SpecialFirstEnemyWave", "PixieWave", "ClassListEnemyWave" })
                DumpTypeByName(n);
            // CustomEnemySpawner subclasses (boss/special spawn shapes)
            foreach (var n in new[] { "EnemyModifierSpawner", "NeckyWormyEnemySpawner", "ScrapBossSpawner", "ShieldArrayBossSpawner", "AmalgamationSpawner" })
            {
                DumpTypeByName(n);
                SampleByName(n);
            }
            SafeDumpFile("encounters-list.txt", () =>
            {
                // Walk Global.Instance.encounters reflectively — it's UnityEngine.Object[]
                // mixing SO-impls and prefabs. Resolve to (index, type-name, asset-name)
                // so a downstream DTO can map index→encounter and cross-ref WorldRegion.
                var s = new StringBuilder();
                s.AppendLine("# Global.encounters[] (the IEncounter array driving procedural mission spawns)");
                if (Global.Instance != null)
                {
                    var f = typeof(Global).GetField("encounters", AllInstance);
                    if (f?.GetValue(Global.Instance) is Array arr)
                    {
                        s.AppendLine($"## count: {arr.Length}");
                        for (int i = 0; i < arr.Length; i++)
                        {
                            var o = arr.GetValue(i) as UnityEngine.Object;
                            if (o == null) { s.AppendLine($"  [{i}] <null>"); continue; }
                            s.AppendLine($"  [{i}] {o.GetType().FullName}  asset='{o.name}'  iid={o.GetInstanceID()}");
                        }
                    }
                    else s.AppendLine("  (encounters field not found)");
                }
                return s.ToString();
            });

            // -- 3. Combat math (mostly NOT a catalog; one juicy enrichment + Global scaling) --
            // Weakpoints are MonoBehaviours on enemy prefabs — closest thing to a
            // "headshot table". Walk every loaded one and record (root, part, multiplier).
            SafeDumpFile("weakpoints.txt", () =>
            {
                var s = new StringBuilder();
                s.AppendLine("# Weakpoints (precision-shot multipliers, authored per-collider on enemy prefabs)");
                var wpType = FindTypeByName("Weakpoint");
                if (wpType == null) { s.AppendLine("  (Weakpoint type not found)"); return s.ToString(); }
                var arr = UnityEngine.Resources.FindObjectsOfTypeAll(wpType);
                s.AppendLine($"## count: {arr.Length}");
                var multField = wpType.GetField("damageMultiplier", AllInstance) ?? wpType.GetField("DamageMultiplier", AllInstance);
                var multProp = wpType.GetProperty("DamageMultiplier", AllInstance);
                foreach (UnityEngine.Component wp in arr)
                {
                    try
                    {
                        var rootName = wp.transform.root != null ? wp.transform.root.name : "<no root>";
                        var partName = wp.gameObject.name;
                        var mul = multField?.GetValue(wp) ?? multProp?.GetValue(wp);
                        s.AppendLine($"  {rootName} › {partName}  ×{mul}");
                    }
                    catch (Exception e) { s.AppendLine($"  <wp read failed: {e.Message}>"); }
                }
                return s.ToString();
            });
            SafeDumpFile("global-scaling.txt", () =>
            {
                // ThreatModifier<PlayerModifier<float>> etc. — the actual difficulty
                // math complementing the 7 ThreatData SOs. Read via reflection so we
                // don't need to bind to ThreatModifier<>'s generic shape at compile time.
                var s = new StringBuilder();
                s.AppendLine("# Global scaling/difficulty fields (ThreatModifier / IntensityModifier / PlayerModifier)");
                if (Global.Instance == null) { s.AppendLine("  (Global.Instance is null)"); return s.ToString(); }
                foreach (var name in new[] {
                    "EnemyHealthScaling", "EnemyDamageScaling", "EnemyMoveSpeedScaling", "EnemyAttackSpeedScaling",
                    "EnemyWaveSize", "EnemyWaveInterval", "IntensityWaveInterval", "IntensityScripAndXPMultiplier",
                    "ResourceAmountMultiplier", "IntensityMinOpenWaveSlots", "LookForPartIntervalScaling",
                    "BaseScripForMissionCompletion", "BaseScripForSideObjective", "BaseXPForSideObjective",
                    "BaseResourcesForSideObjective", "BaseMissionXP", "SurvivalBonusScrip", "CombatResourceSpawnChance"
                })
                {
                    var f = typeof(Global).GetField(name, AllInstance);
                    if (f == null) { s.AppendLine($"  {name}: <not found>"); continue; }
                    try { s.AppendLine($"  {name} ({Pretty(f.FieldType)}) = {NewtonSafe(f.GetValue(Global.Instance))}"); }
                    catch (Exception e)
                    {
                        try { s.AppendLine($"  {name} ({Pretty(f.FieldType)}) = {f.GetValue(Global.Instance)}"); }
                        catch (Exception e2) { s.AppendLine($"  {name}: <read failed: {e.Message} / {e2.Message}>"); }
                    }
                }
                return s.ToString();
            });

            // -- 4. Tutorial / codex / weekly progression --
            // The big find: WeeklyOvertimeAssignment carries the weekly modifier
            // rotation + first-vs-repeat reward asymmetry — neither in the dump anywhere.
            DumpTypeByName("MissionContainer");
            DumpTypeByName("WeeklyOvertimeAssignment");
            SampleByName("WeeklyOvertimeAssignment");
            SampleByName("MissionContainer");
            DumpTypeByName("WeekData");
            DumpTypeByName("CorpContestGlobalEvent");
            DumpTypeByName("CorpData");
            SampleByName("CorpContestGlobalEvent");
            SafeDumpFile("mission-containers.txt", () =>
            {
                var s = new StringBuilder();
                s.AppendLine("# Global.MissionContainers — weekly/event mission variants with their reward overrides");
                if (Global.Instance != null)
                {
                    var f = typeof(Global).GetField("MissionContainers", AllInstance);
                    if (f?.GetValue(Global.Instance) is Array arr)
                    {
                        s.AppendLine($"## count: {arr.Length}");
                        for (int i = 0; i < arr.Length; i++)
                        {
                            var mc = arr.GetValue(i) as UnityEngine.Object;
                            if (mc == null) { s.AppendLine($"  [{i}] <null>"); continue; }
                            s.AppendLine($"  [{i}] {mc.GetType().FullName}  asset='{mc.name}'");
                            try { s.AppendLine($"      json: {UnityEngine.JsonUtility.ToJson(mc)}"); } catch (Exception e) { s.AppendLine($"      json failed: {e.Message}"); }
                        }
                    }
                    else s.AppendLine("  (MissionContainers field not found)");
                }
                return s.ToString();
            });

            // -- 5. Economy: rarity table, crafting prices, master quip catalog --
            DumpTypeByName("RarityData");
            DumpTypeByName("ResourceCost");
            DumpTypeByName("CraftingWindow");
            SampleByName("CraftingWindow");
            DumpTypeByName("OuroBuyWindow");
            SampleByName("OuroBuyWindow");
            DumpTypeByName("OuroChoiceWindow");
            SampleByName("OuroChoiceWindow");
            DumpTypeByName("HexMapProfile");
            SampleByName("HexMapProfile");
            SafeDumpFile("rarities.txt", () =>
            {
                // RarityData[] on Global.Instance.Rarities — the most-asked-for
                // pricing table. Reflective field walk (no Newtonsoft) because
                // Newtonsoft on Unity-managed structs has hit native crashes here.
                var s = new StringBuilder();
                s.AppendLine("# Global.Rarities[] — master rarity/cost table");
                if (Global.Instance == null) { s.AppendLine("  (Global.Instance is null)"); return s.ToString(); }
                var f = typeof(Global).GetField("Rarities", AllInstance);
                if (f?.GetValue(Global.Instance) is Array arr)
                {
                    s.AppendLine($"## count: {arr.Length}");
                    for (int i = 0; i < arr.Length; i++)
                    {
                        s.AppendLine($"### [{i}]");
                        try
                        {
                            var elt = arr.GetValue(i);
                            if (elt == null) { s.AppendLine("  <null>"); continue; }
                            DumpStructFields(s, elt, "  ");
                        }
                        catch (Exception e) { s.AppendLine($"  <element read failed: {e.Message}>"); }
                    }
                }
                else s.AppendLine("  (Rarities field not found)");
                return s.ToString();
            });
            SafeDumpFile("crafting-prices.txt", () =>
            {
                // CraftingWindow's [SerializeField] private cost arrays ARE the
                // master crafting price table.
                var s = new StringBuilder();
                s.AppendLine("# CraftingWindow [SerializeField] cost-related fields");
                var t = FindTypeByName("CraftingWindow");
                if (t == null) { s.AppendLine("  (CraftingWindow type not found)"); return s.ToString(); }
                var arr = UnityEngine.Resources.FindObjectsOfTypeAll(t);
                s.AppendLine($"## prefab instances: {arr.Length}");
                var resourceCostType = FindTypeByName("ResourceCost");
                foreach (var cw in arr)
                {
                    s.AppendLine($"### {cw.name}");
                    foreach (var fld in t.GetFields(AllInstance))
                    {
                        var nameL = fld.Name.ToLowerInvariant();
                        if (!nameL.Contains("cost") && !nameL.Contains("level")) continue;
                        // Skip UI text widgets — their field name happens to contain "cost"
                        // but they're labels, not data.
                        if (typeof(UnityEngine.Object).IsAssignableFrom(fld.FieldType)) continue;
                        // Only descend into ResourceCost arrays/values; skip everything else.
                        if (resourceCostType != null && fld.FieldType == resourceCostType.MakeArrayType())
                        {
                            try
                            {
                                var v = fld.GetValue(cw) as Array;
                                s.AppendLine($"    {fld.Name} ({Pretty(fld.FieldType)})");
                                if (v == null) { s.AppendLine("      <null>"); continue; }
                                for (int i = 0; i < v.Length; i++)
                                {
                                    s.AppendLine($"      [{i}]");
                                    DumpStructFields(s, v.GetValue(i), "        ");
                                }
                            }
                            catch (Exception e) { s.AppendLine($"    {fld.Name}: <read failed: {e.Message}>"); }
                        }
                        else if (fld.FieldType.IsPrimitive)
                        {
                            try { s.AppendLine($"    {fld.Name} ({Pretty(fld.FieldType)}) = {fld.GetValue(cw)}"); }
                            catch (Exception e) { s.AppendLine($"    {fld.Name}: <read failed: {e.Message}>"); }
                        }
                    }
                }
                return s.ToString();
            });
            SafeDumpFile("quips-master.txt", () =>
            {
                // Global.Quips — master player-emote catalog. We currently only
                // dump per-gear quips; Global.Quips is the canonical list.
                var s = new StringBuilder();
                s.AppendLine("# Global.Quips — master quip/emote catalog");
                if (Global.Instance == null) { s.AppendLine("  (Global.Instance is null)"); return s.ToString(); }
                var f = typeof(Global).GetField("Quips", AllInstance);
                if (f?.GetValue(Global.Instance) is Array arr)
                {
                    s.AppendLine($"## count: {arr.Length}");
                    for (int i = 0; i < arr.Length; i++)
                    {
                        var q = arr.GetValue(i) as UnityEngine.Object;
                        if (q == null) { s.AppendLine($"  [{i}] <null>"); continue; }
                        try { s.AppendLine($"  [{i}] {UnityEngine.JsonUtility.ToJson(q)}"); } catch (Exception e) { s.AppendLine($"  [{i}] json failed: {e.Message}"); }
                    }
                }
                else s.AppendLine("  (Quips field not found)");
                return s.ToString();
            });

            // ===== end of scout-driven probes =====

            File.WriteAllText(Path.Combine(_root, "types.md"), _types.ToString());
            File.WriteAllText(Path.Combine(_root, "catalogs.txt"), _catalogs.ToString());
            File.WriteAllText(Path.Combine(_root, "subclasses.txt"), _subclasses.ToString());

            Plugin.Log.LogInfo($"Probe written to {_root}");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Probe.Run failed: {e}");
        }
    }

    private static void DumpType<T>() => DumpType(typeof(T));

    private static void DumpType(Type t)
    {
        _types.AppendLine($"## {t.FullName}");
        _types.AppendLine();
        var fields = t.GetFields(Declared).OrderBy(f => f.IsStatic).ThenBy(f => f.Name).ToList();
        var props = t.GetProperties(Declared).OrderBy(p => p.Name).ToList();
        foreach (var f in fields)
            _types.AppendLine($"- field {(f.IsStatic ? "static " : "")}`{f.Name}` : `{Pretty(f.FieldType)}`");
        foreach (var p in props)
            _types.AppendLine($"- prop `{p.Name}` : `{Pretty(p.PropertyType)}`");

        // Inherited public surface
        if (t.BaseType != null && t.BaseType != typeof(object) && t.BaseType != typeof(UnityEngine.ScriptableObject) && t.BaseType != typeof(UnityEngine.MonoBehaviour))
            _types.AppendLine($"- *(inherits from `{Pretty(t.BaseType)}`)*");
        _types.AppendLine();
    }

    private static void EnumerateCatalogs()
    {
        var soType = typeof(UnityEngine.ScriptableObject);
        var seen = new SortedDictionary<string, int>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); } catch { continue; }
            foreach (var t in types)
            {
                if (t.IsAbstract || !soType.IsAssignableFrom(t)) continue;
                try
                {
                    var arr = UnityEngine.Resources.FindObjectsOfTypeAll(t);
                    if (arr.Length > 0) seen[t.FullName] = arr.Length;
                }
                catch { }
            }
        }

        _catalogs.AppendLine($"# ScriptableObject catalogs ({seen.Count} non-empty types)");
        _catalogs.AppendLine();
        foreach (var kv in seen.OrderByDescending(x => x.Value).ThenBy(x => x.Key))
            _catalogs.AppendLine($"{kv.Value,6}  {kv.Key}");
    }

    private static void DiscoverSubclasses<TBase>(string label, IEnumerable<TBase> instances) where TBase : class
    {
        var seen = new Dictionary<Type, TBase>();
        foreach (var inst in instances)
        {
            if (inst == null) continue;
            seen.TryAdd(inst.GetType(), inst);
        }
        WriteSubclasses(label, seen);
    }

    private static void DiscoverSubclassesFromUpgrades<TBase>(string label, Func<global::Upgrade, IEnumerable<TBase>> picker) where TBase : class
    {
        if (Global.Instance == null) { WriteSubclasses(label, new Dictionary<Type, TBase>()); return; }
        var instances = new List<TBase>();
        foreach (var g in Global.Instance.AllGear.Concat<IUpgradable>(Global.Instance.Characters))
        {
            if (g?.Info?.Upgrades == null) continue;
            foreach (var up in g.Info.Upgrades)
            {
                foreach (var item in picker(up)) instances.Add(item);
            }
        }
        DiscoverSubclasses(label, instances);
    }

    private static void WriteSubclasses<TBase>(string label, Dictionary<Type, TBase> samples) where TBase : class
    {
        _subclasses.AppendLine($"## {label} subclasses observed: {samples.Count}");
        _subclasses.AppendLine();
        foreach (var kv in samples.OrderBy(x => x.Key.Name))
        {
            _subclasses.AppendLine($"### {kv.Key.FullName}");
            foreach (var f in kv.Key.GetFields(Declared))
                _subclasses.AppendLine($"  field {(f.IsStatic ? "static " : "")}`{f.Name}` : `{Pretty(f.FieldType)}`");
            foreach (var p in kv.Key.GetProperties(Declared))
                _subclasses.AppendLine($"  prop `{p.Name}` : `{Pretty(p.PropertyType)}`");
            try { _subclasses.AppendLine($"  json: `{UnityEngine.JsonUtility.ToJson(kv.Value)}`"); } catch (Exception e) { _subclasses.AppendLine($"  json failed: {e.Message}"); }
            // Reflection-based field dump (catches non-[SerializeField] fields JsonUtility skips)
            foreach (var f in kv.Key.GetFields(AllInstance))
            {
                try
                {
                    var v = f.GetValue(kv.Value);
                    var rendered = v switch
                    {
                        null => "null",
                        Array a => $"<array len={a.Length}>",
                        ICollection c => $"<collection len={c.Count}>",
                        _ => v.ToString()
                    };
                    _subclasses.AppendLine($"  refl `{f.Name}` = {rendered}");
                }
                catch (Exception e) { _subclasses.AppendLine($"  refl `{f.Name}` failed: {e.Message}"); }
            }
            _subclasses.AppendLine();
        }
    }

    private static Type FindTypeByName(string name) =>
        AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
            .FirstOrDefault(t => t.Name == name);

    private static void DumpTypeByName(string name)
    {
        try
        {
            var t = FindTypeByName(name);
            if (t != null) DumpType(t);
            else _types.AppendLine($"## {name} *(type not found)*\n");
        }
        catch (Exception e) { _types.AppendLine($"## {name} *(failed: {e.Message})*\n"); }
    }

    private static void SampleByName(string name)
    {
        Type t;
        try { t = FindTypeByName(name); } catch { return; }
        if (t == null) return;
        if (!typeof(UnityEngine.Object).IsAssignableFrom(t)) return;
        if (t.IsAbstract || t.IsGenericTypeDefinition) return;
        UnityEngine.Object[] arr;
        try { arr = UnityEngine.Resources.FindObjectsOfTypeAll(t); } catch { return; }
        if (arr.Length == 0) return;
        var path = Path.Combine(_root, "samples", t.Name + ".json");
        var sb = new StringBuilder();
        sb.Append("{ \"catalogTotal\": ").Append(arr.Length).AppendLine(", \"samples\": [");
        var n = System.Math.Min(arr.Length, 5);
        for (int i = 0; i < n; i++)
        {
            try { sb.Append("  ").Append(UnityEngine.JsonUtility.ToJson(arr[i])); }
            catch (Exception e) { sb.Append("  \"failed: ").Append(e.Message).Append('\"'); }
            if (i < n - 1) sb.AppendLine(",");
            else sb.AppendLine();
        }
        sb.AppendLine("] }");
        File.WriteAllText(path, sb.ToString());
    }

    private static void SampleScriptableObjects<T>() where T : UnityEngine.Object
    {
        var arr = UnityEngine.Resources.FindObjectsOfTypeAll<T>();
        if (arr.Length == 0) return;
        var path = Path.Combine(_root, "samples", typeof(T).Name + ".json");
        var sb = new StringBuilder();
        sb.AppendLine("[");
        for (int i = 0; i < System.Math.Min(arr.Length, 5); i++)
        {
            try { sb.Append("  ").Append(UnityEngine.JsonUtility.ToJson(arr[i])); }
            catch (Exception e) { sb.Append("  \"failed: ").Append(e.Message).Append('\"'); }
            if (i < System.Math.Min(arr.Length, 5) - 1) sb.AppendLine(",");
            else sb.AppendLine();
        }
        sb.AppendLine("]");
        File.WriteAllText(path, sb.ToString());
    }

    private static void ProbeUnlockDirectives()
    {
        if (Global.Instance == null) return;
        var sb = new StringBuilder();
        sb.AppendLine("# UnlockDirective bindings");
        sb.AppendLine();
        foreach (var u in Global.Instance.AllGear.Concat<IUpgradable>(Global.Instance.Characters))
        {
            DirectiveInstance di = null;
            try
            {
                if (u.HasUnlockDirective(out di) && di != null)
                {
                    var dataId = di.Data != null ? di.Data.ID.ToString() : "<null Data>";
                    sb.AppendLine($"- {u.Info?.APIName ?? u.GetType().Name}: directive id={dataId} tier={di.Tier} seed={di.Seed} active={di.IsActive} complete={di.IsComplete}");
                }
            }
            catch (Exception e) { sb.AppendLine($"- {u.Info?.APIName ?? u.GetType().Name}: ERROR {e.Message}"); }
        }
        File.WriteAllText(Path.Combine(_root, "unlock-directives.txt"), sb.ToString());
    }

    /// <summary>
    /// Run <paramref name="produce"/> in a try/catch and write the result to
    /// <c>$probeRoot/<paramref name="filename"/></c>. On error, write the exception
    /// instead — keeps one failing probe block from aborting the whole probe pass.
    /// </summary>
    private static void SafeDumpFile(string filename, Func<string> produce)
    {
        try { File.WriteAllText(Path.Combine(_root, filename), produce()); }
        catch (Exception e)
        {
            try { File.WriteAllText(Path.Combine(_root, filename), $"<probe failed: {e.Message}\n{e.StackTrace}>"); }
            catch { /* give up */ }
            _types?.AppendLine($"## probe `{filename}` failed: {e.Message}");
        }
    }

    /// <summary>
    /// One level of reflective field-walk. For each field: write a `name (Type) = value`
    /// line. Unity Object refs render as `instanceID=N name="..."` instead of being
    /// recursed into (which would risk a native crash on certain Unity types).
    /// Primitive arrays render their length; arrays of Unity Objects render names.
    /// </summary>
    private static void DumpStructFields(StringBuilder s, object value, string indent)
    {
        if (value == null) { s.Append(indent).AppendLine("<null>"); return; }
        var t = value.GetType();
        foreach (var f in t.GetFields(AllInstance).OrderBy(f => f.Name))
        {
            object v;
            try { v = f.GetValue(value); }
            catch (Exception e) { s.Append(indent).Append(f.Name).Append(" = <read failed: ").Append(e.Message).AppendLine(">"); continue; }
            s.Append(indent).Append(f.Name).Append(" (").Append(Pretty(f.FieldType)).Append(") = ").AppendLine(RenderValue(v));
        }
    }

    private static string RenderValue(object v)
    {
        if (v == null) return "null";
        if (v is UnityEngine.Object uo) return uo == null ? "<destroyed>" : $"<{uo.GetType().Name} iid={uo.GetInstanceID()} name=\"{uo.name}\">";
        if (v is string str) return "\"" + str.Replace("\"", "\\\"") + "\"";
        if (v is Array a)
        {
            // Render up to 10 entries inline, then a length indicator.
            var sb = new StringBuilder("[");
            for (int i = 0; i < System.Math.Min(a.Length, 10); i++)
            {
                if (i > 0) sb.Append(", ");
                try { sb.Append(RenderValue(a.GetValue(i))); }
                catch (Exception e) { sb.Append("<err: ").Append(e.Message).Append('>'); }
            }
            if (a.Length > 10) sb.Append(", … +").Append(a.Length - 10).Append(" more");
            sb.Append(']');
            return sb.ToString();
        }
        var t = v.GetType();
        // Primitive-ish — just use ToString
        if (t.IsPrimitive || t.IsEnum || v is decimal) return v.ToString();
        // Treat known small Unity structs leniently
        if (t.Namespace == "UnityEngine") return v.ToString();
        // Custom struct: render fields inline (one level deep)
        if (t.IsValueType)
        {
            var sb = new StringBuilder(t.Name).Append('{');
            var first = true;
            foreach (var f in t.GetFields(AllInstance).OrderBy(f => f.Name))
            {
                if (!first) sb.Append(", ");
                first = false;
                try { sb.Append(f.Name).Append('=').Append(RenderValueShallow(f.GetValue(v))); }
                catch (Exception e) { sb.Append(f.Name).Append("=<err: ").Append(e.Message).Append('>'); }
            }
            return sb.Append('}').ToString();
        }
        return v.ToString();
    }

    private static string RenderValueShallow(object v)
    {
        if (v == null) return "null";
        if (v is UnityEngine.Object uo) return uo == null ? "<destroyed>" : $"<{uo.GetType().Name} \"{uo.name}\">";
        if (v is string str) return "\"" + str + "\"";
        if (v is Array a) return $"<array len={a.Length}>";
        return v.ToString();
    }

    /// <summary>
    /// Stops Newtonsoft from descending into Unity's GameObject/Component graph.
    /// Without this the probe hangs serializing e.g. a TextMeshProUGUI reference,
    /// which transitively walks Component→GameObject→Transform→parent→… forever.
    /// </summary>
    private class UnityObjectStubConverter : JsonConverter
    {
        public override bool CanConvert(Type t) => typeof(UnityEngine.Object).IsAssignableFrom(t);
        public override bool CanRead => false;
        public override void WriteJson(JsonWriter w, object v, JsonSerializer s)
        {
            if (v is UnityEngine.Object o)
                w.WriteRawValue($"{{\"instanceID\":{o.GetInstanceID()},\"name\":{JsonConvert.SerializeObject(o.name)}}}");
            else
                w.WriteNull();
        }
        public override object ReadJson(JsonReader r, Type t, object e, JsonSerializer s) => throw new NotImplementedException();
    }

    private static readonly JsonSerializerSettings _newtonSafe = new JsonSerializerSettings
    {
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        NullValueHandling = NullValueHandling.Ignore,
        FloatFormatHandling = FloatFormatHandling.String,
        Error = (_, args) => args.ErrorContext.Handled = true,
        MaxDepth = 6,
        Converters = { new UnityObjectStubConverter() },
    };

    /// <summary>
    /// Newtonsoft serialize with cycles ignored. Use this for structs / generic
    /// types that Unity's JsonUtility refuses (it returns "{}" for those).
    /// </summary>
    private static string NewtonSafe(object value)
    {
        try { return JsonConvert.SerializeObject(value, Formatting.None, _newtonSafe); }
        catch (Exception e) { return $"<newton failed: {e.Message}>"; }
    }

    private static string Pretty(Type t)
    {
        if (t.IsGenericType)
        {
            var name = t.Name;
            var tick = name.IndexOf('`');
            if (tick >= 0) name = name.Substring(0, tick);
            return name + "<" + string.Join(", ", t.GetGenericArguments().Select(Pretty)) + ">";
        }
        if (t.IsArray) return Pretty(t.GetElementType()) + "[]";
        return t.Name;
    }
}
