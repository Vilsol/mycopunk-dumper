#!/usr/bin/env python3
"""Merge property-labels.json into a dumped data.json in-place."""
import json
import sys


def main() -> int:
    if len(sys.argv) != 3:
        print("usage: dump-enrich.py <data.json> <property-labels.json>", file=sys.stderr)
        return 2
    data_path, labels_path = sys.argv[1], sys.argv[2]
    with open(data_path) as f:
        d = json.load(f)
    with open(labels_path) as f:
        labels = json.load(f)

    n_enriched = 0
    for upgrade in d.get("upgrades", {}).values():
        for p in upgrade.get("Properties") or []:
            info = labels.get(p.get("Type"))
            if info and info.get("localization_keys"):
                p["LocalizationKeys"] = info["localization_keys"]
                n_enriched += 1
    for directive in d.get("directives", {}).values():
        for p in directive.get("Properties") or []:
            info = labels.get(p.get("Type"))
            if info and info.get("localization_keys"):
                p["LocalizationKeys"] = info["localization_keys"]
                n_enriched += 1

    with open(data_path, "w") as f:
        json.dump(d, f, separators=(",", ":"))
    print(f"Enriched {n_enriched} property entries with LocalizationKeys from {labels_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
