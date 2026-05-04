#!/usr/bin/env python3
"""
Side-by-side diff for two MycopunkDumper data.json files.

- Strips noisy keys (`instanceID`, `ID` by default; extend via `--exclude-key`).
- Top-level summary: per-section added / removed / changed counts.
- Per-changed-entry: difftastic-style two-column view with gutter markers
  (`-` removed, `+` added, `~` replace, `…` elided), JSON-path-labeled context
  elision, and pattern-collapse for systematic changes (e.g. icon-atlas migrations).

Usage:
    tools/diff.py [options] <old.json> <new.json> [section[/key]]

    tools/diff.py data.json $MYCOPUNK_DIR/data.json                # full diff
    tools/diff.py data.json new.json missions                      # one section
    tools/diff.py data.json new.json upgrades/99770                # one entry
    tools/diff.py --epsilon=1e-5 data.json new.json                # ignore float drift
    tools/diff.py --exclude-key BuildID --exclude-key DumpedAt …   # ignore extra keys
    tools/diff.py --no-collapse data.json new.json                 # disable pattern collapse
"""
from __future__ import annotations

import argparse
import difflib
import json
import os
import re
import shutil
import sys
from typing import Any

DEFAULT_EXCLUDE_KEYS = {"instanceID", "ID"}

RESET = "\033[0m"
BOLD = "\033[1m"
DIM = "\033[2m"
RED = "\033[31m"
GREEN = "\033[32m"
YELLOW = "\033[33m"
CYAN = "\033[36m"

USE_COLOR = sys.stdout.isatty() and os.environ.get("NO_COLOR") is None

# Strict SGR parser: \x1b[ <digits;>* m
SGR_RE = re.compile(r"\x1b\[[\d;]*m")


def c(text: str, *codes: str) -> str:
    if not USE_COLOR:
        return text
    return "".join(codes) + text + RESET


def _scan(s: str, i: int) -> int:
    """If s[i:] starts with a CSI SGR sequence, return its end index. Else i."""
    if i >= len(s) or s[i] != "\x1b":
        return i
    m = SGR_RE.match(s, i)
    return m.end() if m else i


def visible_len(s: str) -> int:
    out = 0
    i = 0
    while i < len(s):
        nxt = _scan(s, i)
        if nxt > i:
            i = nxt
            continue
        out += 1
        i += 1
    return out


def pad(s: str, width: int) -> str:
    extra = width - visible_len(s)
    if extra <= 0:
        return s
    return s + " " * extra


def truncate(s: str, width: int) -> str:
    if visible_len(s) <= width:
        return s
    out = []
    used = 0
    i = 0
    saw_color = False
    while i < len(s) and used < width - 1:
        nxt = _scan(s, i)
        if nxt > i:
            out.append(s[i:nxt])
            saw_color = saw_color or s[i:nxt] != RESET
            i = nxt
            continue
        out.append(s[i])
        used += 1
        i += 1
    if saw_color and USE_COLOR:
        out.append(RESET)
    out.append("…")
    return "".join(out)


def strip(node: Any, exclude: set[str], epsilon: float | None) -> Any:
    if isinstance(node, dict):
        return {k: strip(v, exclude, epsilon) for k, v in node.items() if k not in exclude}
    if isinstance(node, list):
        return [strip(x, exclude, epsilon) for x in node]
    if epsilon is not None and isinstance(node, float):
        # Quantize to a step of `epsilon`. Avoids 0.20000000298023224 ≠ 0.2 noise.
        if node == 0 or not (node == node and node not in (float("inf"), float("-inf"))):
            return node
        return round(node / epsilon) * epsilon
    return node


def fmt(node: Any) -> list[str]:
    return json.dumps(node, indent=2, sort_keys=True, ensure_ascii=False).splitlines()


def _extract_key(stripped: str) -> str | None:
    """Robustly extract the JSON key from a pretty-printed line.

    Handles `\\"` escapes inside the key (rare but valid). Returns None if the
    line isn't a `"key": value` line — string values containing `": ` won't
    fool this since we anchor on the closing quote.
    """
    if not stripped.startswith('"'):
        return None
    i = 1
    while i < len(stripped):
        if stripped[i] == "\\":
            i += 2
            continue
        if stripped[i] == '"':
            if stripped[i + 1 : i + 3] == ": ":
                return stripped[1:i].replace('\\"', '"')
            return None
        i += 1
    return None


def compute_paths(lines: list[str]) -> list[str]:
    """For each pretty-printed JSON line, return the JSON path of its leaf
    (or its enclosing container when the line is structural like `},`).

    Always rendered with a leading `›` separator so top-level (`› MissionFlags`)
    is visually distinct from nested (`› RawData › MissionFlags`).
    """
    paths: list[str] = []
    stack: list[tuple[int, str]] = []
    for line in lines:
        stripped = line.lstrip(" ")
        depth = (len(line) - len(stripped)) // 2
        while stack and stack[-1][0] >= depth:
            stack.pop()
        key = _extract_key(stripped)
        if key is not None:
            stack.append((depth, key))
        if stack:
            paths.append("› " + " › ".join(k for _, k in stack))
        else:
            paths.append("")
    return paths


# ----- side-by-side renderer --------------------------------------------------

GUTTER_WIDTH = 2  # "± "


def _gutter(tag: str, side: str) -> str:
    """Per-row gutter: makes the actual change find-able without color."""
    if tag == "equal":
        return "  "
    if tag == "replace":
        return c("~ ", RED) if side == "left" else c("~ ", GREEN)
    if tag == "delete":
        return c("- ", RED) if side == "left" else "  "
    if tag == "insert":
        return "  " if side == "left" else c("+ ", GREEN)
    return "  "


def side_by_side(left: list[str], right: list[str], width: int, context: int = 2) -> list[str]:
    """Difftastic-style alignment using SequenceMatcher opcodes."""
    matcher = difflib.SequenceMatcher(a=left, b=right, autojunk=False)
    rows: list[tuple[str | None, str | None, str, int | None, int | None]] = []
    for tag, i1, i2, j1, j2 in matcher.get_opcodes():
        if tag == "equal":
            for k in range(i2 - i1):
                rows.append((left[i1 + k], right[j1 + k], "equal", i1 + k, j1 + k))
        elif tag == "replace":
            la = left[i1:i2]
            ra = right[j1:j2]
            for k in range(max(len(la), len(ra))):
                li = i1 + k if k < len(la) else None
                ri = j1 + k if k < len(ra) else None
                lv = la[k] if k < len(la) else None
                rv = ra[k] if k < len(ra) else None
                rows.append((lv, rv, "replace", li, ri))
        elif tag == "delete":
            for k in range(i1, i2):
                rows.append((left[k], None, "delete", k, None))
        elif tag == "insert":
            for k in range(j1, j2):
                rows.append((None, right[k], "insert", None, k))

    left_paths = compute_paths(left)
    right_paths = compute_paths(right)

    keep = [t != "equal" for _, _, t, _, _ in rows]
    for i, (_, _, t, _, _) in enumerate(rows):
        if t != "equal":
            for j in range(max(0, i - context), min(len(rows), i + context + 1)):
                keep[j] = True

    half = (width - 3 - GUTTER_WIDTH * 2) // 2
    out: list[str] = []
    prev_kept = True
    for i, (l, r, tag, li, ri) in enumerate(rows):
        if not keep[i]:
            if prev_kept:
                # Label gap with the path of the next *changed* row (skip context).
                next_i = next((j for j in range(i + 1, len(rows)) if rows[j][2] != "equal"), None)
                lp = ""
                rp = ""
                if next_i is not None:
                    if rows[next_i][3] is not None:
                        lp = left_paths[rows[next_i][3]]
                    if rows[next_i][4] is not None:
                        rp = right_paths[rows[next_i][4]]
                if not lp:
                    label = rp
                elif not rp or lp == rp:
                    label = lp
                else:
                    label = f"{lp}  ⇆  {rp}"
                left_lbl = c(label, DIM)
                right_lbl = c("", DIM)
                out.append(c("…", DIM) + " " + pad(truncate(left_lbl, half), half) + c(" │ ", DIM) + c("…", DIM) + " " + truncate(right_lbl, half))
            prev_kept = False
            continue
        prev_kept = True
        if tag == "equal":
            ls = c(l or "", DIM)
            rs = c(r or "", DIM)
        elif tag == "replace":
            ls = c(l, RED) if l is not None else c("~", DIM)
            rs = c(r, GREEN) if r is not None else c("~", DIM)
        elif tag == "delete":
            ls = c(l or "", RED)
            rs = c("", DIM)
        elif tag == "insert":
            ls = c("", DIM)
            rs = c(r or "", GREEN)
        else:
            ls = l or ""
            rs = r or ""
        # Gutter is shared across both columns — same tag means same change kind.
        out.append(_gutter(tag, "left") + pad(truncate(ls, half), half) + c(" │ ", DIM) + _gutter(tag, "right") + truncate(rs, half))
    return out


# ----- pattern-collapse (group changed entries by what fields they touch) -----


def changed_paths(old: Any, new: Any, prefix: str = "") -> set[str]:
    """Collect dotted JSON paths where `old` and `new` differ. Used to compute
    a per-entry signature so we can group entries with identical "shape of change"
    (e.g. 50 upgrades all migrating to a PixelSpriteSheet atlas)."""
    if old == new:
        return set()
    if isinstance(old, dict) and isinstance(new, dict):
        out: set[str] = set()
        for k in set(old) | set(new):
            sub = f"{prefix}.{k}" if prefix else k
            if k not in old or k not in new:
                out.add(sub)
            else:
                out |= changed_paths(old[k], new[k], sub)
        return out
    if isinstance(old, list) and isinstance(new, list):
        # Wildcard array indices — order-sensitive but indices treated as equivalent.
        out = set()
        n = max(len(old), len(new))
        for i in range(n):
            sub = f"{prefix}[]"
            if i >= len(old) or i >= len(new):
                out.add(sub)
            else:
                out |= changed_paths(old[i], new[i], sub)
        return out
    return {prefix or "."}


# ----- top-level driver -------------------------------------------------------


def diff_section(
    section: str,
    old: dict,
    new: dict,
    only_key: str | None,
    width: int,
    context: int,
    collapse: bool,
) -> None:
    if not isinstance(old, dict) or not isinstance(new, dict):
        if old != new:
            print(c(f"\n{section}", BOLD, CYAN))
            for line in side_by_side(fmt(old), fmt(new), width, context):
                print(line)
        return

    old_keys = set(old.keys())
    new_keys = set(new.keys())
    added = sorted(new_keys - old_keys)
    removed = sorted(old_keys - new_keys)
    common = sorted(old_keys & new_keys)
    changed = [k for k in common if old[k] != new[k]]

    if only_key is not None:
        if only_key not in old_keys and only_key not in new_keys:
            print(c(f"{section}/{only_key}: not present in either dump", YELLOW))
            return
        added = [k for k in added if k == only_key]
        removed = [k for k in removed if k == only_key]
        changed = [k for k in changed if k == only_key]
        collapse = False  # full detail when explicitly filtered

    if not (added or removed or changed):
        return

    print(c(f"\n━━━ {section} ", BOLD, CYAN) + c(f"+{len(added)} -{len(removed)} ~{len(changed)}", BOLD))
    if removed:
        print(c(f"  removed: {', '.join(removed)}", RED))
    if added:
        print(c(f"  added:   {', '.join(added)}", GREEN))

    for k in added:
        if only_key is None and len(fmt(new[k])) > 50:
            print(c(f"\n  + {section}/{k}", BOLD, GREEN) + c(f"  ({len(fmt(new[k]))} lines, use `{section}/{k}` to expand)", DIM))
            continue
        print(c(f"\n  + {section}/{k}", BOLD, GREEN))
        for line in fmt(new[k]):
            print("    " + c(line, GREEN))

    for k in removed:
        if only_key is None and len(fmt(old[k])) > 50:
            print(c(f"\n  - {section}/{k}", BOLD, RED) + c(f"  ({len(fmt(old[k]))} lines, use `{section}/{k}` to expand)", DIM))
            continue
        print(c(f"\n  - {section}/{k}", BOLD, RED))
        for line in fmt(old[k]):
            print("    " + c(line, RED))

    # Group changed entries by the *set of paths* they touch — entries with
    # identical shape of change collapse to one expanded representative.
    groups: dict[tuple, list[str]] = {}
    for k in changed:
        sig = tuple(sorted(changed_paths(old[k], new[k])))
        groups.setdefault(sig, []).append(k)

    for sig, keys in groups.items():
        if collapse and len(keys) > 1 and sig:  # only group if there's a real shared shape
            rep = min(keys, key=lambda k: len(fmt(old[k])) + len(fmt(new[k])))
            paths_summary = ", ".join(sig[:5]) + (f" + {len(sig) - 5} more" if len(sig) > 5 else "")
            print(c(f"\n  ~ {section}/{rep}", BOLD, YELLOW) + c(f"  (+{len(keys) - 1} entries with same shape)", DIM))
            print(c(f"    shape: {paths_summary}", DIM))
            print(c(f"    also affects: {', '.join(k for k in keys if k != rep)}", DIM))
            for line in side_by_side(fmt(old[rep]), fmt(new[rep]), width, context):
                print("    " + line)
        else:
            for k in keys:
                print(c(f"\n  ~ {section}/{k}", BOLD, YELLOW))
                for line in side_by_side(fmt(old[k]), fmt(new[k]), width, context):
                    print("    " + line)


def main() -> int:
    p = argparse.ArgumentParser(
        prog="diff.py",
        description="Side-by-side diff for two MycopunkDumper data.json files.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="See module docstring for examples.",
    )
    p.add_argument("old", help="path to the older data.json")
    p.add_argument("new", help="path to the newer data.json")
    p.add_argument("filter", nargs="?", default=None, help="section or section/key to limit the diff to")
    p.add_argument("--exclude-key", action="append", default=[], metavar="KEY",
                   help=f"additional dict key to strip recursively (defaults: {sorted(DEFAULT_EXCLUDE_KEYS)})")
    p.add_argument("--epsilon", type=float, default=None, metavar="EPS",
                   help="quantize floats to this step before diffing (e.g. 1e-5 to suppress Unity float drift)")
    p.add_argument("--context", "-C", type=int, default=2, metavar="N",
                   help="lines of context around each change (default 2)")
    p.add_argument("--no-collapse", action="store_true",
                   help="disable pattern-collapse — render every changed entry independently")
    args = p.parse_args()

    exclude = DEFAULT_EXCLUDE_KEYS | set(args.exclude_key)

    with open(args.old) as f:
        old = strip(json.load(f), exclude, args.epsilon)
    with open(args.new) as f:
        new = strip(json.load(f), exclude, args.epsilon)

    width = shutil.get_terminal_size((160, 24)).columns
    if width < 80:
        width = 160  # fall back to a fixed width when piping

    only_section: str | None = None
    only_key: str | None = None
    if args.filter:
        if "/" in args.filter:
            only_section, only_key = args.filter.split("/", 1)
        else:
            only_section = args.filter

    collapse = not args.no_collapse

    if isinstance(old, dict) and isinstance(new, dict):
        sections = sorted(set(old.keys()) | set(new.keys()))
        for section in sections:
            if only_section and section != only_section:
                continue
            diff_section(section, old.get(section, {}), new.get(section, {}), only_key, width - 4, args.context, collapse)
    else:
        for line in side_by_side(fmt(old), fmt(new), width, args.context):
            print(line)
    return 0


if __name__ == "__main__":
    sys.exit(main())
