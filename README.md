# MycopunkDumper

A BepInEx 5 plugin for the Unity game [Mycopunk](https://store.steampowered.com/app/3247750/) that dumps the entire in-game catalog — every upgrade, gear, character, mission, enemy, loot pool, status effect, localization string, and more — into a single `data.json`.

Used to power external tools (wikis, build calculators, skin browsers) without scraping the game at runtime.

## What gets dumped

`data.json` is a sorted, deterministic snapshot of the game's `ScriptableObject` and `MonoBehaviour` catalogs. Top-level sections include:

| Section | Contents |
|---|---|
| `gameVersion` | `{Version, BuildID, DumpedAt}` — provenance |
| `upgrades` | every grid/skill-tree/cosmetic upgrade with resolved stats per upgradable |
| `gears` / `characters` | gear definitions and the four playable characters |
| `missions` / `missionModifiers` / `missionContainers` | mission catalog + modifier rules + weekly schedules |
| `enemies` / `enemyGroups` / `customWaves` | full combat-stat catalog |
| `statusEffects` / `stacks` / `threats` / `threatProfiles` | combat tuning |
| `lootPools` / `directives` / `globalEvents` / `encounters` | content gating + drops |
| `resources` / `rarities` / `crafting` | economy tables |
| `dialogue` / `quips` / `localization` | text catalogs |
| `upgradePresets` | skin-modifier presets (Bloodmetal, Spectral, Coppertone, …) |
| `formulas` / `achievements` / `steamStats` | hardcoded constants scraped from source |

A formal **JSON Schema (Draft 2020-12)** description is at [`data.schema.json`](./data.schema.json). [`SCHEMA_CHANGES.md`](./SCHEMA_CHANGES.md) tracks shape changes vs. earlier dumps.

## Prerequisites

- [`mise`](https://mise.jdx.dev) for tool / task management (pins .NET 8, sets env)
- A local Mycopunk install (Steam Linux default works out of the box)
- Steam running (the dump task launches the game via Steam URI)
- The following Steam launch options on Mycopunk (Properties → Launch Options):

  ```
  WINEDLLOVERRIDES="winhttp.dll=n,b" %command% -batchmode -nographics
  ```

  - `winhttp.dll=n,b` lets the BepInEx `winhttp.dll` shim load under Proton.
  - `-batchmode -nographics` makes Unity skip rendering; the plugin's `Start()` writes `data.json` and the game self-exits (~20s end-to-end).
  - Drop `-batchmode -nographics` only when you want to run `mise run render-skins` (which needs a real GPU context).

`MYCOPUNK_DIR` defaults to the Steam-Linux install path. Override it in `mise.toml` (or in your shell) if your install lives elsewhere — the `.csproj` references resolve through `$(MYCOPUNK_DIR)`.

First-time `mise install` on a system without libicu needs `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 mise install`. The `[env]` block in `mise.toml` covers all subsequent invocations.

## Commands

All build & deploy flows are mise tasks.

| Command | What it does |
|---|---|
| `mise run build` | Debug build → `src/bin/Debug/netstandard2.1/MycopunkDumper.dll` |
| `mise run release` | Release build → `src/bin/Release/netstandard2.1/MycopunkDumper.dll` |
| `mise run clean` | `dotnet clean src` |
| `mise run deploy` | Release + copy DLL into `$MYCOPUNK_DIR/BepInEx/plugins` |
| `mise run dump` | Deploy + headless launch + wait for `data.json` (~20s) |
| `mise run copy-dump` | Copy `$MYCOPUNK_DIR/data.json` into the repo |
| `mise run probe` | Like `dump` but with `Probe.cs` enabled — writes `$MYCOPUNK_DIR/probe/{types.md, catalogs.txt, subclasses.txt, samples/}` for exploration before adding a new dumper |
| `mise run diff <old.json> <new.json> [section[/key]]` | Side-by-side diff of two dumps (strips noisy `instanceID`/`ID`) |
| `mise run decompile` | Re-decompile `Assembly-CSharp.dll` into `./decompiled/` |
| `mise run extract-labels` | Static-analyze decompiled `UpgradeProperty_*.cs` for localization keys hidden behind switches/conditionals → `property-labels.json` (auto-merged into the next `mise run dump`) |
| `mise run extract-assets` | AssetRipper extraction of textures + everything else into `~/MycopunkExtracted/<version>/` |
| `mise run render-skins` | In-game render-to-texture turntables of every `(gear, skin, modifier)` combo, encoded to mp4 via ffmpeg |
| `mise run release-dump` | Publish the dump into a `mycopunk-data` repo: gzip + CHANGES.md + index refresh |
| `mise run release-version` | End-to-end pipeline: decompile → build → dump → validate → release → extract-assets |

### Decompiled source

`mise run decompile` populates `./decompiled/` (gitignored) from the current `Assembly-CSharp.dll`. The decompiled tree is excluded from compilation but is the fastest reference when adding a new dumper — grep for the type, read the source, design the DTO. Re-run after every game update.

One-time setup (newer `ilspycmd` packages have a broken tool manifest — pin `8.2.0.7535`):

```bash
mise install dotnet@6
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 mise exec dotnet@6 -- \
  dotnet tool install -g --version 8.2.0.7535 ilspycmd
```

### Schema validation

```bash
mise exec bun -- bun x ajv-cli@latest validate \
  --spec=draft2020 \
  -s data.schema.json \
  -d $MYCOPUNK_DIR/data.json
```

The `--spec=draft2020` flag is required — the schema uses 2020-12 features (default ajv-cli is draft-07).

## Architecture

Single-purpose plugin laid out in four buckets under `src/`:

```
src/
  MycopunkDumper.csproj
  Plugin.cs              # BepInEx entry: Awake/Start orchestration + JSON write
  Probe.cs               # gated by probe.flag — type/subclass/sample dumps for scaffolding
  SkinRenderer.cs        # gated by render-skins.flag — entry + RenderAll orchestration
  Rendering/             # partial SkinRenderer/RenderDriver: setup, frame, teardown, helpers
  Builders/              # partial Plugin class: per-catalog Build<X> methods
    Builders.Common.cs       # shared helpers (GetPrivateField, BuildIcon, LocText, …)
    Builders.Skins.cs        # ProcessUpgrades, BuildSkin/Modifier, PopulateSkinPreviews
    Builders.Gears.cs        # BuildGear/Character/Quip/LevelUnlock
    Builders.Resources.cs    # BuildResource/Rarities/Crafting
    Builders.Missions.cs     # BuildMission/Objective/MissionModifier/Region
    Builders.Enemies.cs      # BuildEnemy/CustomWave/EnemyGroup
    Builders.Combat.cs       # BuildStatusEffect/Stack/Threat/LootPool
    Builders.Directives.cs   # BuildDirective/GlobalEvent
    Builders.Misc.cs         # BuildCollectable/Dialogue/QuipEntry
  Models/                # one file per top-level DTO (Upgrade.cs, Gear.cs, Mission.cs, …)
  Serialization/
    NativeConverter.cs   # Newtonsoft converter for Unity-native types
```

Repo-root siblings (not source):
- `mise.toml` + `mise-tasks/` — build / dump / probe / render-skins / release pipeline
- `tools/` + `scripts/` — Python helpers (diff.py, etc.)
- `decompiled/` — gitignored; populated by `mise run decompile` and read for reference
- `data.schema.json` + `SCHEMA_CHANGES.md` — consumer-facing schema + change log

- **`Plugin.cs`** — BepInEx entry point. `Start()` walks `Global.Instance.AllGear`, `Global.Instance.Characters`, and the global ScriptableObject catalogs (`Resources.FindObjectsOfTypeAll<T>()`), then serializes to `Paths.GameRootPath/data.json` via Newtonsoft.Json. The `Plugin` class is `partial` — every `Build<X>` lives in `Builders/Builders.<Section>.cs`.
- **`Probe.cs`** — runtime introspection helper, gated by `$MYCOPUNK_DIR/probe.flag`. Dumps type maps, subclass discovery, and SO samples into `$MYCOPUNK_DIR/probe/` to scaffold new dumpers.
- **`SkinRenderer.cs` + `Rendering/`** — render-skins-flag-gated routine that drives the game's render-to-texture path to produce one 360° turntable per `(gear, skin, modifier)` combination plus a no-modifier base. `SkinRenderer.cs` holds the `Run()` entry, the nested `RenderDriver` MonoBehaviour, and the `RenderAll` orchestration coroutine; `Rendering/SkinRenderer.{Setup,Frame,Teardown,Helpers}.cs` are partial files with the per-skin setup, per-frame capture, restore, and reusable helpers (bounds compute, layer recursion, manual VFX-crab spawn). Outputs JPGs that `mise-tasks/render-skins` then encodes to mp4.
- **`Serialization/NativeConverter.cs`** — Newtonsoft `JsonConverter` for Unity-native types (`HexMap`, `UpgradeProperty`, `DirectiveProperty`). Delegates to `JsonUtility.ToJson`, post-processes Unity's bare `Infinity`/`NaN` tokens into RFC 8259-valid JSON, and enriches every `{"instanceID": N}` with a `"@ref": "<type>:<key>"` cross-reference.
- **`Models/*.cs`** (DTOs) — one file per top-level catalog (`Upgrade.cs`, `Gear.cs`, `Mission.cs`, `Enemy.cs`, …). Game enums are flattened to strings; cycles are dropped via Newtonsoft's `ReferenceLoopHandling.Ignore`.

The csproj uses `BepInEx.AssemblyPublicizer.MSBuild` (`Publicize="true"`) so private game members are accessible at compile time — that's why code can touch `Global.Instance.AllGear`, `up.Properties.properties`, etc.

For deeper notes (game-API quirks, runtime patterns for new dumpers, shell aliases on this dev box), see [`CLAUDE.md`](./CLAUDE.md).

## License

No license attached yet — game data dumps are derivative of the original game; treat as research/fan content.
