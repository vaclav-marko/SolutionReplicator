# AGENTS.md — guidance for future agents/LLMs working on SolutionReplicator

This file is for an AI agent (or automation) asked to extend or fix this project. Read it **before** editing. Human-facing docs are in [README.md](README.md). Pair this with the memory notes under `~/.claude/projects/C--SAPDevelop-SolutionReplicator-SolutionReplicator/memory/` if present.

---

## 0. One-paragraph orientation

`SolutionReplicator` is a WPF/.NET 8 wizard that **composes a re-importable SAP package from a client-owned repository of placeholder content**. Flow: load a SAP solution XML as the **template** → pick scope items/building blocks → point at the client **repository folder** (tokenized TXT files) + a central **nomenclature CSV** (`variable;field;value;sysid`) → pick a **target system/profile** and fill its values → **compose** (optionally rename the solution id, substitute `&TOKEN`s with the profile's values and write the package). It is a **shell exe** that references two **existing tools converted to class libraries**. Importing to SAP + activation are manual and out of scope. The SAP XML is a template/envelope, not the master — the client's repo + CSV are. Don't re-architect; extend within the existing wizard-step pattern.

> History: this tool was pivoted (2026-10-21) away from an older SAP-subsetting flow (Edit/Sideload/Output/Process steps). Those steps were **deleted**; do not resurrect them. See the design/log at `~/.claude/plans/i-basically-shared-this-snuggly-meadow.md`.

## 1. Golden rules (do not violate without explicit user sign-off)

1. **Verify every change by building the whole solution**: `dotnet build C:/SAPDevelop/SolutionReplicator/SolutionReplicator.sln`. It must stay `0 Warning(s), 0 Error(s)`. (If the app is running it locks the exe → the build fails only on the final exe copy, MSB3026/3027, not compilation. Close it, or compile with `dotnet build SolutionReplicator/SolutionReplicator.csproj -t:Compile`.)
2. **Never silently change the two referenced libraries' public behavior.** `SolutionBuilder` and `SapVariantReplacer` are shared. Changes there must be **additive and back-compat**. `SapVariantReplacer` is a **git repo** — do not commit without the user asking.
3. **Preserve installation-data file fidelity**: TXT output is **UTF‑16LE + BOM**, tab-delimited, CRLF, non-data lines verbatim, replacements **column-scoped** (match on `(header, exact cell value)`). This all lives in `ReplacementEngine` (reused) — don't reimplement it. A placeholder `&PLN` is just an `OriginalValue`; the engine needs no placeholder-specific code.
4. **Keep the solution XML schema intact** on output so it stays re-importable. When in doubt, ship *more* (a superset of files) rather than prune the DOM.
5. **Selection must stay namespace-agnostic** (plain string set-membership) so custom `Z`/customer ids keep working. Don't add pattern assumptions to selection/consumption logic.
6. **Don't claim something works without evidence.** Use a probe (section 6) or a real build/run, and say what you actually observed.

## 2. Architecture you must respect

- Wizard = `WizardHostViewModel` + `WizardStepViewModel` (abstract) + `WizardContext` (shared mutable state). Steps render via **implicit `DataTemplate`s in `App.xaml`** (step VM type → `UserControl`).
- The flow is **five steps**, hard-coded in `Views/WizardShellWindow.xaml.cs` `OnLoaded`: `Load → Select → RepoCatalog → ProfileValues → Compose`. Load + Select are reused from before the pivot; the last three are the placeholder flow.
- To **add a step**: create `XxxViewModel : WizardStepViewModel` (+ `XxxView` UserControl), register a `DataTemplate` in `App.xaml`, and insert it into the `SetStepsAsync([...])` list. Gate navigation with `CanAdvance`; do heavy work in `OnEnterAsync` (on `Task.Run`); validate/commit in `OnLeaveAsync` (return `false` to block).
- Cross-step data flows **only** through `WizardContext`. Don't reach across step VMs.
- Long work runs on `Task.Run`; keep the UI thread free. Don't block in constructors.

## 3. The parts most likely to need changes — and their traps

### Parsing the solution XML (`Services/ConsumptionResolver.cs`, `Services/ScopeItemMapper.cs`) — UNCHANGED by the pivot, still the riskiest code
- **Trap:** `XmlReader.ReadElementContentAsString()` advances *past* the next sibling, skipping it. **Always** parse an `<item>` as `XElement.Load(reader.ReadSubtree())` (leaves the reader on `</item>`, so the standard `while(reader.Read())` loop is correct). Follow the existing `ForEachItem` helper.
- **Trap:** SI→BB mapping is in the **top-level `<SCENARIOS>`** section (items with `PRJID` + `BB_SEQUENCE`), *not* `SCENARIOS_SEQUENCE` (ordering only) and *not* `APPLICATION_AREAS/.../SCENARIOS` (singular `SCENARIO` refs). `ScopeItemMapper` already handles this; mirror it.
- **Trap:** `IMG_DEFINITION/item/BBID` is empty. Join IMG defs to a BB via the 4-field key `(IMG_ACTIVITY, CUST_OBJTYPE, CUST_OBJNAME, SUBOBJNAME)` from `IMG_SUBOBJECTS`.
- If you change join logic, re-run `tools/ConsumptionProbe` and confirm counts don't regress (section 6).

### The nomenclature table + token flow (`Models/PlaceholderTable.cs`, `Services/{PlaceholderTableStore,TokenScanner,CatalogComposer,RepoFileMatcher}.cs`)
- The table is a semicolon CSV `variable;field;value;sysid`, one row per (variable, field, value, system). `PlaceholderTableStore` writes a header line and tolerates/skips it on load; rows with <4 columns are ignored.
- A row's identity for seeding is **`(Variable, Field, SysId)`** — the variable is column-scoped to a field, and the same variable exists once per system. Never collapse away `Field` or `SysId`.
- `TokenScanner.Scan` = `VariantFileParser.ParseFolder` + `FieldExtractor.Extract`, then filter `OriginalValue` on `^&[A-Z0-9_]+$`. Don't write a new parser.
- `CatalogComposer.SeedMissingTokens(table, discovered, sysId)` adds an **empty-value** row for any discovered `(variable, field)` missing for that `sysId`; it **never clobbers** existing values. Called when a system is selected in `ProfileValuesViewModel`.
- `CatalogComposer.BuildChangeSet(table, sysId)` emits one `FieldValueEntry` per row with matching `SysId` and a **non-empty** value. A variable with no value is omitted → its cell keeps the placeholder (visible "unmapped" signal). `FieldValueEntry.OccurrenceCount` is `required` — set it (0 for compose).
- `PlaceholderTableStore.Save` is temp-then-atomic-replace and is called **only** on the explicit "Save catalog" button — never auto-save on navigation.

### The Compose pipeline (`ViewModels/ComposeViewModel.cs` + `SolutionProcessorService.PreparePackageAsync`)
- Order: `ConsumptionResolver.Resolve` → `RepoFileMatcher.Match` (exact name) → `PreparePackageAsync(renameSolution: user-chosen via checkbox in step 5, source==target country)` → `ReplacementEngine.Apply(matchedFiles, changeSet, outputDataFolder, copyUnmodified:true)`.
- **Exact-name matching** is deliberate: the repo files are exported for the same template and match one-to-one. Any consumed file with no repo match is **reported** (matched/unmatched counts) and skipped — never fabricated. Don't add fuzzy/stem matching without the user asking.
- The **open limitation** (see README): output ships only matched files but the XML references full `IMG_DEFINITION`/`BCSET_DEFINITION` catalogs. If asked to make the package strictly self-consistent, either (a) copy the full `PreparedPackage.FileMapping` set, or (b) DOM-scope those catalogs to the consumed set. Option (b) is riskier — validate against a real SAP re-import.

## 4. Common requested changes — where to start

| Request | Start here |
|---|---|
| Different token syntax (not `&NAME`) | `TokenScanner.TokenPattern` (one compiled regex). Keep whole-cell matching so `ReplacementEngine`'s exact-value match still applies. |
| Change the CSV columns / delimiter | `Models/PlaceholderTable.cs` + `Services/PlaceholderTableStore.cs` (load/save). Keep round-trip (save → load) stable — `ComposeProbe`'s CSV checks guard this. |
| Make output strictly self-consistent | See Compose limitation above. |
| Remember more paths between runs | `Services/AppSettingsStore.cs` (`%LOCALAPPDATA%\SolutionReplicator\settings.json`); add a field + load/save it. |
| New parameter metadata / import-structures report | Extend `ConsumptionResolver` output or add a read-only streaming service mirroring `ScopeItemMapper`; present read-only. |

## 5. Things that will bite you

- The app **locks its own exe** while running → a rebuild fails on the exe copy only. Close the app (or `-t:Compile`). This is not a code error.
- **`FieldValueEntry.OccurrenceCount` is `required`** — forgetting it is a compile error.
- WPF binding/`DataTemplate` errors **don't fail the build** — they fail at runtime. After XAML changes, **launch the app** (even a timed 12s run) and check it renders.
- CommunityToolkit.Mvvm must stay **8.4.0** in all three csproj (no central `Directory.Packages.props`).
- The two probe harnesses (`tools/ComposeProbe`, `tools/ConsumptionProbe`) are **not in the `.sln`**; they link shell `.cs` files directly + reference both libs. If you rename/move a shell file a probe links, update the probe's `<Compile Include>` list or it won't build.
- `Services/InlineXmlValueExtractor.cs` survives only because `ConsumptionProbe` links it; it is **not** used by the shell flow. Don't wire it into the compose path without a reason.

## 6. How to verify without a human clicking

Headless logic/data checks — fast and reliable:

```bash
# End-to-end placeholder slice on the real sample files (authoritative):
cd C:/SAPDevelop/SolutionReplicator/tools/ComposeProbe
dotnet run                               # defaults: C:\_testing\_CUST1 + C:\_testing\ZVMA_DEMO_TEMPLATE.xml
dotnet run -- <repoFolder> <templateXml> # override
# Expect: ALL CHECKS PASSED — &PLN→HU01 under I_WERKS only, HUS1 untouched,
#         UTF-16LE BOM + CRLF preserved, catalog round-trips, both repo files matched.

# Consumption-join regression guardrail (the risky still-live parsing):
cd C:/SAPDevelop/SolutionReplicator/tools/ConsumptionProbe
dotnet run
```

If you change parsing/consumption, re-run `ConsumptionProbe`. If you change tokens/catalog/matching/compose, re-run `ComposeProbe`.

GUI smoke test (startup only):
```bash
timeout 12 "SolutionReplicator/bin/Debug/net8.0-windows/SolutionReplicator.exe"
# exit 124 = still running = startup OK; a fast non-zero exit + stack = XAML/binding error
```

## 7. Scope discipline

- Import-to-SAP and activation are **intentionally out of scope**. Don't add RFC/BAPI pushes unless the user explicitly asks and grants the connection — and never route a blocked action through another session.
- Outward/irreversible actions (git commits, deleting data, overwriting the user's repo/catalog/solutions) require explicit user confirmation.
- Prefer extending the reused libraries additively over forking their logic into the shell.
