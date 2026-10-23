using System.Globalization;
using System.Text.Json.Nodes;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace IlScan;

// Scan 3: values assigned to static fields in .cctor (arrays incl. InitializeArray blobs, collections, ctor calls) + const fields.
sealed class StaticScan
{
    public sealed record Entry(string Owner, string Field, string FieldType, bool ReadOnly, string Text, JsonNode? Json, int Size);

    public readonly List<Entry> Statics = [];
    public readonly List<Entry> Consts = [];
    const int MaxItems = 4096;

    public void VisitType(TypeDefinition t)
    {
        if (t.IsEnum || t.Name.StartsWith('<')) return;
        foreach (var f in t.Fields.Where(f => f.HasConstant && !f.Name.StartsWith('<')))
        {
            var v = f.Constant;
            string text = v switch { null => "null", string s => Interp.Quote(s), _ => Interp.Num2S(v) };
            Consts.Add(new Entry(Sites.OwnerOf(t), f.Name, f.FieldType.Name, true, text,
                v == null ? null : JsonValue.Create(v), 1));
        }
    }

    public void Visit(Flow flow, Site site)
    {
        if (!(flow.Method.IsConstructor && flow.Method.IsStatic)) return;
        // last write wins; walk in IL order
        var writes = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var ins in flow.Reached)
        {
            if (ins.OpCode.Code != Code.Stsfld) continue;
            var f = (FieldReference)ins.Operand;
            if (f.Name.StartsWith('<') || f.DeclaringType.Name.StartsWith('<')) continue;
            var v = flow.Popped(ins)[0];
            if (v is Unk or ArgV or TokV || IsEmpty(v)) continue;
            if (v is Num n && (f.FieldType.MetadataType == MetadataType.Boolean || Interp.SafeResolve(f.FieldType) is { IsEnum: true }))
                v = new Num(n.V, null, f.FieldType);
            var fd = f.Resolve();
            int size = v switch { ArrV a => a.Len ?? a.Items.Count, CollV c => c.Entries.Count, Strs s => s.Set.Count, _ => 1 };
            writes[f.Name] = new Entry(site.Owner, f.Name, f.FieldType.Name, fd?.IsInitOnly ?? false, RenderFull(v), ToJson(v), size);
        }
        Statics.AddRange(writes.Values.OrderBy(e => e.Field, StringComparer.Ordinal));
    }

    // empty collections and zero-filled buffers carry no data
    static bool IsEmpty(Val v) => v switch
    {
        CollV c => c.Entries.Count == 0,
        ArrV a => a.Blob == null && a.Items.Count == 0,
        _ => false,
    };

    static List<Val> Elements(ArrV a)
    {
        if (a.Blob != null) return DecodeBlob(a);
        int n = Math.Min(a.Len ?? (a.Items.Count == 0 ? 0 : a.Items.Keys.Max() + 1), MaxItems);
        return Enumerable.Range(0, n).Select(i => a.Items.GetValueOrDefault(i) ?? (Val)new Unk(null, "default")).ToList();
    }

    static List<Val> DecodeBlob(ArrV a)
    {
        var t = a.Elem;
        var td = Interp.SafeResolve(t);
        if (td is { IsEnum: true }) t = td.Fields.First(f => !f.IsStatic).FieldType;
        var blob = a.Blob!;
        (int size, Func<int, object> read) = t.MetadataType switch
        {
            MetadataType.Boolean => (1, i => (object)(blob[i] != 0)),
            MetadataType.Byte => (1, i => blob[i]),
            MetadataType.SByte => (1, i => (sbyte)blob[i]),
            MetadataType.Int16 => (2, i => BitConverter.ToInt16(blob, i)),
            MetadataType.UInt16 => (2, i => BitConverter.ToUInt16(blob, i)),
            MetadataType.Char => (2, i => BitConverter.ToChar(blob, i)),
            MetadataType.Int32 => (4, i => BitConverter.ToInt32(blob, i)),
            MetadataType.UInt32 => (4, i => BitConverter.ToUInt32(blob, i)),
            MetadataType.Single => (4, i => BitConverter.ToSingle(blob, i)),
            MetadataType.Int64 => (8, i => BitConverter.ToInt64(blob, i)),
            MetadataType.UInt64 => (8, i => BitConverter.ToUInt64(blob, i)),
            MetadataType.Double => (8, i => BitConverter.ToDouble(blob, i)),
            _ => (0, (Func<int, object>)(_ => 0)),
        };
        if (size == 0) return [new Unk(null, $"<{blob.Length}-byte blob of {a.Elem.Name}>")];
        int n = Math.Min(a.Len ?? blob.Length / size, Math.Min(blob.Length / size, MaxItems));
        return Enumerable.Range(0, n).Select(i => (Val)new Num(read(i * size), null, td is { IsEnum: true } ? a.Elem : null)).ToList();
    }

    public static string RenderFull(Val v) => v switch
    {
        ArrV a => "[" + string.Join(", ", Elements(a).Select(RenderFull)) + "]",
        CollV c => c.Entries.Values.Any(e => e.K != null)
            ? "{" + string.Join(", ", c.Entries.Values.Select(e => $"{(e.K == null ? "?" : RenderFull(e.K))}: {RenderFull(e.V)}")) + "}"
            : "[" + string.Join(", ", c.Entries.Values.Select(e => RenderFull(e.V))) + "]",
        ObjV o => $"{o.Ctor.DeclaringType.Name}({string.Join(", ", o.Args.Select(RenderFull))})",
        _ => Interp.Render(v),
    };

    public static JsonNode? ToJson(Val v)
    {
        switch (v)
        {
            case Num n:
                if (n.Type?.MetadataType == MetadataType.Boolean) return JsonValue.Create(Interp.ToLong(n.V) != 0);
                if (n.Type != null) return JsonValue.Create(Interp.Render(n));
                return n.V switch
                {
                    float f when float.IsFinite(f) => JsonValue.Create(f),
                    double d when double.IsFinite(d) => JsonValue.Create(d),
                    float or double => JsonValue.Create(Interp.Num2S(n.V)),
                    bool b => JsonValue.Create(b),
                    _ => JsonValue.Create(Interp.ToLong(n.V)),
                };
            case Strs s when s.Set.Count == 1 && !s.Partial:
                return JsonValue.Create(s.Set.Min);
            case ArrV a:
                return new JsonArray(Elements(a).Select(ToJson).ToArray());
            case CollV c when c.Entries.Values.Any(e => e.K != null):
                var obj = new JsonObject();
                foreach (var e in c.Entries.Values)
                {
                    var k = e.K is Strs ks && ks.Set.Count == 1 ? ks.Set.Min! : e.K == null ? "?" : RenderFull(e.K);
                    obj[k] = ToJson(e.V);
                }
                return obj;
            case CollV c:
                return new JsonArray(c.Entries.Values.Select(e => ToJson(e.V)).ToArray());
            case ObjV o:
                return new JsonObject
                {
                    ["$type"] = o.Ctor.DeclaringType.Name,
                    ["args"] = new JsonArray(o.Args.Select(ToJson).ToArray()),
                };
            default:
                return JsonValue.Create(Interp.Render(v));
        }
    }

    public List<string> Write(string dir)
    {
        var lines = new List<string> { "# Static field values assigned in .cctor. [ro] = static readonly. Format: Owner.Field : Type [n] = value" };
        foreach (var g in Statics.GroupBy(e => e.Owner).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            lines.Add("");
            lines.Add($"== {g.Key}");
            foreach (var e in g) lines.Add($"  {(e.ReadOnly ? "[ro] " : "")}{e.Field} : {e.FieldType} [{e.Size}] = {e.Text}");
        }
        File.WriteAllLines(Path.Combine(dir, "statics.txt"), lines);

        var cl = new List<string> { "# const fields (non-enum types). Owner.Field : Type = value" };
        foreach (var g in Consts.GroupBy(e => e.Owner).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            cl.Add($"== {g.Key}");
            foreach (var e in g.OrderBy(e => e.Field, StringComparer.Ordinal)) cl.Add($"  {e.Field} : {e.FieldType} = {e.Text}");
        }
        File.WriteAllLines(Path.Combine(dir, "consts.txt"), cl);

        var json = new JsonObject();
        foreach (var g in Statics.GroupBy(e => e.Owner).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var o = new JsonObject();
            foreach (var e in g) o[e.Field] = e.Json?.DeepClone();
            json[g.Key] = o;
        }
        var cj = new JsonObject();
        foreach (var g in Consts.GroupBy(e => e.Owner).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var o = new JsonObject();
            foreach (var e in g.OrderBy(e => e.Field, StringComparer.Ordinal)) o[e.Field] = e.Json?.DeepClone();
            cj[g.Key] = o;
        }
        File.WriteAllText(Path.Combine(dir, "statics.json"),
            new JsonObject { ["statics"] = json, ["consts"] = cj }.ToJsonString(Program.Json) + "\n");

        var tables = Statics.Where(e => e.Size >= 3).OrderByDescending(e => e.Size).ThenBy(e => e.Owner, StringComparer.Ordinal).Take(10);
        return
        [
            "[statics]  statics.txt consts.txt statics.json",
            $"  static field initializers: {Statics.Count} in {Statics.Select(e => e.Owner).Distinct().Count()} types ({Statics.Count(e => e.Size >= 3)} tables with >=3 entries); const fields: {Consts.Count}",
            "  largest tables: " + string.Join(", ", tables.Select(e => $"{e.Owner}.{e.Field}[{e.Size}]")),
        ];
    }
}
