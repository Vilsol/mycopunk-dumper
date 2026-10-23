using Mono.Cecil;
using Mono.Cecil.Cil;

namespace IlScan;

// Scan 4: switch / if-chain dispatch on an enum, int or string whose cases yield literals.
sealed class SwitchScan
{
    public sealed record Table(string Owner, string Method, string Subject, string Kind, List<(string Case, string Result)> Rows);

    public readonly List<Table> Tables = [];

    public void Visit(Flow flow, Site site)
    {
        var chains = new Dictionary<string, (string Kind, List<(string, string)> Rows, TypeReference? Type)>(StringComparer.Ordinal);
        foreach (var ins in flow.Reached)
        {
            var code = ins.OpCode.Code;
            if (code == Code.Switch)
            {
                var v = flow.Popped(ins)[0];
                var subject = Interp.Render(v);
                if (subject.Contains("__state") || subject.Contains("ComputeStringHash")) continue;
                int offset = 0;
                var type = v switch { Unk u => u.Type, ArgV a => a.Type, _ => null };
                if (ins.Previous is { OpCode.Code: Code.Sub } sub && sub.Previous is { OpCode.Code: Code.Ldc_I4 } k)
                {
                    offset = (int)k.Operand;
                    var left = flow.Popped(sub)[0];
                    subject = Interp.Render(left);
                    type = left switch { Unk u => u.Type, ArgV a => a.Type, _ => type };
                }
                var targets = (Instruction[])ins.Operand;
                var rows = new List<(string, string)>();
                for (int i = 0; i < targets.Length; i++)
                    if (CaseResult(targets[i]) is { } r) rows.Add((Label(type, i + offset), r));
                if (ins.Next != null && CaseResult(ins.Next) is { } d) rows.Add(("default", d));
                if (rows.Count(r => r.Item1 != "default") >= 2 && Informative(rows))
                    Tables.Add(new Table(site.Owner, site.Method, subject, "switch", rows));
                continue;
            }

            if (code is Code.Beq or Code.Bne_Un)
            {
                var p = flow.Popped(ins);
                if (p.Length != 2) continue;
                int ni = p[1] is Num ? 1 : p[0] is Num ? 0 : -1;
                if (ni < 0 || p[1 - ni] is Num or Strs) continue;
                var n = (Num)p[ni];
                if (n.V is not (int or long)) continue;
                var other = p[1 - ni];
                var type = other switch { Unk u => u.Type, ArgV a => a.Type, _ => null };
                var target = code == Code.Beq ? (Instruction)ins.Operand : ins.Next;
                AddChain(Interp.Render(other), "if-chain", Label(type, Interp.ToLong(n.V)), target, type);
                continue;
            }

            if (code is Code.Brtrue or Code.Brfalse && ins.Previous is { OpCode.Code: Code.Call } eq
                && eq.Operand is MethodReference { Name: "op_Equality", DeclaringType.FullName: "System.String" }
                && flow.In.ContainsKey(eq))
            {
                var p = flow.Popped(eq);
                if (p.Length != 2) continue;
                int si = p[1] is Strs { Set.Count: 1, Partial: false } ? 1 : p[0] is Strs { Set.Count: 1, Partial: false } ? 0 : -1;
                if (si < 0) continue;
                var target = code == Code.Brtrue ? (Instruction)ins.Operand : ins.Next;
                AddChain(Interp.Render(p[1 - si]), "string-chain", Interp.Quote(((Strs)p[si]).Set.Min!), target, null);
            }
        }

        foreach (var (subject, (kind, rows, _)) in chains.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            if (rows.Count >= 3 && rows.Count(r => r.Item2 != null) >= 2 && Informative(rows.Where(r => r.Item2 != null).ToList()))
                Tables.Add(new Table(site.Owner, site.Method, subject, kind, rows.Where(r => r.Item2 != null).ToList()));
        return;

        void AddChain(string subject, string kind, string label, Instruction? target, TypeReference? type)
        {
            if (subject.Contains("__state") || target == null) return;
            if (!chains.TryGetValue(subject, out var c)) chains[subject] = c = (kind, [], type);
            if (c.Rows.Any(r => r.Item1 == label)) return;
            c.Rows.Add((label, CaseResult(target)!));
        }
    }

    // drop bool-ish predicates (every case returns 0/1) and tables with a single distinct result
    static bool Informative(List<(string Case, string Result)> rows) =>
        rows.Select(r => r.Result).Distinct().Count() >= 2 && !rows.All(r => r.Result is "0" or "1" or "0, 0" or "1, 1");

    static string Label(TypeReference? t, long v)
    {
        if (Interp.SafeResolve(t) is { IsEnum: true } td
            && td.Fields.FirstOrDefault(f => f.HasConstant && Interp.ToLong(f.Constant) == v) is { } f)
            return td.Name + "." + f.Name;
        return v.ToString();
    }

    // Literals produced on the straight-line path from a case target, e.g. `ldstr "x"; ret` or `ldc.r4 1.5; stloc`.
    static string? CaseResult(Instruction start)
    {
        var parts = new List<string>();
        var seen = new HashSet<Instruction>();
        var cur = start;
        for (int steps = 0; cur != null && steps < 16 && seen.Add(cur); steps++)
        {
            switch (cur.OpCode.Code)
            {
                case Code.Br:
                    cur = (Instruction)cur.Operand;
                    continue;
                case Code.Ldstr:
                    parts.Add(Interp.Quote((string)cur.Operand));
                    break;
                case Code.Ldc_I4 or Code.Ldc_I8 or Code.Ldc_R4 or Code.Ldc_R8:
                    parts.Add(Interp.Num2S(cur.Operand));
                    break;
                case Code.Ldsfld when cur.Operand is FieldReference { FieldType.MetadataType: MetadataType.String } f:
                    parts.Add(Interp.ShortName(f));
                    break;
                case Code.Call or Code.Callvirt or Code.Newobj:
                    var mr = (MethodReference)cur.Operand;
                    if (mr.DeclaringType.Name == "TextBlocks" && parts.Count > 0)
                    {
                        int k = Math.Min(mr.Parameters.Count, parts.Count);
                        var call = $"loc({string.Join(", ", parts[^k..])})";
                        parts.RemoveRange(parts.Count - k, k);
                        parts.Add(call);
                    }
                    else if (cur.OpCode.Code == Code.Newobj && parts.Count > 0)
                        parts = [$"new {mr.DeclaringType.Name}({string.Join(", ", parts)})"];
                    else if (mr.ReturnType.MetadataType == MetadataType.Void) goto done;
                    break;
                case Code.Ret or Code.Stloc or Code.Stfld or Code.Stsfld or Code.Starg or Code.Throw or Code.Leave:
                    goto done;
                default:
                    if (cur.OpCode.FlowControl == FlowControl.Cond_Branch) goto done;
                    break;
            }
            cur = cur.Next;
        }
        done:
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    public List<string> Write(string dir)
    {
        var lines = new List<string> { "# Case -> literal tables found in switch / if-chain / string-compare dispatch. loc(...) = literals passed to a TextBlocks lookup." };
        foreach (var t in Tables.OrderBy(t => t.Owner, StringComparer.Ordinal).ThenBy(t => t.Method, StringComparer.Ordinal))
        {
            lines.Add("");
            lines.Add($"== {t.Owner}.{t.Method}  [{t.Kind} on {t.Subject}]");
            foreach (var (c, r) in t.Rows) lines.Add($"  {c}\t=> {r}");
        }
        File.WriteAllLines(Path.Combine(dir, "switches.txt"), lines);
        Program.WriteJson(Path.Combine(dir, "switches.json"), Tables
            .OrderBy(t => t.Owner, StringComparer.Ordinal).ThenBy(t => t.Method, StringComparer.Ordinal)
            .Select(t => new { owner = t.Owner, method = t.Method, kind = t.Kind, subject = t.Subject, cases = t.Rows.Select(r => new { @case = r.Case, result = r.Result }) }));
        var loc = Tables.Count(t => t.Rows.Any(r => r.Result.Contains("loc(")));
        return
        [
            "[switch tables]  switches.txt switches.json",
            $"  tables: {Tables.Count} ({Tables.Count(t => t.Kind == "switch")} switch, {Tables.Count(t => t.Kind == "if-chain")} if-chain, {Tables.Count(t => t.Kind == "string-chain")} string-chain; {loc} feed TextBlocks)",
            "  largest: " + string.Join(", ", Tables.OrderByDescending(t => t.Rows.Count).ThenBy(t => t.Owner, StringComparer.Ordinal).Take(8).Select(t => $"{t.Owner}.{t.Method}[{t.Rows.Count}]")),
        ];
    }
}

static class SrcGaps
{
    // Scan 5: IL-only data whose owning type / field / method is never named in src/ (case-insensitive identifier match).
    public static List<string> Write(string dir, HashSet<string> src, LocScan loc, ConstScan consts, StaticScan statics, SwitchScan switches)
    {
        static string Simple(string owner) => owner[(owner.LastIndexOf('.') + 1)..].Split('`')[0];
        static string Member(string m) => (m.StartsWith("get_") ? m[4..] : m).ToLowerInvariant();
        bool Known(string ident) => src.Contains(ident.ToLowerInvariant());

        var members = new List<string>();
        foreach (var e in statics.Statics.Where(e => e.Size >= 2 && !Known(e.Field)).OrderByDescending(e => e.Size).ThenBy(e => e.Owner, StringComparer.Ordinal))
            members.Add($"static\t{e.Owner}.{e.Field}\t[{e.Size}]\t{Interp.Clip(e.Text)}");
        foreach (var t in switches.Tables.Where(t => !src.Contains(Member(t.Method))).OrderByDescending(t => t.Rows.Count).ThenBy(t => t.Owner, StringComparer.Ordinal))
            members.Add($"switch\t{t.Owner}.{t.Method}\t[{t.Rows.Count}]\t{t.Subject}");

        var rows = new Dictionary<string, (int Loc, int Sw, int St, int Cs)>(StringComparer.Ordinal);
        void Bump(string owner, int l = 0, int sw = 0, int st = 0, int cs = 0)
        {
            var r = rows.GetValueOrDefault(owner);
            rows[owner] = (r.Loc + l, r.Sw + sw, r.St + st, r.Cs + cs);
        }
        foreach (var (o, n) in loc.LiteralByOwner) Bump(o, l: n);
        foreach (var t in switches.Tables) Bump(t.Owner, sw: 1);
        foreach (var e in statics.Statics.Where(e => e.Size >= 3)) Bump(e.Owner, st: 1);
        foreach (var (o, s) in consts.ScoreByOwner) Bump(o, cs: s);
        var gaps = rows.Where(kv => !Known(Simple(kv.Key)))
            .Select(kv => (kv.Key, kv.Value, Rank: kv.Value.Loc + 5 * kv.Value.Sw + 5 * kv.Value.St + kv.Value.Cs / 10))
            .Where(x => x.Rank > 0)
            .OrderByDescending(x => x.Rank).ThenBy(x => x.Key, StringComparer.Ordinal).ToList();

        var lines = new List<string>
        {
            $"# Static tables (>=2 entries) and switch tables whose field/method name never appears in src/ ({members.Count}) — likely undumped.",
            "# kind\tmember\t[size]\tvalue/subject",
        };
        lines.AddRange(members);
        lines.Add("");
        lines.Add($"# Types with IL-only data never mentioned in src/ ({gaps.Count} of {rows.Count} types with findings).");
        lines.Add("# rank\ttype\tlocLiterals\tswitchTables\tstaticTables\tconstScore");
        lines.AddRange(gaps.Select(g => $"{g.Rank}\t{g.Key}\t{g.Value.Loc}\t{g.Value.Sw}\t{g.Value.St}\t{g.Value.Cs}"));
        File.WriteAllLines(Path.Combine(dir, "src-gaps.txt"), lines);
        return
        [
            "[src gaps]  src-gaps.txt",
            $"  {members.Count} static/switch tables not referenced by name in src/; e.g. " + string.Join(", ", members.Take(8).Select(m => m.Split('\t')[1])),
            $"  {gaps.Count} of {rows.Count} types with findings never referenced in src/; top: " + string.Join(", ", gaps.Take(10).Select(g => Simple(g.Key))),
        ];
    }
}
