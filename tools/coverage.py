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
    return re.split(r"[.+]", full)[-1]


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
