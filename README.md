# SolutionReplicator

A Windows desktop (WPF / .NET 8) wizard that composes a **re-importable SAP solution package** from a **client-owned repository of placeholder content**. You use an exported **SAP Best Practices solution XML** as the structural template, pick the scope items / building blocks you want (standard **or** custom `Z` content), point at a folder of your own `.TXT` files that carry **placeholder tokens** (e.g. `&PLN`), choose a **target system** from a reusable value table, and generate a package whose placeholders have been substituted with that system's real values — ready to import into SAP Solution Builder.

> The SAP solution XML is used only as a **template/envelope** that makes the output re-importable. The content is **yours**: a repository of tokenized files plus a central **nomenclature table** (a CSV) that maps each placeholder variable to a real value per target system. Importing the generated package into SAP and activating it are done **manually** by you; the tool's job ends at producing a valid package.

---

## Table of contents

1. [What it does](#what-it-does)
2. [Concepts & terminology](#concepts--terminology)
3. [Requirements](#requirements)
4. [Build](#build)
5. [Run](#run)
6. [Step-by-step usage](#step-by-step-usage)
7. [The placeholder nomenclature table (CSV)](#the-placeholder-nomenclature-table-csv)
8. [Output package](#output-package)
9. [Architecture & design](#architecture--design)
10. [How the "consumed files" are computed](#how-the-consumed-files-are-computed)
11. [SAP solution-XML structure notes](#sap-solution-xml-structure-notes)
12. [Reuse map](#reuse-map)
13. [Verification harness](#verification-harness)
14. [Known limitations & caveats](#known-limitations--caveats)
15. [Reverting the library conversion](#reverting-the-library-conversion)
16. [Project layout](#project-layout)

---

## What it does

The repository content is the master; SAP's export is the template. The wizard walks **five** steps:

| # | Step | What happens |
|---|------|--------------|
| 1 | **Load** | Pick the exported solution XML (the template). It's parsed (streaming, memory-safe) and the real scope-item → building-block map is built. |
| 2 | **Select** | Tick the scope items / building blocks to deploy. Works with custom `Z`/customer ids. |
| 3 | **Repository & catalog** | Point at your **repository folder** (tokenized `.TXT` files) and your **nomenclature CSV**. The repo is scanned and every placeholder variable (`&…`) is **auto-discovered**. |
| 4 | **Target system & values** | Choose a **target system** (a `sysid` in the CSV), or **+ Add system…** to create one. Fill in / edit the real value for each variable, then **💾 Save catalog** (writes the CSV). |
| 5 | **Compose** | Resolve which files the selected scope consumes, match them to the repo by exact name, substitute the chosen profile's values into the placeholders, and write the re-importable solution XML plus the populated `.TXT` files. |

After Compose, you import the generated solution into **SAP Solution Builder** and activate it yourself.

### Why placeholders instead of hardcoded values
A client stands up many systems (e.g. HU-PROD, CZ-PROD, DE-TEST). Writing the repository content **once** with tokens like `&PLN` for "plant", and maintaining a single table that says `&PLN → HU01` for one system and `→ CZ01` for another, means a new deployment is just picking a column — not re-typing every value. Adding a 21st system adds a column; it never disturbs the existing ones.

### Why "only consumed" matters
A full solution references ~14,000 installation-data files. A scope-item selection only *consumes* a subset, and your repository only holds the files you've tokenized. Compose pulls in **only the files that are both consumed by the selection and present in your repo**, and reports anything consumed but missing.

---

## Concepts & terminology

- **Solution XML** — the SAP Best Practices export used as the template; root element `<Solution>`, flat uppercase tags, generic `<item>` row wrappers, UTF‑8.
- **Scope Item (SI)** — a business scenario, keyed by `PRJID`. Each SI references a set of building blocks.
- **Building Block (BB)** — a reusable configuration unit, keyed by `BBID` (e.g. `1IM (XX)`). `(XX)` = country-independent.
- **Repository** — a plain folder of the client's own tokenized `.TXT` variant files.
- **Placeholder token** — a whole data cell of the form `&NAME` (e.g. `&PLN`), sitting under a specific column/field (e.g. `I_WERKS`). It stands in for a real value until compose time.
- **Nomenclature table (CSV)** — the central `variable;field;value;sysid` CSV: each row binds one variable (scoped to a field) to a real value for one target system. One CSV can serve many repositories.
- **Target system (`sysid`)** — a named set of values in the CSV (e.g. `HU-PROD`).
- **Installation-data / variant files** — tab-delimited **UTF‑16LE + BOM** `.TXT` files. Row 0 is the header `[VARIANT]\t[DESCRIPTION]\tI_FIELD1…`; real parameter columns start at index 2.
- **Consumed set** — the files a given BB selection pulls in (see [below](#how-the-consumed-files-are-computed)).

---

## Requirements

- Windows 10/11 (64-bit).
- **.NET 8 SDK** (build) / .NET 8 Desktop Runtime (run).
- Enough RAM for the solution DOM during Compose (~1 GB resident for a ~34 MB XML; the app targets x64 with server GC).
- The three projects must sit in their expected relative locations (see [Project layout](#project-layout)); the `.sln` references them by relative path.

---

## Build

```bash
cd C:/SAPDevelop/SolutionReplicator
dotnet build SolutionReplicator.sln
```

This builds the shell plus the two referenced libraries. A clean build reports `0 Warning(s), 0 Error(s)`.

> If the app is running, it locks `bin/Debug/.../SolutionReplicator.exe` — the build then fails only on the final exe copy (MSB3026/3027), not on compilation. Close the app and rebuild, or compile without the copy via `dotnet build SolutionReplicator/SolutionReplicator.csproj -t:Compile`.

## Run

```bash
dotnet run --project SolutionReplicator/SolutionReplicator.csproj
```

or launch `SolutionReplicator/bin/Debug/net8.0-windows/SolutionReplicator.exe`.

---

## Step-by-step usage

1. **Load** — *Browse…* to the exported solution XML (the template). Click **Load**; the header shows the solution id, source country, and SI/BB counts. **Next**.
2. **Select** — tick scope items (ticking an SI ticks its BBs) and/or individual BBs. Use **Search**, **Select All**, **Deselect All**. **Next** is enabled once at least one item is selected.
3. **Repository & catalog** — *Browse…* the **Repository** to your folder of tokenized `.TXT` files, and *Browse…* the **Catalog** to a `.csv` file (it need not exist yet — it's created on save). The panel shows how many TXT files and distinct variables were found, and whether the CSV was loaded (row/system counts) or is new. **Next** is enabled once the folder has at least one TXT.
4. **Target system & values** — pick a **Target system** from the dropdown or **+ Add system…** to create one (enter its `sysid`). In the grid, edit each variable's **Value for this system**. Click **💾 Save catalog** to write the CSV. **Next**.
5. **Compose** — Optionally tick **Rename solution to:** and enter a new solution id (pre-filled from the template). *Browse…* an **Output** folder, review the summary, click **⚙ Compose**. The log reports consumed-file count, how many matched in the repo (and how many were unmatched/skipped), the output XML path and data folder, and how many files were written with substitutions. Then import the package into SAP Solution Builder and activate manually.

Last-used paths (template, repository, catalog, output, profile) are remembered between runs and pre-filled.

---

## The placeholder nomenclature table (CSV)

A semicolon-delimited CSV you point at (it can live anywhere central and serve multiple
repositories). Layout — one row per **(variable, field, value, target system)**:

```
variable;field;value;sysid
&PLN;I_WERKS;HU01;HU-PROD
&PLN;I_WERKS;CZ01;CZ-PROD
&PLN;I_WERKS;DE01;DE-TEST
```

- **`variable`** — the placeholder exactly as it appears in the TXT cell (e.g. `&PLN`).
- **`field`** — the column/header the variable lives under (e.g. `I_WERKS`). Keeps a variable
  unambiguous if it is ever used under two different columns.
- **`value`** — the real value for that system (e.g. `HU01`).
- **`sysid`** — the target system this value belongs to (e.g. `HU-PROD`).

A header row is written on save and tolerated (skipped) on load. Each variable appears once per
system; at compose time you pick a single **sysid** and only that system's rows are applied.

- Adding a target system = adding rows with a new `sysid`. Purely additive — existing systems are untouched.
- A variable with **no value** (empty) for the chosen system is left unmapped: its cell keeps the
  placeholder (a visible "not mapped" signal), rather than being blanked silently.
- Variables are **auto-discovered** from the repository on step 3; the Target-system step seeds an
  empty row per variable for the chosen system, so you only ever type the values.

A ready-to-use sample lives at `C:\_testing\sample-nomenclature.csv`.

---

## Output package

Written into the export folder:

```
<name>.xml                      # the template solution XML, filtered to the selection
<name>_installation_data/       # the matched repo .TXT files, placeholders substituted
    *.TXT
```

- `<name>` is the template's solution id, or a custom name if you ticked **Rename solution to:** in step 5.
- TXT files are written **UTF‑16LE + BOM**, tab-delimited, CRLF; non-data lines are preserved verbatim; only the targeted columns are substituted (column-scoped, exact value match). `&PLN` under `I_WERKS` becomes e.g. `HU01`; a literal value under another column is left untouched.
- The solution XML is written UTF‑8.
- Files are written under the **template's consumed filename** so the XML's references resolve.

---

## Architecture & design

### Three projects, one shell

```
SolutionReplicator.sln
├── SolutionReplicator/               (new)  WinExe, net8.0-windows, x64 — the wizard shell
├── ../SolutionXMLParser/SolutionBuilder            → class library (was WinExe)
└── ../Self_Localize_varfiles/SapVariantReplacer    → class library (was WinExe)
```

The two existing tools were converted to **class libraries** (`OutputType=Library`, their `App.xaml`/`MainWindow` excluded from the build but kept on disk) so the shell reuses their proven logic without duplicating it. CommunityToolkit.Mvvm is unified on **8.4.0** across all three. See [Reverting](#reverting-the-library-conversion).

### Wizard = ViewModel-driven single window

- `WizardShellWindow` — header (breadcrumb/step), a `ContentControl` bound to the current step, and a Back / Next / Cancel footer.
- `WizardHostViewModel` — owns the ordered `Steps`, `CurrentStep`, and navigation. Advancing runs the current step's `OnLeaveAsync()` (may veto), then the next step's `OnEnterAsync()`. **Next** is gated by each step's `CanAdvance`.
- `WizardStepViewModel` (abstract) — `Title`, `CanAdvance`, `OnEnterAsync`, `OnLeaveAsync`, and a reference to the shared `WizardContext`.
- `WizardContext` — the single mutable state object passed to every step (template path, loaded `SolutionData`, selection sets, consumption result, repo folder + parsed repo files, discovered tokens, CSV path + loaded `PlaceholderTable`, chosen system, the compose change-set, output folder).
- **View resolution** is by implicit `DataTemplate` in `App.xaml` (step VM type → `UserControl`) — no page navigation, no code-behind wiring.

### Compose pipeline

`ComposeViewModel` runs:
1. `ConsumptionResolver.Resolve(xml, selectedBbIds, selectedScopeItemIds)` → the consumed file set.
2. `RepoFileMatcher.Match(consumedFiles, repoFolder)` → repo files matched by exact name (+ unmatched report).
3. `SolutionProcessorService.PreparePackageAsync(... renameSolution: user-chosen, same country ...)` → the filtered, re-importable XML (solution id renamed if the checkbox was ticked).
4. `ReplacementEngine.Apply(matchedFiles, changeSet, outputDataFolder, copyUnmodified:true)` → the populated TXTs.

The change-set is built by `CatalogComposer.BuildChangeSet(table, sysid)`: one `(field, variable) → value` entry per CSV row for the chosen system that has a non-empty value. Because `ReplacementEngine` matches on `(header, cell value)`, a placeholder is just a cell value — **the replacement engine needs no change**.

### Persistence (new in this tool)

- **Nomenclature CSV** — `PlaceholderTableStore` loads/saves the `variable;field;value;sysid` CSV (atomic temp-then-replace; saved only on the explicit **Save catalog** button).
- **Last-used paths** — `AppSettingsStore` keeps `%LOCALAPPDATA%\SolutionReplicator\settings.json` and pre-fills the browse fields.

---

## How the "consumed files" are computed

`ConsumptionResolver` streams the XML once and, for each selected `BBID`, follows these join paths:

```
selected BBID
 ├─ BUILDING_BLOCKS/item[BBID]/TASKS/item/FILENAME                         → consumed file
 ├─ BUILDING_BLOCKS/item[BBID]/TASKS/item[OBJTY=BCS]/OBJID                 → BCSET_DEFINITION/item[MDF_BCSET]
 ├─ BUILDING_BLOCKS/item[BBID]/IMG_SUBOBJECTS/item/FILENAME                → consumed file
 │     + key (IMG_ACTIVITY, CUST_OBJTYPE, CUST_OBJNAME, SUBOBJNAME)        → IMG_DEFINITION/item
 └─ ASSOCIATION/item[BBID_PRI]/FILENAME_PRI, FILENAME_SEC                  → consumed file

IMG_DEFINITION/item  → FIELDS/item(PARAM, FIELDNAME, CENTRALDATA)  + TDC/item/VAR_EXT_FILE
BCSET_DEFINITION/item → MDT_VARIANTS/item(PARAM, …) + MDT_TDC_ALIAS/VAR_EXT_FILE + MDF_EXT_FILE
ADDITION/item[PRIM_FILE ∈ consumed] → SEC_FILE        (companion files, resolved to a fix-point)
```

- Selection is pure **string set-membership**, so custom `Z`/customer ids flow through unchanged.
- The consumed set drives the compose step's file matching against the repository.

`ScopeItemMapper` provides the SI → BB map for the tree (see note below).

---

## SAP solution-XML structure notes

Things that are easy to get wrong (all verified against real exports):

- **The SI → BB mapping is NOT in `SCENARIOS_SEQUENCE`.** That section is ordering-only. The real scope-item definitions live in a **top-level `<SCENARIOS>`** section: each `item` has `PRJID`, `SIR_SI_ID`, `SIR_SI_COUNTRY`, `DESCRIPTION`, and **`BB_SEQUENCE/item/BBID`**. Beware `APPLICATION_AREAS/item/SCENARIOS/item` — a *different* list.
- `IMG_DEFINITION/item/BBID` is always empty — join to a BB only via the 4‑field `(IMG_ACTIVITY, CUST_OBJTYPE, CUST_OBJNAME, SUBOBJNAME)` key carried on `IMG_SUBOBJECTS`.
- The 8 filename-bearing elements: `FILENAME, FILENAME_PRI, FILENAME_SEC, PRIM_FILE, SEC_FILE, MDF_EXT_FILE, VAR_EXT_FILE, EXT_FILENAME`.
- **XmlReader pitfall:** `ReadElementContentAsString()` advances *past* the following sibling, silently skipping it. The resolver/mapper parse each `<item>` as an `XElement` via `XElement.Load(reader.ReadSubtree())`, which leaves the reader on `</item>` so the standard `while(reader.Read())` loop stays correct.
- Installation-data TXTs are **UTF‑16LE + BOM**, tab-delimited; columns 0/1 are `[VARIANT]`/`[DESCRIPTION]`, parameter columns start at index 2.

---

## Reuse map

**Reused as-is**
- `XmlParserService`, `CountryCodeService`, `TreeNodeViewModel` *(SolutionBuilder)*
- `VariantFileParser`, `FieldExtractor`, `ReplacementEngine`, `InputDialog`, `FieldValueEntry`, `InvertBoolConverter` *(SapVariantReplacer)*
- `SolutionProcessorService.PreparePackageAsync` *(SolutionBuilder)*

**Net-new (in the shell)**
- `ConsumptionResolver`, `ScopeItemMapper`, `ConsumptionResult`/`ConsumedParameter`, `ScopeItemInfo`
- `PlaceholderTable`, `PlaceholderTableStore`, `AppSettingsStore`, `TokenScanner`, `RepoFileMatcher`, `CatalogComposer`
- `WizardHostViewModel`, `WizardStepViewModel`, `WizardContext`
- Step VMs/Views: `LoadSolution`, `SelectScope`, `RepoCatalog`, `ProfileValues`, `Compose`

---

## Verification harness

Two throwaway console harnesses (not part of the `.sln`) validate the logic headlessly. Each links the shell `.cs` files directly and references both libraries; run with `dotnet run` in its folder.

- **`tools/ComposeProbe`** — the end-to-end proof of the placeholder slice on the real sample files (`C:\_testing\_CUST1` + `C:\_testing\ZVMA_DEMO_TEMPLATE.xml`): variable discovery, CSV seed/round-trip/multi-system, placeholder substitution (`&PLN → HU01` under `I_WERKS` only, `HUS1` untouched, UTF‑16LE BOM + CRLF preserved), and exact-name matching against the template's consumed set. Prints `ALL CHECKS PASSED`.

  ```bash
  cd C:/SAPDevelop/SolutionReplicator/tools/ComposeProbe
  dotnet run                          # defaults to the sample repo + template
  dotnet run -- <repoFolder> <xml>    # override
  ```

- **`tools/ConsumptionProbe`** — the regression guardrail for the consumption-join logic (`ConsumptionResolver` + `ScopeItemMapper`), the riskiest still-live code. Run it after any change to parsing/consumption and confirm counts don't regress.

---

## Known limitations & caveats

- **Output ships only the matched files**, but the solution XML still references the full `IMG_DEFINITION`/`BCSET_DEFINITION` catalogs (those sections aren't DOM-scoped). So the XML can reference files not shipped in `_installation_data`. **Validate on your first real Solution Builder re-import.**
- **Exact-name matching** assumes the repository files are named to match the template's consumed files one-to-one. Any consumed file without a repo match is reported and skipped (never fabricated) — the compose log makes the matched/unmatched counts visible.
- **Import to SAP + activation are manual** (out of scope by design).
- **Unmapped tokens** (no value for the chosen profile) keep their placeholder in the output — intentional, so a half-filled profile is obvious rather than silently blanked.
- **GUI** is verified for startup/rendering and the compose logic is proven headlessly; interactive click-through of all five steps against real data is a manual check.

---

## Reverting the library conversion

The two source tools were turned into libraries **reversibly**. To restore either as a standalone app:

1. In its `.csproj`, change `<OutputType>Library</OutputType>` back to `WinExe`.
2. Remove the `<ItemGroup>` that excludes `App.xaml` / `MainWindow` (and, for SapVariantReplacer, note `InvertBoolConverter` now lives in `Converters/InvertBoolConverter.cs`).

`SapVariantReplacer` is a git repo; these changes are currently **uncommitted**. `SolutionXMLParser` is not a git repo.

---

## Project layout

```
C:\SAPDevelop\
├── SolutionReplicator\                      # this app (new)
│   ├── SolutionReplicator.sln
│   ├── SolutionReplicator\
│   │   ├── App.xaml(.cs)
│   │   ├── Models\        (WizardContext, ConsumptionResult, PlaceholderTable)
│   │   ├── Services\      (ConsumptionResolver, ScopeItemMapper, InlineXmlValueExtractor,
│   │   │                   PlaceholderTableStore, AppSettingsStore, TokenScanner,
│   │   │                   RepoFileMatcher, CatalogComposer)
│   │   ├── ViewModels\    (WizardHost, WizardStep, Load, SelectScope, RepoCatalog,
│   │   │                   ProfileValues, Compose)
│   │   └── Views\         (WizardShellWindow + one UserControl per step)
│   └── tools\
│       ├── ComposeProbe\                     # end-to-end placeholder-slice harness
│       └── ConsumptionProbe\                 # consumption-join regression harness
├── SolutionXMLParser\SolutionBuilder\       # library: XML parse/filter/transform, country rules
└── Self_Localize_varfiles\SapVariantReplacer\  # library: variant parse/extract/replace
```
