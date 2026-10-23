using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MycopunkDumper;

// Probe-only: every static field of every Assembly-CSharp type → static-fields.txt. Reading a
// static runs the type's cctor if it hasn't run yet — acceptable in a probe run, the game exits after.
internal static class ProbeStatics
{
    private const BindingFlags Statics = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;

    internal static bool IsGameAssembly(Assembly a) => a.GetName().Name.StartsWith("Assembly-CSharp");

    internal static Type[] GameTypes() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(IsGameAssembly)
            .SelectMany(a => { try { return a.GetTypes(); } catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null).ToArray(); } })
            .ToArray();

    // Returns the non-scalar values as extra roots for ProbeReachability.
    public static List<KeyValuePair<string, object>> Write(string root)
    {
        var roots = new List<KeyValuePair<string, object>>();
        var body = new StringBuilder();
        int types = 0, fields = 0, failed = 0;
        foreach (var t in GameTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            if (t.FullName == null || t.ContainsGenericParameters || t.FullName.Contains('<')) continue;
            FieldInfo[] fs;
            try { fs = t.GetFields(Statics); } catch { continue; }
            var lines = new List<string>();
            foreach (var f in fs.OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                if (typeof(Delegate).IsAssignableFrom(f.FieldType)) continue;
                // `<Instance>k__BackingField` → `Instance` (auto-property singletons)
                var name = f.Name.StartsWith("<") && f.Name.Contains(">k__") ? f.Name.Substring(1, f.Name.IndexOf('>') - 1) : f.Name;
                var mods = f.IsLiteral ? "const " : f.IsInitOnly ? "readonly " : "";
                string rendered;
                try
                {
                    var v = f.IsLiteral ? f.GetRawConstantValue() : f.GetValue(null);
                    rendered = Summarize(v);
                    if (!f.IsLiteral && v != null && !(v is string) && !v.GetType().IsPrimitive && !v.GetType().IsEnum)
                        roots.Add(new KeyValuePair<string, object>($"static:{t.FullName}.{name}", v));
                }
                catch (Exception e)
                {
                    failed++;
                    rendered = $"<read failed: {(e.InnerException ?? e).GetType().Name}: {(e.InnerException ?? e).Message}>";
                }
                lines.Add($"  {mods}{name} : {Probe.Pretty(f.FieldType)} = {Clip(rendered, 600)}");
            }
            if (lines.Count == 0) continue;
            types++;
            fields += lines.Count;
            body.AppendLine($"## {t.FullName}");
            foreach (var l in lines) body.AppendLine(l);
        }
        var header = $"# Static fields of Assembly-CSharp types after game init: {fields} fields on {types} types, {failed} reads failed\n" +
                     "# format: `  [const |readonly ]name : Type = value`; collections as `<Type count=N> [first items]`\n";
        File.WriteAllText(Path.Combine(root, "static-fields.txt"), header + body);
        return roots;
    }

    private static string Summarize(object v)
    {
        if (v == null || v is string || v is UnityEngine.Object || !(v is IEnumerable e)) return Probe.RenderValue(v);
        var items = new List<string>();
        var n = 0;
        if (v is IDictionary d)
        {
            foreach (DictionaryEntry kv in d)
                if (n++ < 5) items.Add($"{Item(kv.Key)}: {Item(kv.Value)}");
        }
        else
        {
            foreach (var x in e)
            {
                if (n++ < 5) items.Add(Item(x));
                if (n > 1_000_000) break;
            }
        }
        var more = n > 5 ? $", … +{n - 5}" : "";
        return $"<{Probe.Pretty(v.GetType())} count={n}> [{string.Join(", ", items)}{more}]";
    }

    private static string Item(object x)
    {
        try { return Clip(Probe.RenderValue(x), 100); }
        catch (Exception ex) { return $"<err: {ex.Message}>"; }
    }

    internal static string Clip(string s, int n) => s.Length <= n ? s : s.Substring(0, n) + "…";
}
