using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using Mono.Cecil;

namespace IlScan;

static class Program
{
    public static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    static int Main(string[] args)
    {
        var sw = Stopwatch.StartNew();
        var game = Environment.GetEnvironmentVariable("MYCOPUNK_DIR")
                   ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/Steam/steamapps/common/Mycopunk");
        string dll = Path.Combine(game, "Mycopunk_Data/Managed/Assembly-CSharp.dll");
        string data = Path.Combine(game, "data.json");
        string src = "src";
        string outDir = "il-scan-out";
        for (int i = 0; i + 1 < args.Length; i += 2)
        {
            switch (args[i])
            {
                case "--dll": dll = args[i + 1]; break;
                case "--data": data = args[i + 1]; break;
                case "--src": src = args[i + 1]; break;
                case "--out": outDir = args[i + 1]; break;
                default:
                    Console.Error.WriteLine($"unknown arg {args[i]}; usage: il-scan [--dll X] [--data data.json] [--src src/] [--out dir]");
                    return 2;
            }
        }
        if (!File.Exists(dll))
        {
            Console.Error.WriteLine($"ERROR: {dll} not found");
            return 1;
        }
        Directory.CreateDirectory(outDir);

        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(dll)!);
        var asm = AssemblyDefinition.ReadAssembly(dll, new ReaderParameters { AssemblyResolver = resolver });
        var module = asm.MainModule;

        var loc = new LocScan(module);
        var consts = new ConstScan();
        var statics = new StaticScan();
        var switches = new SwitchScan();

        int methods = 0, failed = 0;
        foreach (var type in module.GetTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            if (type.Name == "<PrivateImplementationDetails>" || type.Name == "<Module>") continue;
            statics.VisitType(type);
            foreach (var m in type.Methods)
            {
                if (!m.HasBody) continue;
                methods++;
                Flow flow;
                try { flow = Interp.Run(m); }
                catch (Exception e)
                {
                    failed++;
                    if (failed <= 5) Console.Error.WriteLine($"warn: {m.FullName}: {e.GetType().Name} {e.Message}");
                    continue;
                }
                var site = Sites.Of(m);
                loc.Visit(flow, site);
                consts.Visit(flow, site);
                statics.Visit(flow, site);
                switches.Visit(flow, site);
            }
        }
        var tScan = sw.Elapsed;

        var dump = DumpIndex.Load(data);
        var srcIdents = SrcIndex.Load(src);

        var summary = new List<string>
        {
            "il-scan summary",
            $"  dll:     {dll}",
            $"  data:    {(dump.Loaded ? data : "(not found; loc cross-reference skipped)")}",
            $"  methods: {methods} analysed, {failed} failed",
            "",
        };
        summary.AddRange(loc.Write(outDir, dump));
        summary.Add("");
        summary.AddRange(consts.Write(outDir));
        summary.Add("");
        summary.AddRange(statics.Write(outDir));
        summary.Add("");
        summary.AddRange(switches.Write(outDir));
        summary.Add("");
        summary.AddRange(SrcGaps.Write(outDir, srcIdents, loc, consts, statics, switches));
        summary.Add("");
        summary.Add($"elapsed: scan {tScan.TotalSeconds:F1}s, total {sw.Elapsed.TotalSeconds:F1}s");
        File.WriteAllLines(Path.Combine(outDir, "summary.txt"), summary);
        Console.WriteLine(string.Join('\n', summary));
        return 0;
    }

    public static void WriteJson(string path, object o) => File.WriteAllText(path, JsonSerializer.Serialize(o, Json) + "\n");
}

readonly record struct Site(string Owner, string Method)
{
    public override string ToString() => Owner + "." + Method;
}

static class Sites
{
    // Folds compiler-generated closures/iterators back onto the user-facing type + method.
    public static Site Of(MethodDefinition m)
    {
        var t = m.DeclaringType;
        string name = Unmangle(m.Name) ?? m.Name;
        while (t.IsNested && t.Name.StartsWith('<'))
        {
            if (!t.Name.StartsWith("<>") && Unmangle(t.Name) is { } outer) name = outer;
            t = t.DeclaringType;
        }
        return new Site(t.FullName.Replace('/', '.'), name);
    }

    public static string OwnerOf(TypeDefinition t)
    {
        while (t.IsNested && t.Name.StartsWith('<')) t = t.DeclaringType;
        return t.FullName.Replace('/', '.');
    }

    public static string? Unmangle(string n)
    {
        if (!n.StartsWith('<')) return null;
        int end = n.IndexOf('>');
        return end > 1 ? n[1..end] : null;
    }
}

sealed class DumpIndex
{
    public bool Loaded;
    public readonly HashSet<string> LocKeys = new(StringComparer.Ordinal);
    public readonly Dictionary<string, string> LocKeysLower = new(StringComparer.Ordinal);
    // every string value / property name outside `localization` — i.e. keys referenced by asset data
    public readonly HashSet<string> AssetStrings = new(StringComparer.Ordinal);

    public static DumpIndex Load(string path)
    {
        var d = new DumpIndex();
        if (!File.Exists(path)) return d;
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        foreach (var sec in doc.RootElement.EnumerateObject())
        {
            if (sec.Name == "localization")
            {
                foreach (var k in sec.Value.EnumerateObject())
                {
                    d.LocKeys.Add(k.Name);
                    d.LocKeysLower.TryAdd(k.Name.Trim().ToLowerInvariant(), k.Name);
                }
                continue;
            }
            d.Collect(sec.Value);
        }
        d.Loaded = true;
        return d;
    }

    void Collect(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                {
                    AssetStrings.Add(p.Name);
                    Collect(p.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var x in e.EnumerateArray()) Collect(x);
                break;
            case JsonValueKind.String:
                AssetStrings.Add(e.GetString()!);
                break;
        }
    }
}

static class SrcIndex
{
    // lowercased identifiers, so `AntimassByFloor` in a DTO matches the game's `antimassByFloor`
    public static HashSet<string> Load(string dir)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (!Directory.Exists(dir)) return set;
        var word = new System.Text.RegularExpressions.Regex(@"[A-Za-z_][A-Za-z0-9_]*");
        foreach (var f in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            if (f.Contains("/obj/") || f.Contains("/bin/")) continue;
            foreach (System.Text.RegularExpressions.Match m in word.Matches(File.ReadAllText(f))) set.Add(m.Value.ToLowerInvariant());
        }
        return set;
    }
}
