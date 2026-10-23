using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace IlScan;

// Scan 1: localization keys flowing into TextBlocks lookups (and wrappers that forward a string param to one).
sealed class LocScan
{
    sealed record PatRec(string pattern, int matches, SortedSet<string> sites);
    sealed record CallRec(string Callee, string CalleeShort, Val[] Args, Site Site, string CallerFullName);

    // callee FullName -> index of the key argument (in the call's arg list, `this` included)
    readonly Dictionary<string, int> sinks = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> wrapperOf = new(StringComparer.Ordinal);
    readonly List<CallRec> calls = [];

    public readonly SortedDictionary<string, SortedSet<string>> Literal = new(StringComparer.Ordinal);
    public readonly SortedDictionary<string, SortedSet<string>> EnumDerived = new(StringComparer.Ordinal);
    public readonly SortedDictionary<string, SortedSet<string>> Patterns = new(StringComparer.Ordinal);
    public readonly SortedDictionary<string, SortedSet<string>> Dynamic = new(StringComparer.Ordinal);
    public readonly Dictionary<string, int> LiteralByOwner = new(StringComparer.Ordinal);

    public LocScan(ModuleDefinition module)
    {
        var tb = module.GetType("TextBlocks");
        if (tb == null) return;
        foreach (var m in tb.Methods)
        {
            var p = m.Parameters.FirstOrDefault(x => x.Name == "id" && x.ParameterType.MetadataType == MetadataType.String);
            if (p != null && m.IsPublic) sinks[m.FullName] = p.Index + (m.HasThis ? 1 : 0);
        }
    }

    public void Visit(Flow flow, Site site)
    {
        foreach (var ins in flow.Reached)
        {
            if (ins.OpCode.Code is not (Code.Call or Code.Callvirt or Code.Newobj)) continue;
            var mr = (MethodReference)ins.Operand;
            if (!mr.Parameters.Any(p => p.ParameterType.MetadataType == MetadataType.String)) continue;
            calls.Add(new CallRec(mr.FullName, Interp.ShortName(mr), flow.Popped(ins), site, flow.Method.FullName));
        }
    }

    void Resolve()
    {
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var c in calls)
            {
                if (!sinks.TryGetValue(c.Callee, out int idx) || idx >= c.Args.Length) continue;
                if (c.Args[idx] is ArgV { Type.MetadataType: MetadataType.String } a && !sinks.ContainsKey(c.CallerFullName))
                {
                    sinks[c.CallerFullName] = a.Index;
                    wrapperOf[c.CallerFullName] = $"{c.Site} (param {a.Name}) -> {c.CalleeShort}";
                    changed = true;
                }
            }
        }
        foreach (var c in calls)
        {
            if (!sinks.TryGetValue(c.Callee, out int idx) || idx >= c.Args.Length) continue;
            var site = $"{c.Site}";
            switch (c.Args[idx])
            {
                case Strs s:
                    foreach (var k in s.Set)
                    {
                        var bucket = k.Contains("{*}") ? Patterns : s.Via != null ? EnumDerived : Literal;
                        Add(bucket, k, site);
                        if (bucket == Literal) LiteralByOwner[c.Site.Owner] = LiteralByOwner.GetValueOrDefault(c.Site.Owner) + 1;
                    }
                    if (s.Partial) Add(Dynamic, "(mixed constant/dynamic)", site);
                    break;
                case ArgV:
                    break; // wrapper forwarding, already registered
                case Unk u:
                    Add(Dynamic, u.Src ?? "?", site);
                    break;
                default:
                    Add(Dynamic, Interp.Render(c.Args[idx]), site);
                    break;
            }
        }
    }

    static void Add(SortedDictionary<string, SortedSet<string>> d, string k, string site)
    {
        if (!d.TryGetValue(k, out var s)) d[k] = s = new SortedSet<string>(StringComparer.Ordinal);
        s.Add(site);
    }

    static string Sites(SortedSet<string> s, int max = 4) =>
        string.Join(", ", s.Take(max)) + (s.Count > max ? $", +{s.Count - max} more" : "");

    static Regex PatternRegex(string p) =>
        new("^" + string.Join(".*", p.Split("{*}").Select(Regex.Escape)) + "$", RegexOptions.CultureInvariant);

    public List<string> Write(string dir, DumpIndex dump)
    {
        Resolve();
        var refd = new HashSet<string>(Literal.Keys, StringComparer.Ordinal);
        refd.UnionWith(EnumDerived.Keys);

        var keysTsv = new List<string> { "# key\tkind\tin_dump\tsites" };
        string InDump(string k) => !dump.Loaded ? "?" : dump.LocKeys.Contains(k) ? "yes"
            : dump.LocKeysLower.TryGetValue(k.Trim().ToLowerInvariant(), out var real) ? $"near:{Interp.Quote(real)}" : "no";
        foreach (var (k, s) in Literal) keysTsv.Add($"{k}\tliteral\t{InDump(k)}\t{Sites(s)}");
        foreach (var (k, s) in EnumDerived) keysTsv.Add($"{k}\tenum\t{InDump(k)}\t{Sites(s)}");
        File.WriteAllLines(Path.Combine(dir, "loc-keys.tsv"), keysTsv);

        var missing = Literal.Where(kv => dump.Loaded && !dump.LocKeys.Contains(kv.Key)).ToList();
        var missingEnum = EnumDerived.Where(kv => dump.Loaded && !dump.LocKeys.Contains(kv.Key)).ToList();
        var ml = new List<string>
        {
            "# Literal keys passed to TextBlocks lookups (or wrappers) that are absent from data.json `localization`.",
            "# near:\"x\" = a key differing only by case/whitespace exists.",
        };
        foreach (var (k, s) in missing) ml.Add($"{Interp.Quote(k)}\t{InDump(k)}\t{Sites(s, 8)}");
        ml.Add("");
        ml.Add($"# Enum-member names reaching a lookup via Enum.ToString() that are absent ({missingEnum.Count}); usually only some members are ever used.");
        foreach (var (k, s) in missingEnum) ml.Add($"{Interp.Quote(k)}\t{InDump(k)}\t{Sites(s, 3)}");
        File.WriteAllLines(Path.Combine(dir, "loc-missing.txt"), ml);

        var pl = new List<string> { "# pattern\tdump_matches\tsample_matches\tsites   ({*} = runtime value)" };
        var patternHits = new HashSet<string>(StringComparer.Ordinal);
        var patternJson = new List<PatRec>();
        foreach (var (p, s) in Patterns)
        {
            var lit = p.Replace("{*}", "");
            var matches = new List<string>();
            if (dump.Loaded && lit.Length >= 3)
            {
                var re = PatternRegex(p);
                matches = dump.LocKeys.Where(k => re.IsMatch(k)).Order(StringComparer.Ordinal).ToList();
                patternHits.UnionWith(matches);
            }
            pl.Add($"{Interp.Quote(p)}\t{(lit.Length >= 3 ? matches.Count : "-")}\t{string.Join(",", matches.Take(5))}\t{Sites(s)}");
            patternJson.Add(new PatRec(p, matches.Count, s));
        }
        File.WriteAllLines(Path.Combine(dir, "loc-patterns.txt"), pl);

        var dl = new List<string> { "# Non-constant key expressions passed to lookups, i.e. keys that come from data (fields/getters) — grouped by source." };
        foreach (var (src, s) in Dynamic.OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Key, StringComparer.Ordinal))
            dl.Add($"{s.Count,4}  {src}\t{Sites(s, 3)}");
        File.WriteAllLines(Path.Combine(dir, "loc-dynamic.txt"), dl);

        var wl = new List<string> { "# Methods that forward a string parameter into a TextBlocks lookup (treated as additional key sinks)." };
        wl.AddRange(wrapperOf.Values.Order(StringComparer.Ordinal));
        File.WriteAllLines(Path.Combine(dir, "loc-wrappers.txt"), wl);

        var orphan = new List<string>();
        var assetRef = new List<string>();
        if (dump.Loaded)
        {
            foreach (var k in dump.LocKeys.Order(StringComparer.Ordinal))
            {
                if (refd.Contains(k) || patternHits.Contains(k)) continue;
                (dump.AssetStrings.Contains(k) || dump.AssetStrings.Contains(k.Trim()) ? assetRef : orphan).Add(k);
            }
            var ul = new List<string> { $"# localization keys never referenced as a code literal/pattern AND never appearing as a value elsewhere in data.json ({orphan.Count})" };
            ul.AddRange(orphan.Select(Interp.Quote));
            ul.Add("");
            ul.Add($"# localization keys not referenced in code but present as asset data elsewhere in data.json ({assetRef.Count})");
            ul.AddRange(assetRef.Select(Interp.Quote));
            File.WriteAllLines(Path.Combine(dir, "loc-unreferenced.txt"), ul);
        }

        Program.WriteJson(Path.Combine(dir, "loc.json"), new
        {
            literal = Literal,
            enumDerived = EnumDerived,
            patterns = patternJson,
            dynamicSources = Dynamic,
            wrappers = wrapperOf.Values.Order(StringComparer.Ordinal),
            missingFromDump = missing.Select(kv => kv.Key),
            unreferencedOrphans = orphan,
            unreferencedAssetBacked = assetRef,
        });

        var r = new List<string>
        {
            "[localization]  loc-keys.tsv loc-missing.txt loc-patterns.txt loc-dynamic.txt loc-wrappers.txt loc-unreferenced.txt loc.json",
            $"  sinks: {sinks.Count} ({wrapperOf.Count} wrappers discovered)",
            $"  literal keys in code: {Literal.Count}, enum-derived: {EnumDerived.Count}, patterns: {Patterns.Count}, dynamic sources: {Dynamic.Count}",
        };
        if (dump.Loaded)
        {
            r.Add($"  dump localization keys: {dump.LocKeys.Count}");
            r.Add($"  literal keys MISSING from dump: {missing.Count}" + (missing.Count > 0 ? "  e.g. " + string.Join(", ", missing.Take(10).Select(kv => kv.Key)) : ""));
            r.Add($"  dump keys unreferenced by code: {orphan.Count + assetRef.Count} ({orphan.Count} orphans, {assetRef.Count} asset-backed)");
            var topPat = patternJson.OrderByDescending(p => p.matches).Take(5).Select(p => $"{p.pattern} ({p.matches})");
            r.Add("  top patterns: " + string.Join(", ", topPat));
        }
        return r;
    }
}
