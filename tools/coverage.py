#!/usr/bin/env python3
"""Cross-reference probe output against the plugin source to list what the dump doesn't cover.

Usage: coverage.py <probe_dir> <src_dir> <decompiled_dir>

A type counts as covered when it, or any ancestor (resolved from decompiled source), appears as
an identifier anywhere in src/ — e.g. OuroMission is covered because Mission is dumped. A Global
field counts as covered when its name appears in src/. Both are heuristics that err toward
"covered"; Probe.cs is excluded from the search so probe-only mentions don't count.
"""
import pathlib
import re
import sys

probe, src, decompiled = map(pathlib.Path, sys.argv[1:4])
code = "\n".join(p.read_text() for p in src.rglob("*.cs") if p.name != "Probe.cs" and "/bin/" not in str(p) and "/obj/" not in str(p))
idents = set(re.findall(r"\b\w+\b", code))

bases = {}
for p in decompiled.rglob("*.cs"):
    for m in re.finditer(r"\bclass (\w+)(?:<[^>]*>)?\s*:\s*([\w.<>, ]+?)\s*(?:where\b|\{|$)", p.read_text(errors="ignore"), re.M):
        parents = [re.sub(r"<.*", "", x).split(".")[-1] for x in re.split(r",\s*(?![^<]*>)", m[2])]
        bases.setdefault(m[1], parents)

# Engine/infrastructure bases that say nothing about whether the dump covers a type.
GENERIC = {"ScriptableObject", "MonoBehaviour", "NetworkBehaviour", "Object", "Component", "Behaviour"}


def short(full):
    return re.split(r"[.+]", re.sub(r"<.*", "", full))[-1]


def ancestors(t):
    out, todo = [], list(bases.get(t, []))
    while todo:
        x = todo.pop(0)
        if x in GENERIC or x in out:
            continue
        out.append(x)
        todo += bases.get(x, [])
    return out


def covered(t):
    return any(x in idents for x in [t] + ancestors(t))


def chain(t):
    return ", ".join(ancestors(t))


print("## ScriptableObject catalogs not covered by the dumper")
print("# loaded afterLoadAll  type  [ancestors]   (loaded=0 → needs Resources.LoadAll)")
for line in (probe / "catalogs.txt").read_text().splitlines():
    m = re.match(r"\s*(\d+)\s+(\d+)\s+(\S+)$", line)
    if m and not covered(short(m[3])):
        print(f"{m[1]:>6} {m[2]:>6}  {m[3]}  [{chain(short(m[3]))}]")

print("\n## Global fields not referenced by the dumper")
for line in (probe / "global-fields.txt").read_text().splitlines():
    m = re.match(r"(\w+) : (.*?) = (.*)$", line)
    if m and m[1] not in idents:
        print(f"{m[1]} : {m[2]} = {m[3][:120]}")

print("\n## MonoBehaviour types (top 60 by count) not covered by the dumper")
rows = []
for line in (probe / "components.txt").read_text().splitlines():
    m = re.match(r"\s*(\d+)\s+(\S+)$", line)
    if m and not covered(short(m[2])):
        rows.append((int(m[1]), m[2]))
for n, t in rows[:60]:
    print(f"{n:>6}  {t}")
print(f"# {len(rows)} unreferenced MonoBehaviour types total")


# Post-dump probe aggregates (ProbeContent.cs): "<count>  <type>  <comma-separated scenes|bundles>"
def aggregate(name):
    path = probe / name
    if not path.exists():
        return []
    return [(int(m[1]), m[2], m[3]) for line in path.read_text().splitlines() if (m := re.match(r"\s*(\d+)\s+(\S+)\s+(\S+)$", line))]


startup = {m[2] for line in (probe / "components.txt").read_text().splitlines() if (m := re.match(r"\s*(\d+)\s+(\S+)$", line))}
startup_so = {m[3] for line in (probe / "catalogs.txt").read_text().splitlines() if (m := re.match(r"\s*(\d+)\s+(\d+)\s+(\S+)$", line))}

print("\n## Game MonoBehaviour types found only in build scenes, not covered by the dumper")
print("# count  type  [ancestors]  scenes")
rows = [r for r in aggregate("scene-components.txt") if r[1] not in startup and short(r[1]) in bases and not covered(short(r[1]))]
for n, t, where in rows:
    print(f"{n:>6}  {t}  [{chain(short(t))}]  {where}")
print(f"# {len(rows)} scene-only unreferenced types")

print("\n## ScriptableObject types only visible while a build scene is loaded, not covered by the dumper")
for n, t, where in aggregate("scene-catalogs.txt"):
    if t not in startup_so and not covered(short(t)):
        print(f"{n:>6}  {t}  [{chain(short(t))}]  {where}")

print("\n## Asset types in StreamingAssets bundles not referenced by the dumper")
for n, t, where in aggregate("bundle-assets.txt"):
    if not covered(short(t)):
        print(f"{n:>6}  {t}  [{chain(short(t))}]  {where}")


# UI widgets dominate reachability via menu/HUD prefabs and are never dump material.
UI = {"Graphic", "MaskableGraphic", "Selectable", "Button", "UIBehaviour", "ICharacterGraphic", "ISettingsOption"}


def static_root_covered(paths):
    """All example paths start at a static field the dumper reads (e.g. TextBlocks.strings)."""
    roots = [re.match(r"static:[\w.+]+\.(\w+)", p) for p in paths]
    return bool(paths) and all(m and m[1] in idents for m in roots)


reach = probe / "reachability.txt"
if reach.exists():
    print("\n## Reachable game types with undumped instances (top 50, type + ancestors not in src/)")
    print("# uncovered  total  type  [ancestors]   → example path")
    entries, cur = [], None
    for line in reach.read_text().splitlines():
        if line.startswith("## "):
            break
        m = re.match(r"\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s+(.+)$", line)
        if m:
            cur = [int(m[1]) - int(m[2]) - int(m[3]), int(m[1]), m[5], []]
            entries.append(cur)
        elif cur and line.startswith("    path: "):
            cur[3].append(line[len("    path: "):])
    rows, ui = [], 0
    for e in entries:
        t = short(e[2])
        if e[0] <= 0 or covered(t) or static_root_covered(e[3]):
            continue
        if e[2].startswith("Pigeon.UI.") or UI & set(ancestors(t)):
            ui += 1
            continue
        rows.append(e)
    rows.sort(key=lambda r: -r[0])
    for unc, n, t, paths in rows[:50]:
        print(f"{unc:>8} {n:>6}  {t}  [{chain(short(t))}]\n           → {paths[0] if paths else '?'}")
    print(f"# {len(rows)} uncovered reachable types total ({ui} UI widget types hidden)")

unemitted = probe / "unemitted-fields.txt"
if unemitted.exists():
    print("\n## Fields on dumped types with values but never emitted (name not in data.json keys or src/)")
    for line in unemitted.read_text().splitlines():
        m = re.match(r"\s*\d+/\d+\s+(\S+)\.(\w+) :", line)
        if m and m[2] not in idents:
            print(line.strip())

statics = probe / "static-fields.txt"
if statics.exists():
    print("\n## Static collections (count > 0) not referenced by the dumper")
    owner = None
    for line in statics.read_text().splitlines():
        if line.startswith("## "):
            owner = line[3:]
        m = re.match(r"\s+(?:const |readonly )?(\w+) : .*? = <.*? count=(\d+)>", line)
        # skip scratch buffers/pools — runtime plumbing, not data
        if m and int(m[2]) > 0 and m[1] not in idents and not re.search(r"(?i)buffer|pool", m[1]) and " : Char[]" not in line:
            print(f"{owner}.{line.strip()[:200]}")
