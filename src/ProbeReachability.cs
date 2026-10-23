using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using BepInEx;
using Newtonsoft.Json;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MycopunkDumper;

// Probe-only: BFS over serialized fields from Global, every game ScriptableObject and the static
// roots, descending into prefabs (components + children) of referenced GameObjects/components.
// Writes reachability.txt and unemitted-fields.txt. Must run post-dump: "covered" means the
// object's instanceID is in NativeConverter.InstanceRefs, or it is inline data (no UnityEngine.Object
// hop) of such an object — i.e. what JsonUtility/the builders actually emit.
internal static class ProbeReachability
{
    private const int MaxNodes = 3_000_000;
    private const int MaxDepth = 48;
    private const int MaxElements = 20_000;
    private const double MaxSeconds = 480;
    private const BindingFlags Inst = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private sealed class RefEq : IEqualityComparer<object>
    {
        public new bool Equals(object a, object b) => ReferenceEquals(a, b);
        public int GetHashCode(object o) => RuntimeHelpers.GetHashCode(o);
    }

    private sealed class Stat
    {
        public int N, Catalogued, Inline, Referenced;
        public int CoveredExample = -1;
        public readonly List<int> Uncovered = new();
    }

    private struct Work
    {
        public object Obj;
        public int Node, Depth;
        public bool OwnerCat;
    }

    private static readonly Dictionary<Type, FieldInfo[]> _fieldCache = new();
    private static readonly Dictionary<Type, bool> _leafCache = new();
    private static readonly HashSet<object> _visited = new(new RefEq());
    private static readonly Queue<Work> _queue = new();
    private static readonly List<int> _parent = new();
    private static readonly List<string> _label = new();
    private static readonly Dictionary<Type, Stat> _stats = new();
    private static readonly Dictionary<Type, int> _leaves = new();
    private static readonly Dictionary<FieldInfo, int[]> _fieldStats = new();
    private static HashSet<int> _jsonIds = new();
    private static HashSet<string> _jsonKeys = new();
    private static int _errors;
    private static string _truncated;

    public static IEnumerator Run(string root, List<KeyValuePair<string, object>> staticRoots)
    {
        var sw = Stopwatch.StartNew();
        int rootCount = 0;
        try
        {
            ReadDumpKeys(Path.Combine(Paths.GameRootPath, "data.json"));
            if (Global.Instance != null) { Visit(Global.Instance, -1, "Global", false, 0); rootCount++; }
            foreach (var so in Resources.FindObjectsOfTypeAll<ScriptableObject>())
            {
                if (so == null || !IsGame(so.GetType())) continue;
                Visit(so, -1, so.GetType().Name, false, 0);
                rootCount++;
            }
            foreach (var kv in staticRoots) { Visit(kv.Value, -1, kv.Key, false, 0); rootCount++; }
        }
        catch (Exception e) { Plugin.Log.LogError($"ProbeReachability roots failed: {e}"); }
        Plugin.Log.LogInfo($"ProbeReachability: {rootCount} roots, walking…");

        while (_queue.Count > 0)
        {
            if (_parent.Count >= MaxNodes) { _truncated = $"node cap {MaxNodes}"; break; }
            if (sw.Elapsed.TotalSeconds > MaxSeconds) { _truncated = $"time cap {MaxSeconds}s"; break; }
            for (var k = 0; k < 20_000 && _queue.Count > 0; k++)
            {
                try { Process(_queue.Dequeue()); }
                catch { _errors++; }
            }
            yield return null;
        }
        Plugin.Log.LogInfo($"ProbeReachability: {_parent.Count} nodes in {sw.Elapsed.TotalSeconds:F1}s{(_truncated != null ? " (truncated: " + _truncated + ")" : "")}");

        try { WriteReport(root, rootCount, sw.Elapsed.TotalSeconds); }
        catch (Exception e) { Plugin.Log.LogError($"ProbeReachability report failed: {e}"); }
    }

    private static bool IsGame(Type t) => ProbeStatics.IsGameAssembly(t.Assembly);

    private static void Visit(object v, int parent, string label, bool ownerCat, int depth)
    {
        if (v == null || depth > MaxDepth) return;
        if (v is Object uo)
        {
            if (uo == null || !_visited.Add(uo)) return;
            var ut = uo.GetType();
            if (!(uo is GameObject) && !IsGame(ut)) { _leaves[ut] = _leaves.GetValueOrDefault(ut) + 1; return; }
            if (uo is GameObject) _leaves[ut] = _leaves.GetValueOrDefault(ut) + 1;
            if (!label.StartsWith("/") && !label.StartsWith(":")) label += $"'{uo.name}'";
            var node = AddNode(parent, label);
            var cat = NativeConverter.InstanceRefs.ContainsKey(uo.GetInstanceID());
            if (!(uo is GameObject))
            {
                var s = StatFor(ut);
                if (cat) { s.Catalogued++; if (s.CoveredExample < 0) s.CoveredExample = node; }
                else
                {
                    if (_jsonIds.Contains(uo.GetInstanceID())) s.Referenced++;
                    if (s.Uncovered.Count < 3) s.Uncovered.Add(node);
                }
            }
            _queue.Enqueue(new Work { Obj = uo, Node = node, Depth = depth, OwnerCat = cat });
            return;
        }

        var t = v.GetType();
        if (v is IEnumerable e && !(v is string) && IsCollection(t))
        {
            if (LeafElements(t)) return;
            var i = 0;
            if (v is IDictionary d)
            {
                foreach (DictionaryEntry kv in d)
                {
                    if (i++ >= MaxElements) break;
                    var key = kv.Key is string || kv.Key is Enum || kv.Key.GetType().IsPrimitive ? ProbeStatics.Clip(kv.Key.ToString(), 40) : "#" + (i - 1);
                    Visit(kv.Key, parent, $"{label}{{key {key}}}", ownerCat, depth + 1);
                    Visit(kv.Value, parent, $"{label}[{key}]", ownerCat, depth + 1);
                }
            }
            else
            {
                foreach (var x in e)
                {
                    if (i >= MaxElements) break;
                    Visit(x, parent, $"{label}[{i++}]", ownerCat, depth + 1);
                }
            }
            return;
        }

        if (!IsGame(t) || typeof(Delegate).IsAssignableFrom(t)) return;
        if (!t.IsValueType && !_visited.Add(v)) return;
        var n = AddNode(parent, label);
        var st = StatFor(t);
        if (ownerCat) { st.Inline++; if (st.CoveredExample < 0) st.CoveredExample = n; }
        else if (st.Uncovered.Count < 3) st.Uncovered.Add(n);
        _queue.Enqueue(new Work { Obj = v, Node = n, Depth = depth, OwnerCat = ownerCat });
    }

    private static void Process(Work w)
    {
        if (w.Obj is GameObject go)
        {
            if (go == null) return;
            foreach (var c in go.GetComponents<Component>())
                if (c != null) Visit(c, w.Node, ":" + c.GetType().Name, false, w.Depth + 1);
            var tr = go.transform;
            for (var i = 0; i < tr.childCount; i++)
            {
                var child = tr.GetChild(i).gameObject;
                Visit(child, w.Node, "/" + child.name, false, w.Depth + 1);
            }
            return;
        }

        foreach (var f in Fields(w.Obj.GetType()))
        {
            object v;
            try { v = f.GetValue(w.Obj); }
            catch { _errors++; continue; }
            if (w.OwnerCat)
            {
                if (!_fieldStats.TryGetValue(f, out var fs)) _fieldStats[f] = fs = new int[2];
                fs[0]++;
                if (!IsDefault(v)) fs[1]++;
            }
            if (v == null || v is string || v.GetType().IsPrimitive || v.GetType().IsEnum) continue;
            Visit(v, w.Node, "." + FieldLabel(f), w.OwnerCat, w.Depth + 1);
        }
        // A component reference reaches its whole prefab.
        if (w.Obj is Component comp && comp != null)
            Visit(comp.gameObject, w.Node, "/" + comp.gameObject.name, false, w.Depth + 1);
    }

    private static string FieldLabel(FieldInfo f) =>
        f.Name.StartsWith("<") && f.Name.Contains(">k__") ? f.Name.Substring(1, f.Name.IndexOf('>') - 1) : f.Name;

    // Readable name for generic instantiations (FullName embeds assembly-qualified args).
    private static string TypeName(Type t)
    {
        if (!t.IsGenericType) return t.FullName;
        var prefix = t.DeclaringType != null ? TypeName(t.DeclaringType) + "+" : t.Namespace != null ? t.Namespace + "." : "";
        return prefix + Probe.Pretty(t);
    }

    // Audio/event plumbing — always "unemitted", never interesting.
    private static bool NoiseField(Type t) =>
        typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(t) || (t.Namespace?.StartsWith("AK.Wwise") ?? false);

    private static int AddNode(int parent, string label)
    {
        _parent.Add(parent);
        _label.Add(label);
        return _parent.Count - 1;
    }

    private static Stat StatFor(Type t)
    {
        if (!_stats.TryGetValue(t, out var s)) _stats[t] = s = new Stat();
        s.N++;
        return s;
    }

    private static FieldInfo[] Fields(Type t)
    {
        if (_fieldCache.TryGetValue(t, out var cached)) return cached;
        var list = new List<FieldInfo>();
        for (var x = t; x != null && IsGame(x); x = x.BaseType)
        {
            foreach (var f in x.GetFields(Inst))
            {
                var ft = f.FieldType;
                if (typeof(Delegate).IsAssignableFrom(ft) || ft.IsPointer || ft == typeof(IntPtr) || typeof(MemberInfo).IsAssignableFrom(ft)) continue;
                if (f.IsPublic || f.IsDefined(typeof(SerializeField), false) || f.IsDefined(typeof(SerializeReference), false))
                    list.Add(f);
            }
        }
        return _fieldCache[t] = list.ToArray();
    }

    private static bool IsCollection(Type t) =>
        t.IsArray || typeof(IDictionary).IsAssignableFrom(t) || typeof(IList).IsAssignableFrom(t)
        || (t.IsGenericType && t.Namespace == "System.Collections.Generic");

    private static bool LeafElements(Type t)
    {
        if (_leafCache.TryGetValue(t, out var b)) return b;
        var args = t.IsArray ? new[] { t.GetElementType() } : t.IsGenericType ? t.GetGenericArguments() : null;
        return _leafCache[t] = args != null && args.All(LeafType);
    }

    private static bool LeafType(Type t) =>
        t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || (t.IsValueType && !t.IsGenericType && !IsGame(t));

    private static bool IsDefault(object v)
    {
        switch (v)
        {
            case null: return true;
            case Object uo: return uo == null;
            case string s: return s.Length == 0;
            case bool b: return !b;
            case Enum en: return Convert.ToInt64(en) == 0;
            case ICollection c: return c.Count == 0;
        }
        var t = v.GetType();
        if (t.IsPrimitive) return Convert.ToDouble(v) == 0;
        if (t.IsValueType) { try { return v.Equals(Activator.CreateInstance(t)); } catch { return false; } }
        return false;
    }

    // Every JSON key and every "instanceID" value in data.json.
    private static void ReadDumpKeys(string path)
    {
        if (!File.Exists(path)) return;
        using var r = new JsonTextReader(new StreamReader(path));
        string last = null;
        while (r.Read())
        {
            if (r.TokenType == JsonToken.PropertyName) { last = (string)r.Value; _jsonKeys.Add(last.ToLowerInvariant()); }
            else if (r.TokenType == JsonToken.Integer && last == "instanceID") _jsonIds.Add(Convert.ToInt32(r.Value));
        }
    }

    private static string PathOf(int node)
    {
        var parts = new List<string>();
        for (var n = node; n >= 0; n = _parent[n]) parts.Add(_label[n]);
        parts.Reverse();
        var s = string.Concat(parts);
        return s.Length <= 400 ? s : s.Substring(0, 200) + " … " + s.Substring(s.Length - 200);
    }

    private static void WriteReport(string root, int roots, double secs)
    {
        var s = new StringBuilder();
        s.AppendLine($"# Reachability walk: {_parent.Count} nodes from {roots} roots in {secs:F1}s, {_errors} read errors{(_truncated != null ? ", TRUNCATED at " + _truncated : "")}");
        s.AppendLine("# Roots: Global, every game ScriptableObject, static-fields.txt values. Follows public/[SerializeField]/[SerializeReference] fields,");
        s.AppendLine("# collections, and prefabs (components + children) of referenced GameObjects/game components.");
        s.AppendLine("# catalogued = instanceID in NativeConverter.InstanceRefs; inline = plain data owned (no UnityEngine.Object hop) by a catalogued object;");
        s.AppendLine("# referenced = uncatalogued, but data.json carries its instanceID. uncovered = total - catalogued - inline.");
        s.AppendLine("#   total  catalog   inline  refrncd  type");
        foreach (var kv in _stats.OrderByDescending(x => x.Value.N - x.Value.Catalogued - x.Value.Inline).ThenBy(x => TypeName(x.Key), StringComparer.Ordinal))
        {
            var st = kv.Value;
            s.AppendLine($"{st.N,8} {st.Catalogued,8} {st.Inline,8} {st.Referenced,8}  {TypeName(kv.Key)}");
            if (st.CoveredExample >= 0) s.AppendLine($"    covered: {PathOf(st.CoveredExample)}");
            foreach (var n in st.Uncovered) s.AppendLine($"    path: {PathOf(n)}");
        }
        s.AppendLine();
        s.AppendLine("## Engine / third-party UnityEngine.Object leaves (recorded, not descended; GameObjects are descended)");
        foreach (var kv in _leaves.OrderByDescending(x => x.Value).ThenBy(x => x.Key.FullName, StringComparer.Ordinal))
            s.AppendLine($"{kv.Value,8}  {TypeName(kv.Key)}");
        File.WriteAllText(Path.Combine(root, "reachability.txt"), s.ToString());

        var u = new StringBuilder();
        u.AppendLine("# Serialized fields of covered (dumped) instances that hold non-default values on some instance,");
        u.AppendLine("# but whose name (case-insensitive, minus m_/_ prefix) never appears as a key in data.json. Builders rename fields, so expect noise.");
        u.AppendLine("# nonDefault/seen  DeclaringType.field : FieldType");
        foreach (var kv in _fieldStats.OrderBy(x => TypeName(x.Key.DeclaringType), StringComparer.Ordinal).ThenBy(x => x.Key.Name, StringComparer.Ordinal))
        {
            if (kv.Value[1] == 0 || NoiseField(kv.Key.FieldType)) continue;
            var key = kv.Key.Name.ToLowerInvariant();
            if (key.StartsWith("m_")) key = key.Substring(2);
            key = key.TrimStart('_');
            if (_jsonKeys.Contains(key) || _jsonKeys.Contains(kv.Key.Name.ToLowerInvariant())) continue;
            u.AppendLine($"{kv.Value[1],7}/{kv.Value[0],-7} {TypeName(kv.Key.DeclaringType)}.{kv.Key.Name} : {Probe.Pretty(kv.Key.FieldType)}");
        }
        File.WriteAllText(Path.Combine(root, "unemitted-fields.txt"), u.ToString());
    }
}
