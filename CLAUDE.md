# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A BepInEx 5 plugin for the Unity game **Mycopunk** that dumps every in-game upgrade definition to `data.json`. Not a generic mod template despite the README — the README is leftover boilerplate from `MycopunkModTemplate`.

## Shell quirks

`grep`/`ls`/some other tools are aliased to `rtk`/`ugrep` wrappers. Wide globs (`grep "..." *.cs` over the 1700+ decompiled files) fail with `invalid argument -t` or `unexpected -P`. Use `command grep` for the system binary, or run extractions via inline `python3 - <<'EOF' ... EOF` which sidesteps the alias entirely. Surveys across decompiled source basically always need this.

## Build / dump cycle

`mise.toml` pins `dotnet@8.0.420` and sets `MYCOPUNK_DIR` / `MYCOPUNK_APPID` env vars. Build/clean tasks live inline in `mise.toml`; the multi-step bash tasks (`deploy`, `dump`, `copy-dump`) are file-based scripts in `./mise-tasks/` with `#MISE` frontmatter (`description=`, `depends=[...]`). **Multi-line scripts must be file tasks, not inline `run = '''...'''`** — mise's inline runner silently fails on backgrounded subprocesses and `if !` constructs, while file tasks just use bash directly. Run `mise trust` once.

```bash
mise run build       # Debug → src/bin/Debug/netstandard2.1/MycopunkDumper.dll
mise run release     # Release build → src/bin/Release/...
mise run clean
mise run deploy      # release + copy DLL into $MYCOPUNK_DIR/BepInEx/plugins
mise run dump        # deploy + headless launch via Steam URI + wait for data.json (~24s end-to-end)
mise run probe       # like dump but with the probe enabled (writes $MYCOPUNK_DIR/probe/)
mise run copy-dump   # copy $MYCOPUNK_DIR/data.json into the repo
mise run decompile   # re-decompile Assembly-CSharp.dll into ./decompiled/ (run after game updates)
mise run diff <old> <new> [section[/key]]  # side-by-side dump diff via tools/diff.py — strips noisy `instanceID`/`ID` keys
mise run ci-buildid  # CI-only: print current Steam public-branch manifest gid (needs STEAM_USERNAME/PASSWORD + DepotDownloader; assumes Steam Guard is disabled on the account)
mise run ci-dump     # CI-only: same contract as `dump` but launches Mycopunk.exe directly under Proton (needs $PROTON_DIR pointing at an extracted Proton install). Used by .github/workflows/release.yml.
```

`release-version` honours two env vars: `DUMP_TASK` (default `dump`; CI sets it to `ci-dump`) selects the dump task it invokes, and `STEAM_MANIFEST_ID` (passed through to `release-dump`) gets stamped onto the new `index.json` entry as `steamManifestId` so the autonomous workflow can no-op when the Steam manifest gid hasn't moved. Locally, both unset → behaviour is unchanged from before. The dump-time enrichment with `property-labels.json` lives in `scripts/dump-enrich.py` (shared by `dump` and `ci-dump`); modify it there if you change the merge logic. `release-version` now runs `mise run extract-labels` between decompile and build so the enrichment is always fresh.

`mise run dump` requires Steam already running and these Steam launch options on the game (Properties → Launch Options):

```
WINEDLLOVERRIDES="winhttp.dll=n,b" %command% -batchmode -nographics
```

`winhttp.dll=n,b` lets BepInEx's `winhttp.dll` shim get loaded under Proton (Wine's own `winhttp` would otherwise win). `-batchmode -nographics` makes Unity skip rendering and the game self-exits after `Start()` finishes — cold-start to dump is ~20s. **Watch for typos** like `windhttp` (extra `d`) — that silently breaks BepInEx loading and `LogOutput.log` won't be touched.

First-time install on a system without libicu needs `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 mise install` — the `[env]` block in `mise.toml` covers all subsequent runs.

The csproj `<HintPath>` references `$MYCOPUNK_DIR/Mycopunk_Data/Managed/Assembly-CSharp.dll`. On a different machine, update the path. The reference uses `Publicize="true"` (via `BepInEx.AssemblyPublicizer.MSBuild`) so private game members are accessible at compile time — that's why code can touch `Global.Instance.AllGear`, `up.Properties.properties`, etc.

`data.json` is written to `Paths.GameRootPath` (= the game install dir under Proton, visible at `$MYCOPUNK_DIR/data.json` on Linux).

There are no tests, no lint config, and no CI.

## Architecture

Single-purpose plugin. All compiled source lives under `src/`; repo-root holds tooling (`mise.toml`, `mise-tasks/`, `tools/`, `scripts/`), generated artifacts (`data.json`, `property-labels.json`, `decompiled/` — all gitignored), and consumer-facing schema (`data.schema.json`, `SCHEMA_CHANGES.md`).

```
src/
  MycopunkDumper.csproj
  Plugin.cs              # BepInEx entry: Awake/Start orchestration + JSON write
  Probe.cs               # gated by probe.flag — type/subclass/sample dumps for scaffolding
  SkinRenderer.cs        # gated by render-skins.flag — in-game turntable capture
  Builders/              # partial Plugin class: per-catalog Build<X> methods
  Rendering/             # partial SkinRenderer/RenderDriver: Setup, Frame, Teardown, Helpers
  Models/                # one file per top-level DTO
  Serialization/
    NativeConverter.cs   # Newtonsoft converter for Unity-native types
```

The `Plugin` class is `partial`. Every `Build<X>` and helper lives in `src/Builders/Builders.<Section>.cs` (Common, Skins, Gears, Resources, Missions, Enemies, Combat, Directives, Misc). The static field maps + `Awake/Start` stay in `src/Plugin.cs` itself. The same trick applies to `SkinRenderer` + nested `RenderDriver`, split across `src/Rendering/SkinRenderer.{Setup,Frame,Teardown,Helpers}.cs` plus the orchestration `RenderAll` coroutine in `src/SkinRenderer.cs`.

- **`src/Plugin.cs`** — `Start()` iterates `Global.Instance.AllGear` + `Global.Instance.Characters` + `Global.Instance` itself, calling `ProcessUpgrades` on each. Upgrades are deduplicated by `UpgradeKey(up.ID)` into a static `SortedDictionary<string, Upgrade>`. Per-upgradable data (`ApplicableTo` entries and `StatsByUpgradable` per property) is recorded on every pass, not just the first. Result is serialized via Newtonsoft.Json.
- **`src/Builders/Builders.Common.cs`** — shared helpers: `GetPrivateField` (walks the type hierarchy), `BuildIcon`, `LocText`, `UpgradeKey`, `PrettifyPropertyType`, `StripRichText`, `InjectPlainTextSiblings`, etc. Every other Builders.* file relies on these.
- **`src/Builders/Builders.Skins.cs`** — the heaviest partial: `ProcessUpgrades`, `BuildSkin`/`BuildSkinModifier`, `PopulateSkinPreviews`, the chance-gated-modifier filter shared with `SkinRenderer`.
- **`src/Models/*.cs`** — DTOs mirroring each game catalog (one per file, name == primary type). Game enums are flattened to strings.
- **`src/Models/DumpFile.cs`** — Top-level wrapper covering ~30 sections (`upgrades`, `gears`, `characters`, `resources`, `directives`, `missions`, `enemies`, …). See `SCHEMA_CHANGES.md` for full per-entry shapes.
- **`src/Serialization/NativeConverter.cs`** — Newtonsoft `JsonConverter` for Unity-native types (`HexMap`, `UpgradeProperty`, `DirectiveProperty`). Delegates to `JsonUtility.ToJson` and writes the result as raw JSON; read is not implemented (dump-only).

- **`src/Probe.cs`** — runtime introspection helper. Triggered by `$MYCOPUNK_DIR/probe.flag` (or `mise run probe`). Writes `$MYCOPUNK_DIR/probe/{types.md, catalogs.txt, subclasses.txt, unlock-directives.txt, samples/}` for exploration before adding new dumpers. Add new probe targets by editing `Probe.Run()` — the helpers (`DumpType<T>`, `DiscoverSubclasses<T>`, `SampleScriptableObjects<T>`) handle reflection and output formatting. **Workflow**: run `mise run probe`, read `subclasses.txt` for polymorphic types, design DTO, add builder, repeat.
- **`data.schema.json`** — JSON Schema (Draft 2020-12) describing the dump's shape. Validates against the current `data.json` via `ajv-cli`. Hand-written; keep in sync with the DTOs when adding new fields.

`NativeConverter` post-processes Unity's `JsonUtility.ToJson` output to quote bare `Infinity` / `-Infinity` / `NaN` tokens (Unity emits them unquoted; strict JSON parsers reject them). Combined with `FloatFormatHandling.String` on the outer Newtonsoft serializer, the entire dump is valid RFC 8259 JSON.

Serialization uses `ReferenceLoopHandling.Ignore` and a swallowing error handler — game objects have cycles and not-always-serializable fields, and the dump is best-effort.

## Game API quirks (publicized Assembly-CSharp.dll)

- `Upgrade.ID` returns `UpgradeID` — a struct with fields `ID : Int32` and `Mod : String` (empty for vanilla). `UpgradeID.ToString()` is the inherited `Object.ToString()` which returns the type name `"UpgradeID"`, so **never key dictionaries on `up.ID.ToString()`** — it collapses every entry to one. Use the `UpgradeKey()` helper in `src/Builders/Builders.Common.cs` (`"{Mod}:{ID}"`, or just `ID` when `Mod` is empty).
- `UpgradeProperty.GetStatData(Random, IUpgradable, UpgradeInstance)` — 3 args. Construct the third via `new UpgradeInstance(up, upgradable)`. **The enumerator can throw inside `MoveNext`** (e.g. game-internal NREs in `GetStackingModifier` for some properties when iterated against `Global.Instance` as the IUpgradable) — wrap each property's stat collection in a `try/catch` so one bad property doesn't abort the dump.
- The game defines its own `Upgrade` class — our DTO shadows it. When `up` is in scope, it's the game's type; our DTO is `MycopunkDumper.Upgrade` if disambiguation is needed. Use `global::Upgrade` to force the game's type (e.g. when reflecting `LevelUnlock_MultipleUpgrades.upgrades`).
- All four playable characters share the C# class name `Character` — **never key on `GetType().Name` for upgradables**, use `Info.APIName` (unique per gear/character: `wrangler`, `bruiser`, `scrapper`, `glider`, `smg`, etc.) with fallback to type name for `Global` and similar non-`GearInfo`-bearing upgradables.
- `PlayerResource.ID` (e.g. `"saxonite"`) is the canonical key; `PlayerResource.Name` is the display name (`"Saxonite"`). The `resources` map keys are IDs.
- `GearInfo.XPGainMultilier` — sic, missing the `p`. Use the game's spelling.
- `DirectiveData` ScriptableObjects are discoverable via `Resources.FindObjectsOfTypeAll<DirectiveData>()` (22 entries). Same pattern works for `PlayerResource` (26 entries) and probably any other game catalog SO.
- For unknown game signatures, prefer reading **decompiled source** (`./decompiled/`, see below) over compile-error iteration or runtime reflection. Falling back: trigger a compile error to get a method's full signature; or `Logger.LogInfo` a reflection dump of fields.

## Decompiled source (`./decompiled/`, gitignored)

The decompiled `Assembly-CSharp.dll` is the single biggest unblocker for this codebase — it surfaced `TextBlocks` (master localization), `ModifyProperties` (second tooltip source), per-property RNG (`GetRandomEffect`), hardcoded XP/achievement/status-effect constants, the `Player` base prefab, etc.

- One-time setup: `mise install dotnet@6 && DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 mise exec dotnet@6 -- dotnet tool install -g --version 8.2.0.7535 ilspycmd` (newer `ilspycmd` packages have a broken `DotnetToolSettings.xml` — pin `8.2.0.7535`). Then `mise run decompile` to (re)populate `./decompiled/` from the current `Assembly-CSharp.dll` — re-run after every game update or the source goes stale (you'll get e.g. `CS1061: 'X' does not contain a definition for 'Y'` when the game removes a property).
- Excluded from build via `<Compile Remove="decompiled/**/*.cs"/>` in csproj — required because some decompiled files have invalid syntax that would break compilation.
- **Use it before adding any new dumper.** The fastest path is: grep the decompiled source for the type/concept you want, read the relevant `.cs`, then design the DTO. Saves several probing iterations.
- `mise run extract-labels` runs static analysis over `decompiled/UpgradeProperty_*.cs` for localization keys hidden in `switch` expressions / conditional branches that runtime sampling can't trigger. Produces `property-labels.json`, auto-merged into the next `mise run dump` as `Properties[i].LocalizationKeys`.
- `Pigeon.Movement.Player` and other types live in subdirs (`./decompiled/Pigeon.Movement/Player.cs`). When the type's base class is from a separate Unity assembly (e.g. `NetworkBehaviour` from `Unity.Netcode.Runtime.dll`), add that DLL as a `<Reference>` in the csproj to access the type from the plugin.

## Runtime patterns for new dumpers

- **Catalog discovery**: `UnityEngine.Resources.FindObjectsOfTypeAll<T>()` for any `T : ScriptableObject`. Returns prefab assets at game start under BepInEx, including non-active ones. The pattern repeats for every catalog section in `Plugin.Start`.
- **Multi-seed sampling for RNG-rolled stats**: 23 `UpgradeProperty` subclasses call `GetRandomEffect()` / `rand.Next*()` inside `GetStatData`. Run with seeds 0..15 and union the resulting `value` strings — see the `RolledValuesByUpgradable` loop in `Plugin.ProcessUpgrades`. Captures categorical rolls (Element, Row/Column) and numeric distributions.
- **`TextBlocks.strings`** is the master `Dictionary<string, TextBlockGroup>` populated from CSV at startup. Every game-displayed string resolves through `TextBlocks.GetString(key)`. Exposed as `localization` in the dump. For any "where does this label come from?" question, grep decompiled source for `TextBlocks.GetString("` calls.
- **`@ref` instance-ID enrichment**: `NativeConverter` post-processes JsonUtility output and adds `"@ref": "<type>:<key>"` to every `{"instanceID": N}` whose `N` is in `NativeConverter.InstanceRefs`. To make a new catalog cross-referenceable, add a `foreach (var x in Resources.FindObjectsOfTypeAll<T>()) NativeConverter.InstanceRefs[x.GetInstanceID()] = "<type>:<key>";` block in `Plugin.Start` (before the dump assembly).
- **`ProcessUpgrades(Global.Instance)`** captures `Global.upgradesForAllGear` (the upgrades that apply to all gear / are part of the global skill tree). Don't re-dump them by other means.

## Schema validation

```bash
mise exec bun -- bun x ajv-cli@latest validate \
  --spec=draft2020 \
  -s data.schema.json \
  -d $MYCOPUNK_DIR/data.json
```

The `--spec=draft2020` flag is required (default is draft-07; the schema uses 2020-12 features). Validation failures print the JSON path + failed keyword — use that to find the missing schema entry.

## Autonomous CI

`.github/workflows/release.yml` runs `release-version` end-to-end on a 6-hour cron + `workflow_dispatch`. It uses a dedicated bot Steam account with **Steam Guard disabled** (so DepotDownloader logs in with just `-username` / `-password`, no 2FA scaffolding) to drive both the manifest check and the depot download, runs the game under Proton-GE, and pushes results to the sibling `mycopunk-data` repo via an SSH deploy key (`MYCOPUNK_DATA_DEPLOY_KEY`). Each run early-exits when Steam's current manifest gid matches the published `latest.steamManifestId`, so no-op runs are ~30 s. Full design + secrets list + one-time setup are in `docs/superpowers/specs/2026-05-06-autonomous-release-workflow-design.md`.

## Conventions

- `netstandard2.1`, `LangVersion=latest`, `AllowUnsafeBlocks=true`. C# 12 features (collection expressions, primary constructors) are in use.
- BepInEx plugin metadata comes from `MyPluginInfo` (auto-generated by `BepInEx.PluginInfoProps` from csproj `<Product>` / `<Version>` / `PackageId`). Don't hand-edit a `MyPluginInfo` file — change the csproj.
- The `[MycoMod(null, ModFlags.IsClientSide)]` attribute is game-specific (Mycopunk's mod loader) and sits alongside the standard `[BepInPlugin]`.
