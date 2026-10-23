using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace IlScan;

// Scan 2: numeric literals in method bodies, with the expression/call/field that consumes them.
sealed class ConstScan
{
    public sealed record Hit(string Value, string Kind, string Context, int Offset, int Score);

    public readonly SortedDictionary<string, SortedDictionary<string, List<Hit>>> ByOwner = new(StringComparer.Ordinal);
    public readonly Dictionary<string, int> ScoreByOwner = new(StringComparer.Ordinal);
    int skipped;

    static readonly Regex Interesting = new(
        @"xp|exp(?!lo)|level|damage|dmg|health|hp\b|cost|price|duration|delay|cooldown|speed|mult|chance|prob|threat|difficulty|reward|scrap|resource|radius|range|distance|heal|armou?r|shield|crit|rate|boost|bonus|penalty|weight|spawn|wave|limit|amount|ammo|fuel|oxygen|stack|tier|rarity|grade|score",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly Regex LoopBound = new(@"Count|Length|len\b|\.Length|<>1__state|__state", RegexOptions.CultureInvariant);

    public void Visit(Flow flow, Site site)
    {
        var m = flow.Method;
        if (m.IsConstructor && m.IsStatic) return; // covered by StaticScan
        var seen = new HashSet<(Instruction, string)>();
        foreach (var ins in flow.Reached)
        {
            var code = ins.OpCode.Code;
            if (code is Code.Stloc or Code.Dup or Code.Box or Code.Castclass or Code.Starg || code.ToString().StartsWith("Conv")) continue;
            var popped = flow.Popped(ins);
            for (int i = 0; i < popped.Length; i++)
            {
                if (popped[i] is not Num { Origin: { } origin } n) continue;
                if (origin.OpCode.Code is not (Code.Ldc_I4 or Code.Ldc_I8 or Code.Ldc_R4 or Code.Ldc_R8)) continue;
                double dv = Convert.ToDouble(n.V);
                if (dv is 0 or 1 or -1) continue;
                bool isFloat = n.V is float or double;
                var ctx = Context(ins, popped, i, isFloat, out bool skip);
                if (skip) { skipped++; continue; }
                if (!seen.Add((origin, ctx))) continue;
                int score = (isFloat ? 2 : 1)
                            + (Interesting.IsMatch(ctx) ? 3 : 0)
                            + (Interesting.IsMatch(site.Method) ? 1 : 0)
                            - (ctx.Contains("deltaTime") || ctx.Contains("Color") || ctx.Contains("Vector") ? 1 : 0);
                if (!ByOwner.TryGetValue(site.Owner, out var bm)) ByOwner[site.Owner] = bm = new(StringComparer.Ordinal);
                if (!bm.TryGetValue(site.Method, out var list)) bm[site.Method] = list = [];
                list.Add(new Hit(Interp.Num2S(n.V), n.V.GetType().Name, ctx, origin.Offset, score));
                ScoreByOwner[site.Owner] = ScoreByOwner.GetValueOrDefault(site.Owner) + score;
            }
        }
    }

    static bool IsEnum(TypeReference? t) => Interp.SafeResolve(t) is { IsEnum: true };

    static TypeReference? TypeOf(Val v) => v switch { Unk u => u.Type, ArgV a => a.Type, _ => null };

    static string Context(Instruction ins, Val[] popped, int slot, bool isFloat, out bool skip)
    {
        skip = false;
        var code = ins.OpCode.Code;
        switch (ins.Operand)
        {
            case MethodReference mr when code is Code.Call or Code.Callvirt or Code.Newobj:
            {
                int pi = slot - (mr.HasThis && code != Code.Newobj ? 1 : 0);
                var p = pi >= 0 && pi < mr.Parameters.Count ? mr.Parameters[pi] : null;
                if (p != null && IsEnum(p.ParameterType)) skip = true;
                if (!isFloat && code == Code.Newobj && mr.DeclaringType.Name.Contains('`')) skip = true; // collection capacity
                string args = string.Join(", ", popped.Skip(mr.HasThis && code != Code.Newobj ? 1 : 0).Select((v, j) => j == pi ? "#" : Interp.Render(v)));
                string recv = mr.HasThis && code != Code.Newobj && popped.Length > 0 && popped[0] is not ArgV { Name: "this" } ? Interp.Render(popped[0]) + "->" : "";
                return Interp.Clip($"{(code == Code.Newobj ? "new " : "")}{recv}{Interp.ShortName(mr)}({args})" + (string.IsNullOrEmpty(p?.Name) ? "" : $" [{p.Name}]"));
            }
            case FieldReference f when code is Code.Stfld or Code.Stsfld:
                if (f.Name.Contains("__state")) skip = true;
                return "-> " + Interp.ShortName(f);
        }
        if (code == Code.Ret) return "return";
        if (code is Code.Newarr) { skip = !isFloat; return "newarr"; }
        if (code.ToString().StartsWith("Ldelem") || code.ToString().StartsWith("Stelem") || code == Code.Ldelema)
        {
            skip = slot == 1 && !isFloat; // array index
            return "array element";
        }
        if (popped.Length == 2)
        {
            var other = popped[1 - slot];
            var otherSrc = Interp.Render(other);
            if (!isFloat && (LoopBound.IsMatch(otherSrc) || IsEnum(TypeOf(other)))) skip = true;
            if (code is Code.Shl or Code.Shr or Code.Shr_Un or Code.And or Code.Or or Code.Xor && !isFloat) skip = true;
            var sym = code switch
            {
                Code.Add => "+", Code.Sub => "-", Code.Mul => "*", Code.Div => "/", Code.Rem => "%",
                Code.Clt or Code.Blt or Code.Blt_Un => "<", Code.Cgt or Code.Bgt or Code.Bgt_Un => ">",
                Code.Ble or Code.Ble_Un => "<=", Code.Bge or Code.Bge_Un => ">=",
                Code.Ceq or Code.Beq => "==", Code.Bne_Un => "!=",
                _ => code.ToString().ToLowerInvariant(),
            };
            var a = slot == 0 ? "#" : Interp.Render(popped[0]);
            var b = slot == 1 ? "#" : Interp.Render(popped[1]);
            return Interp.Clip($"{a} {sym} {b}");
        }
        if (code is Code.Switch) { skip = true; return "switch"; }
        return code.ToString().ToLowerInvariant();
    }

    public List<string> Write(string dir)
    {
        int total = ByOwner.Values.Sum(t => t.Values.Sum(l => l.Count));
        var lines = new List<string>
        {
            "# Numeric literals per type/method. Format: value<TAB>score<TAB>context (# marks where the literal sits).",
            "# Skipped: 0/1/-1, enum-typed args/compares, loop bounds, array indices, state-machine plumbing, .cctor (see statics.txt).",
        };
        foreach (var (owner, methods) in ByOwner)
        {
            lines.Add("");
            lines.Add($"== {owner}  (score {ScoreByOwner[owner]})");
            foreach (var (meth, hits) in methods)
            {
                lines.Add($"  {meth}");
                foreach (var h in hits.OrderBy(h => h.Offset))
                    lines.Add($"    {h.Value}\t{h.Score}\t{h.Context}");
            }
        }
        File.WriteAllLines(Path.Combine(dir, "constants.txt"), lines);

        var all = ByOwner.SelectMany(o => o.Value.SelectMany(mm => mm.Value.Select(h => (Owner: o.Key, Meth: mm.Key, h)))).ToList();
        var top = new List<string> { "# Highest-scoring literals (float + tuning-sounding context). value<TAB>site<TAB>context" };
        foreach (var (o, mm, h) in all.Where(x => x.h.Score >= 5)
                     .OrderByDescending(x => x.h.Score).ThenBy(x => x.Owner, StringComparer.Ordinal).ThenBy(x => x.Meth, StringComparer.Ordinal).ThenBy(x => x.h.Offset))
            top.Add($"{h.Value}\t{o}.{mm}\t{h.Context}");
        top.Add("");
        top.Add("# Types ranked by summed score");
        foreach (var (o, s) in ScoreByOwner.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(150))
            top.Add($"{s,6}  {o}");
        File.WriteAllLines(Path.Combine(dir, "constants-top.txt"), top);

        Program.WriteJson(Path.Combine(dir, "constants.json"),
            ByOwner.ToDictionary(o => o.Key, o => o.Value.ToDictionary(mm => mm.Key, mm => mm.Value.OrderBy(h => h.Offset).Select(h => new { value = h.Value, type = h.Kind, score = h.Score, context = h.Context, il = h.Offset }))));

        return
        [
            "[constants]  constants.txt constants-top.txt constants.json",
            $"  literals: {total} across {ByOwner.Count} types ({skipped} trivial skipped), high-score (>=5): {all.Count(x => x.h.Score >= 5)}",
            "  top types: " + string.Join(", ", ScoreByOwner.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(8).Select(kv => $"{kv.Key}({kv.Value})")),
        ];
    }
}
