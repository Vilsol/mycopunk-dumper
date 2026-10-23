using System.Globalization;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace IlScan;

// Abstract values tracked on the IL evaluation stack / in locals.
abstract class Val;

sealed class Unk(TypeReference? type, string? src) : Val
{
    public static readonly Unk Any = new(null, null);
    public readonly TypeReference? Type = type;
    public readonly string? Src = src;
}

// Set of possible string constants. Entries containing "{*}" are concatenation patterns.
// Partial = some control-flow path produced a non-constant.
sealed class Strs(SortedSet<string> set, string? via = null, bool partial = false) : Val
{
    public readonly SortedSet<string> Set = set;
    public readonly string? Via = via;
    public readonly bool Partial = partial;
    public Strs(string s) : this(new SortedSet<string>(StringComparer.Ordinal) { s }) { }
}

sealed class Num(object v, Instruction? origin, TypeReference? type = null) : Val
{
    public readonly object V = v;
    public readonly Instruction? Origin = origin;
    public readonly TypeReference? Type = type;
}

sealed class ArgV(int index, TypeReference type, string name) : Val
{
    public readonly int Index = index;
    public readonly TypeReference Type = type;
    public readonly string Name = name;
}

sealed class ArrV(TypeReference elem, int? len) : Val
{
    public readonly TypeReference Elem = elem;
    public readonly int? Len = len;
    public readonly SortedDictionary<int, Val> Items = new();
    public byte[]? Blob;
}

sealed class CollV(TypeReference type) : Val
{
    public readonly TypeReference Type = type;
    // keyed by call offset so revisits during the fixpoint stay idempotent
    public readonly SortedDictionary<int, (Val? K, Val V)> Entries = new();
}

sealed class ObjV(MethodReference ctor, Val[] args) : Val
{
    public readonly MethodReference Ctor = ctor;
    public readonly Val[] Args = args;
}

sealed class TokV(object member) : Val
{
    public readonly object Member = member;
}

sealed class ElemAddr(ArrV arr, int index) : Val
{
    public readonly ArrV Arr = arr;
    public readonly int Index = index;
}

sealed class State(Val[] stack, Val[] locals)
{
    public Val[] Stack = stack;
    public readonly Val[] Locals = locals;
    public State Clone() => new((Val[])Stack.Clone(), (Val[])Locals.Clone());
}

sealed class Flow(MethodDefinition method, Dictionary<Instruction, State> inStates)
{
    public readonly MethodDefinition Method = method;
    public readonly Dictionary<Instruction, State> In = inStates;
    public IEnumerable<Instruction> Reached => Method.Body.Instructions.Where(In.ContainsKey);

    // Values an instruction pops, in push order (args[0] = first argument / `this`).
    public Val[] Popped(Instruction ins)
    {
        var st = In[ins];
        int n = Interp.PopCount(ins, Method, st.Stack.Length);
        n = Math.Min(n, st.Stack.Length);
        return st.Stack[^n..];
    }
}

static class Interp
{
    const int MaxSet = 128;
    const int MaxVisits = 64;
    const int MaxSrc = 160;

    static readonly HashSet<string> CollectionTypes = new(StringComparer.Ordinal)
    {
        "Dictionary`2", "List`1", "HashSet`1", "SortedDictionary`2", "SortedList`2", "Queue`1", "Stack`1",
        "SortedSet`1", "ConcurrentDictionary`2",
    };

    public static Flow Run(MethodDefinition m)
    {
        var body = m.Body;
        body.SimplifyMacros();
        var ins = body.Instructions;
        var inStates = new Dictionary<Instruction, State>();
        var visits = new Dictionary<Instruction, int>();
        var cache = new Dictionary<int, Val>();
        var queue = new Queue<Instruction>();
        if (ins.Count == 0) return new Flow(m, inStates);

        Val[] InitLocals() => body.Variables.Select(v => (Val)new Unk(v.VariableType, null)).ToArray();

        void Seed(Instruction at, Val[] stack)
        {
            if (inStates.ContainsKey(at)) return;
            inStates[at] = new State(stack, InitLocals());
            queue.Enqueue(at);
        }

        Seed(ins[0], []);
        foreach (var h in body.ExceptionHandlers)
        {
            bool catches = h.HandlerType is ExceptionHandlerType.Catch or ExceptionHandlerType.Filter;
            Seed(h.HandlerStart, catches ? [Unk.Any] : []);
            if (h.FilterStart != null) Seed(h.FilterStart, [Unk.Any]);
        }

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            var st = inStates[cur].Clone();
            var succ = Step(cur, st, m, cache);
            foreach (var s in succ)
            {
                if (!inStates.TryGetValue(s, out var old))
                {
                    inStates[s] = st.Clone();
                    queue.Enqueue(s);
                    continue;
                }
                var merged = MergeState(old, st, out bool changed);
                if (!changed) continue;
                inStates[s] = merged;
                visits.TryGetValue(s, out int v);
                if (v >= MaxVisits) continue;
                visits[s] = v + 1;
                queue.Enqueue(s);
            }
        }
        return new Flow(m, inStates);
    }

    static State MergeState(State a, State b, out bool changed)
    {
        changed = false;
        int n = Math.Min(a.Stack.Length, b.Stack.Length);
        var stack = new Val[n];
        for (int i = 0; i < n; i++)
        {
            stack[i] = Merge(a.Stack[a.Stack.Length - n + i], b.Stack[b.Stack.Length - n + i]);
            if (!ReferenceEquals(stack[i], a.Stack[a.Stack.Length - n + i])) changed = true;
        }
        if (n != a.Stack.Length) changed = true;
        var locals = new Val[a.Locals.Length];
        for (int i = 0; i < locals.Length; i++)
        {
            locals[i] = Merge(a.Locals[i], b.Locals[i]);
            if (!ReferenceEquals(locals[i], a.Locals[i])) changed = true;
        }
        return new State(stack, locals);
    }

    // Returns `a` itself when nothing changes, so callers can detect a fixpoint by reference.
    static Val Merge(Val a, Val b)
    {
        if (ReferenceEquals(a, b)) return a;
        switch (a, b)
        {
            case (Strs x, Strs y):
                if (x.Set.IsSupersetOf(y.Set) && (x.Partial || !y.Partial)) return a;
                var u = new SortedSet<string>(x.Set, StringComparer.Ordinal);
                u.UnionWith(y.Set);
                if (u.Count > MaxSet) return Unk.Any;
                return new Strs(u, x.Via ?? y.Via, x.Partial || y.Partial);
            case (Strs x, _):
                return x.Partial ? a : new Strs(x.Set, x.Via, true);
            case (_, Strs y):
                return new Strs(y.Set, y.Via, true);
            case (Num x, Num y) when Equals(x.V, y.V) && x.Origin == y.Origin:
                return a;
            case (ArgV x, ArgV y) when x.Index == y.Index:
                return a;
            case (Unk x, Unk y):
                if (x.Type == null && x.Src == null) return a;
                var t = x.Type?.FullName == y.Type?.FullName ? x.Type : null;
                var s = x.Src == y.Src ? x.Src : null;
                if (t == x.Type && s == x.Src) return a;
                return new Unk(t, s);
            case (Unk { Type: null, Src: null }, _):
                return a;
        }
        return Unk.Any;
    }

    public static int PopCount(Instruction ins, MethodDefinition m, int stackLen)
    {
        var op = ins.OpCode;
        switch (op.StackBehaviourPop)
        {
            case StackBehaviour.Pop0: return 0;
            case StackBehaviour.Pop1:
            case StackBehaviour.Popi:
            case StackBehaviour.Popref:
                return 1;
            case StackBehaviour.Pop1_pop1:
            case StackBehaviour.Popi_pop1:
            case StackBehaviour.Popi_popi:
            case StackBehaviour.Popi_popi8:
            case StackBehaviour.Popi_popr4:
            case StackBehaviour.Popi_popr8:
            case StackBehaviour.Popref_pop1:
            case StackBehaviour.Popref_popi:
                return 2;
            case StackBehaviour.Popi_popi_popi:
            case StackBehaviour.Popref_popi_popi:
            case StackBehaviour.Popref_popi_popi8:
            case StackBehaviour.Popref_popi_popr4:
            case StackBehaviour.Popref_popi_popr8:
            case StackBehaviour.Popref_popi_popref:
                return 3;
            case StackBehaviour.PopAll: return stackLen;
            case StackBehaviour.Varpop:
                if (op.Code == Code.Ret) return m.ReturnType.MetadataType == MetadataType.Void ? 0 : 1;
                if (ins.Operand is CallSite cs) return cs.Parameters.Count + (cs.HasThis ? 1 : 0) + 1;
                if (ins.Operand is MethodReference mr)
                    return mr.Parameters.Count + (mr.HasThis && op.Code != Code.Newobj ? 1 : 0);
                return 0;
        }
        return 0;
    }

    public static int PushCount(Instruction ins)
    {
        var op = ins.OpCode;
        switch (op.StackBehaviourPush)
        {
            case StackBehaviour.Push0: return 0;
            case StackBehaviour.Push1_push1: return 2;
            case StackBehaviour.Varpush:
                var rt = ins.Operand switch { MethodReference mr => mr.ReturnType, CallSite cs => cs.ReturnType, _ => null };
                return rt == null || rt.MetadataType == MetadataType.Void ? 0 : 1;
            default: return 1;
        }
    }

    static List<Instruction> Successors(Instruction ins)
    {
        var r = new List<Instruction>(2);
        switch (ins.OpCode.FlowControl)
        {
            case FlowControl.Branch:
                r.Add((Instruction)ins.Operand);
                break;
            case FlowControl.Cond_Branch:
                if (ins.Operand is Instruction[] targets) r.AddRange(targets);
                else r.Add((Instruction)ins.Operand);
                if (ins.Next != null) r.Add(ins.Next);
                break;
            case FlowControl.Return:
            case FlowControl.Throw:
                break;
            default:
                if (ins.Next != null) r.Add(ins.Next);
                break;
        }
        return r;
    }

    static List<Instruction> Step(Instruction ins, State st, MethodDefinition m, Dictionary<int, Val> cache)
    {
        var stack = new List<Val>(st.Stack);
        Val Pop()
        {
            if (stack.Count == 0) return Unk.Any;
            var v = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            return v;
        }
        void Push(Val v) => stack.Add(v);

        var code = ins.OpCode.Code;
        switch (code)
        {
            case Code.Ldstr: Push(new Strs((string)ins.Operand)); break;
            case Code.Ldc_I4: Push(new Num((int)ins.Operand, ins)); break;
            case Code.Ldc_I8: Push(new Num((long)ins.Operand, ins)); break;
            case Code.Ldc_R4: Push(new Num((float)ins.Operand, ins)); break;
            case Code.Ldc_R8: Push(new Num((double)ins.Operand, ins)); break;
            case Code.Ldnull: Push(new Unk(null, "null")); break;
            case Code.Ldarg:
            {
                var p = (ParameterDefinition)ins.Operand;
                int idx = p == m.Body.ThisParameter ? 0 : p.Index + (m.HasThis ? 1 : 0);
                Push(new ArgV(idx, p.ParameterType, p == m.Body.ThisParameter ? "this" : p.Name));
                break;
            }
            case Code.Ldarga:
            {
                var p = (ParameterDefinition)ins.Operand;
                Push(new Unk(p.ParameterType, "&" + p.Name));
                break;
            }
            case Code.Ldloc:
                Push(st.Locals[((VariableDefinition)ins.Operand).Index]);
                break;
            case Code.Stloc:
                st.Locals[((VariableDefinition)ins.Operand).Index] = Pop();
                break;
            case Code.Ldloca:
            {
                var v = (VariableDefinition)ins.Operand;
                Push(new Unk(v.VariableType, "&V_" + v.Index));
                break;
            }
            case Code.Dup:
            {
                var v = Pop();
                Push(v);
                Push(v);
                break;
            }
            case Code.Ldfld:
            case Code.Ldflda:
            {
                var f = (FieldReference)ins.Operand;
                var obj = Pop();
                string prefix = obj is ArgV { Name: "this" } or Unk { Src: null or "this" } ? "" : Render(obj) + "->";
                Push(new Unk(f.FieldType, Clip(prefix + ShortName(f))));
                break;
            }
            case Code.Ldsfld:
            case Code.Ldsflda:
            {
                var f = (FieldReference)ins.Operand;
                Push(new Unk(f.FieldType, ShortName(f)));
                break;
            }
            case Code.Ldtoken: Push(new TokV(ins.Operand)); break;
            case Code.Box:
            {
                var v = Pop();
                var t = (TypeReference)ins.Operand;
                Push(v switch
                {
                    Num n => new Num(n.V, n.Origin, t),
                    Unk u => new Unk(u.Type ?? t, u.Src),
                    _ => v,
                });
                break;
            }
            case Code.Castclass:
            case Code.Unbox_Any:
            case Code.Conv_I: case Code.Conv_I1: case Code.Conv_I2: case Code.Conv_I4: case Code.Conv_I8:
            case Code.Conv_U: case Code.Conv_U1: case Code.Conv_U2: case Code.Conv_U4: case Code.Conv_U8:
            case Code.Conv_R4: case Code.Conv_R8: case Code.Conv_R_Un:
                break; // value passes through unchanged
            case Code.Add: case Code.Sub: case Code.Mul: case Code.Div: case Code.Rem:
            case Code.Add_Ovf: case Code.Add_Ovf_Un: case Code.Sub_Ovf: case Code.Sub_Ovf_Un:
            case Code.Mul_Ovf: case Code.Mul_Ovf_Un: case Code.Div_Un: case Code.Rem_Un:
            case Code.And: case Code.Or: case Code.Xor: case Code.Shl: case Code.Shr: case Code.Shr_Un:
            case Code.Ceq: case Code.Cgt: case Code.Cgt_Un: case Code.Clt: case Code.Clt_Un:
            {
                var b = Pop();
                var a = Pop();
                var t = (a as Unk)?.Type ?? (b as Unk)?.Type ?? (a as ArgV)?.Type;
                Push(new Unk(t, Clip($"({Render(a)} {OpSymbol(code)} {Render(b)})")));
                break;
            }
            case Code.Neg:
                Push(new Unk((stack.Count > 0 ? stack[^1] as Unk : null)?.Type, Clip("-" + Render(Pop()))));
                break;
            case Code.Newarr:
            {
                var len = Pop();
                if (!cache.TryGetValue(ins.Offset, out var arr))
                    cache[ins.Offset] = arr = new ArrV((TypeReference)ins.Operand, len is Num n ? Convert.ToInt32(n.V) : null);
                Push(arr);
                break;
            }
            case Code.Ldelema:
            {
                var idx = Pop();
                var arr = Pop();
                Push(arr is ArrV a && idx is Num n ? new ElemAddr(a, Convert.ToInt32(n.V)) : Unk.Any);
                break;
            }
            case Code.Ldelem_Any: case Code.Ldelem_I: case Code.Ldelem_I1: case Code.Ldelem_I2: case Code.Ldelem_I4:
            case Code.Ldelem_I8: case Code.Ldelem_R4: case Code.Ldelem_R8: case Code.Ldelem_Ref: case Code.Ldelem_U1:
            case Code.Ldelem_U2: case Code.Ldelem_U4:
            {
                var idx = Pop();
                var arr = Pop();
                if (arr is ArrV a && idx is Num n && a.Items.TryGetValue(Convert.ToInt32(n.V), out var item)) Push(item);
                else Push(new Unk(null, Clip($"{Render(arr)}[{Render(idx)}]")));
                break;
            }
            case Code.Stelem_Any: case Code.Stelem_I: case Code.Stelem_I1: case Code.Stelem_I2: case Code.Stelem_I4:
            case Code.Stelem_I8: case Code.Stelem_R4: case Code.Stelem_R8: case Code.Stelem_Ref:
            {
                var v = Pop();
                var idx = Pop();
                var arr = Pop();
                if (arr is ArrV a && idx is Num n) a.Items[Convert.ToInt32(n.V)] = v;
                break;
            }
            case Code.Stobj:
            {
                var v = Pop();
                if (Pop() is ElemAddr ea) ea.Arr.Items[ea.Index] = v;
                break;
            }
            case Code.Call:
            case Code.Callvirt:
            case Code.Newobj:
                DoCall(ins, (MethodReference)ins.Operand, stack, cache);
                break;
            default:
            {
                int pops = PopCount(ins, m, stack.Count);
                for (int i = 0; i < pops; i++) Pop();
                int pushes = PushCount(ins);
                for (int i = 0; i < pushes; i++) Push(Unk.Any);
                break;
            }
        }
        st.Stack = stack.ToArray();
        return Successors(ins);
    }

    static void DoCall(Instruction ins, MethodReference mr, List<Val> stack, Dictionary<int, Val> cache)
    {
        bool isNew = ins.OpCode.Code == Code.Newobj;
        int n = mr.Parameters.Count + (mr.HasThis && !isNew ? 1 : 0);
        n = Math.Min(n, stack.Count);
        var args = stack.GetRange(stack.Count - n, n).ToArray();
        stack.RemoveRange(stack.Count - n, n);

        var result = Intrinsic(ins, mr, args, isNew, cache);
        if (isNew) stack.Add(result ?? NewObj(ins, mr, args, cache));
        else if (mr.ReturnType.MetadataType != MetadataType.Void)
            stack.Add(result ?? new Unk(mr.ReturnType, Clip(CallSrc(mr, args))));
    }

    static string CallSrc(MethodReference mr, Val[] args)
    {
        var name = ShortName(mr);
        if (mr.Name.StartsWith("get_")) return name;
        int skip = mr.HasThis ? 1 : 0;
        return $"{name}({string.Join(", ", args.Skip(skip).Select(Render))})";
    }

    static Val NewObj(Instruction ins, MethodReference ctor, Val[] args, Dictionary<int, Val> cache)
    {
        var dt = ctor.DeclaringType;
        var bare = dt is GenericInstanceType g ? g.ElementType.Name : dt.Name;
        if (CollectionTypes.Contains(bare))
        {
            if (!cache.TryGetValue(ins.Offset, out var c)) cache[ins.Offset] = c = new CollV(dt);
            if (args.Length > 0 && args[0] is ArrV src && c is CollV cv)
                foreach (var (k, v) in src.Items) cv.Entries[-1 - k] = (null, v);
            return c;
        }
        if (args.Any(a => a is Num or Strs or ObjV or ArrV)) return new ObjV(ctor, args);
        return new Unk(dt, $"new {dt.Name}()");
    }

    static Val? Intrinsic(Instruction ins, MethodReference mr, Val[] args, bool isNew, Dictionary<int, Val> cache)
    {
        var dt = mr.DeclaringType.FullName;
        var name = mr.Name;
        if (dt == "System.String")
        {
            switch (name)
            {
                case "Concat":
                    var parts = args.Length == 1 && args[0] is ArrV arr
                        ? Enumerable.Range(0, arr.Len ?? arr.Items.Count).Select(i => arr.Items.GetValueOrDefault(i, Unk.Any)).ToArray()
                        : args;
                    return Concat(parts);
                case "Format" when args.Length > 0:
                    var fmt = args[0] is Strs ? args[0] : args.Length > 1 ? args[1] : null; // Format(IFormatProvider, ...)
                    if (fmt is Strs fs)
                        return new Strs(new SortedSet<string>(fs.Set.Select(FormatToPattern), StringComparer.Ordinal), fs.Via, fs.Partial);
                    return null;
                case "ToLower" or "ToLowerInvariant" or "ToUpper" or "ToUpperInvariant" or "Trim" when args.Length >= 1 && args[0] is Strs s:
                    Func<string, string> f = name switch
                    {
                        "ToLower" or "ToLowerInvariant" => x => x.ToLowerInvariant(),
                        "ToUpper" or "ToUpperInvariant" => x => x.ToUpperInvariant(),
                        _ => x => x.Trim(),
                    };
                    return new Strs(new SortedSet<string>(s.Set.Select(f), StringComparer.Ordinal), s.Via, s.Partial);
            }
        }
        if (name == "ToString" && args.Length == 1 && !isNew)
        {
            var t = ins.Previous?.OpCode.Code == Code.Constrained ? (TypeReference)ins.Previous.Operand
                : args[0] switch { Num nv => nv.Type, Unk u => u.Type, ArgV a => a.Type, _ => null };
            var td = SafeResolve(t);
            if (td is { IsEnum: true })
            {
                var members = td.Fields.Where(f => f.HasConstant).ToList();
                if (args[0] is Num num)
                {
                    var hit = members.FirstOrDefault(f => ToLong(f.Constant) == ToLong(num.V));
                    if (hit != null) return new Strs(new SortedSet<string>(StringComparer.Ordinal) { hit.Name }, "enum " + td.Name);
                }
                if (members.Count is > 0 and <= MaxSet)
                    return new Strs(new SortedSet<string>(members.Select(f => f.Name), StringComparer.Ordinal), "enum " + td.Name, true);
            }
            if (args[0] is Num plain && t == null) return new Strs(Convert.ToString(plain.V, CultureInfo.InvariantCulture)!);
            return null;
        }
        if (dt == "System.Runtime.CompilerServices.RuntimeHelpers" && name == "InitializeArray" && args.Length == 2)
        {
            if (args[0] is ArrV a && args[1] is TokV { Member: FieldReference fr } && SafeResolve(fr) is { } fd)
                a.Blob = fd.InitialValue;
            return null;
        }
        if (!isNew && args.Length >= 2 && args[0] is CollV cv
            && name is "Add" or "set_Item" or "TryAdd" or "Push" or "Enqueue")
        {
            cv.Entries[ins.Offset] = args.Length >= 3 ? (args[1], args[2]) : (null, args[1]);
            return null;
        }
        if (!isNew && name == ".ctor" && args.Length >= 1 && args[0] is ElemAddr ea)
        {
            ea.Arr.Items[ea.Index] = new ObjV(mr, args[1..]);
            return null;
        }
        return null;
    }

    static Val Concat(Val[] parts)
    {
        var acc = new List<string> { "" };
        bool anyLiteral = false;
        string? via = null;
        foreach (var p in parts)
        {
            IEnumerable<string> opts;
            switch (p)
            {
                case Strs s:
                    opts = s.Set;
                    via ??= s.Via;
                    anyLiteral |= s.Set.Any(x => x.Replace("{*}", "").Length > 0);
                    break;
                case Num n:
                    opts = [Convert.ToString(n.V, CultureInfo.InvariantCulture)!];
                    break;
                default:
                    opts = ["{*}"];
                    break;
            }
            var next = new List<string>();
            foreach (var a in acc)
            foreach (var o in opts)
                next.Add(a + o);
            if (next.Count > MaxSet) return Unk.Any;
            acc = next;
        }
        if (!anyLiteral) return new Unk(null, Clip(string.Join(" + ", parts.Select(Render))));
        return new Strs(new SortedSet<string>(acc.Select(CollapseStars), StringComparer.Ordinal), via);
    }

    static string CollapseStars(string s)
    {
        while (s.Contains("{*}{*}")) s = s.Replace("{*}{*}", "{*}");
        return s;
    }

    static string FormatToPattern(string fmt)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < fmt.Length; i++)
        {
            if (fmt[i] == '{' && i + 1 < fmt.Length && fmt[i + 1] == '{') { sb.Append('{'); i++; continue; }
            if (fmt[i] == '}' && i + 1 < fmt.Length && fmt[i + 1] == '}') { sb.Append('}'); i++; continue; }
            if (fmt[i] == '{')
            {
                int end = fmt.IndexOf('}', i);
                if (end < 0) { sb.Append(fmt[i..]); break; }
                sb.Append("{*}");
                i = end;
                continue;
            }
            sb.Append(fmt[i]);
        }
        return CollapseStars(sb.ToString());
    }

    static string OpSymbol(Code c) => c switch
    {
        Code.Add or Code.Add_Ovf or Code.Add_Ovf_Un => "+",
        Code.Sub or Code.Sub_Ovf or Code.Sub_Ovf_Un => "-",
        Code.Mul or Code.Mul_Ovf or Code.Mul_Ovf_Un => "*",
        Code.Div or Code.Div_Un => "/",
        Code.Rem or Code.Rem_Un => "%",
        Code.And => "&", Code.Or => "|", Code.Xor => "^",
        Code.Shl => "<<", Code.Shr or Code.Shr_Un => ">>",
        Code.Ceq => "==", Code.Cgt or Code.Cgt_Un => ">", Code.Clt or Code.Clt_Un => "<",
        _ => c.ToString(),
    };

    public static TypeDefinition? SafeResolve(TypeReference? t)
    {
        if (t == null) return null;
        try { return t.Resolve(); } catch { return null; }
    }

    static FieldDefinition? SafeResolve(FieldReference f)
    {
        try { return f.Resolve(); } catch { return null; }
    }

    public static string ShortName(MemberReference m)
    {
        var dt = m.DeclaringType?.Name ?? "";
        // hoisted iterator/closure fields: <time>5__2 -> time, <>4__this -> this
        if (m is FieldReference && dt.StartsWith('<'))
            return m.Name == "<>4__this" ? "this" : Sites.Unmangle(m.Name) ?? m.Name;
        if (m.Name == ".ctor") return dt;
        var n = m.Name.StartsWith("get_") ? m.Name[4..] : m.Name;
        return dt.Length > 0 ? dt + "." + n : n;
    }

    public static long ToLong(object v) => v is ulong u ? unchecked((long)u) : Convert.ToInt64(v, CultureInfo.InvariantCulture);

    public static string Clip(string s) => s.Length > MaxSrc ? s[..(MaxSrc - 3)] + "..." : s;

    public static string Num2S(object v) => v switch
    {
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        _ => Convert.ToString(v, CultureInfo.InvariantCulture)!,
    };

    public static string Render(Val v) => v switch
    {
        Num n when n.Type?.MetadataType == MetadataType.Boolean => ToLong(n.V) != 0 ? "true" : "false",
        Num n => n.Type is { } t && SafeResolve(t) is { IsEnum: true } td
            ? td.Fields.FirstOrDefault(f => f.HasConstant && ToLong(f.Constant) == ToLong(n.V)) is { } ef
                ? td.Name + "." + ef.Name : Num2S(n.V)
            : Num2S(n.V),
        Strs s when s.Set.Count == 1 && !s.Partial => Quote(s.Set.Min!),
        Strs s => "{" + string.Join("|", s.Set.Take(8).Select(Quote)) + (s.Set.Count > 8 || s.Partial ? "|..." : "") + "}",
        ArgV a => a.Name,
        Unk u => u.Src ?? "?",
        ObjV o => $"new {o.Ctor.DeclaringType.Name}({string.Join(", ", o.Args.Select(Render))})",
        ArrV a => $"{a.Elem.Name}[{a.Len?.ToString() ?? "?"}]",
        CollV c => $"new {c.Type.Name}",
        _ => "?",
    };

    public static string Quote(string s) => System.Text.Json.JsonSerializer.Serialize(s, Program.Json);
}
