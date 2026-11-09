# Solution Replicator

A Windows desktop (WPF / .NET 8) wizard that **composes a re-importable SAP solution package from a repository of placeholder-driven content**. You use an exported **SAP Best Practices solution XML** as the structural template, pick the scope items / building blocks you want (standard **or** custom `Z` content), point at a folder of your own tokenized `.TXT` files (carrying placeholders like `&PLN`), choose a **target system** from a reusable value table, and generate a package whose placeholders have been substituted with that system's real values — ready to import into SAP Solution Builder.

> The SAP solution XML is used only as a **template/envelope** that keeps the output re-importable. The content is **yours**: a repository of tokenized files plus a central **nomenclature table** (a CSV) mapping each placeholder to a real value per target system. Importing the generated package into SAP and activating it are done **manually**; the tool's job ends at producing a valid package.

---

## ⚠️ Disclaimer

**This tool is provided "as is", without any warranty or support from SAP.** It is an illustrative prototype — rapidly built, not production-hardened or quality-assured — intended **solely for educational and demonstration purposes**: to show how the end-to-end solution-replication ecosystem can work. It is **not** an official SAP product or offering, carries no support or liability, and should **not** be used in a productive environment. Use it at your own risk. See [LICENSE](LICENSE).

---

## Table of contents

1. [What it does](#what-it-does)
2. [Concepts & terminology](#concepts--terminology)
3. [The placeholder nomenclature table (CSV)](#the-placeholder-nomenclature-table-csv)
4. [Requirements](#requirements)
5. [Build](#build)
6. [Run](#run)
7. [Step-by-step usage](#step-by-step-usage)
8. [Output package](#output-package)
9. [Project layout](#project-layout)
10. [Architecture notes](#architecture-notes)
11. [Verification harnesses](#verification-harnesses)
12. [Known limitations](#known-limitations)

---

## What it does

The repository content is the master; SAP's export is the template. The wizard walks **five** steps:

| # | Step | What happens |
|---|------|--------------|
| 1 | **Load** | Pick the exported solution XML (the template). It's parsed (streaming, memory-safe) and the real scope-item → building-block map is built. |
| 2 | **Select** | Tick the scope items / building blocks to deploy. Works with custom `Z` / customer ids. |
| 3 | **Repository & catalog** | Point at your **repository folder** (tokenized `.TXT` files) and your **nomenclature CSV**. The repo is scanned and every placeholder variable (`&…`) is **auto-discovered**. |
| 4 | **Target system & values** | Choose a **target system** (a `sysid` in the CSV), or add a new one. Fill in / edit the real value for each variable, then **Save catalog** (writes the CSV). |
| 5 | **Compose** | Resolve which files the selected scope consumes, match them to the repo by exact name, substitute the chosen system's values into the placeholders, and write the re-importable solution XML plus the populated `.TXT` files. |

After Compose, you import the generated solution into **SAP Solution Builder** and activate it yourself.

### Why placeholders instead of hardcoded values

A client stands up many systems (e.g. HU-PROD, CZ-PROD, DE-TEST). Writing the repository content **once** with tokens like `&PLN` for "plant", and maintaining a single table that says `&PLN → HU01` for one system and `→ CZ01` for another, means a new deployment is just picking a column — not re-typing every value. Adding a 21st system adds rows; it never disturbs the existing ones.

### Why "only consumed" matters

A full solution references thousands of installation-data files. A scope-item selection only *consumes* a subset, and your repository only holds the files you've tokenized. Compose pulls in **only the files that are both consumed by the selection and present in your repo**, and reports anything consumed but missing.

---

## Concepts & terminology

- **Solution XML** — the SAP Best Practices export used as the template; root element `<Solution>`, flat uppercase tags, generic `<item>` row wrappers, UTF-8.
- **Scope Item (SI)** — a business scenario, keyed by `PRJID`. Each SI references a set of building blocks.
- **Building Block (BB)** — a reusable configuration unit, keyed by `BBID` (e.g. `1IM (XX)`). `(XX)` = country-independent.
- **Repository** — a plain folder of your own tokenized `.TXT` variant files.
- **Placeholder token** — a whole data cell of the form `&NAME` (e.g. `&PLN`), sitting under a specific column/field (e.g. `I_WERKS`). It stands in for a real value until compose time.
- **Nomenclature table (CSV)** — the central `variable;field;value;sysid` CSV: each row binds one variable (scoped to a field) to a real value for one target system. One CSV can serve many repositories.
- **Target system (`sysid`)** — a named set of values in the CSV (e.g. `HU-PROD`).
- **Installation-data / variant files** — tab-delimited **UTF-16LE + BOM** `.TXT` files. Row 0 is the header `[VARIANT]\t[DESCRIPTION]\tI_FIELD1…`; real parameter columns start at index 2.

---

## The placeholder nomenclature table (CSV)

A single semicolon-delimited flat file that **decouples content from configuration**. Instead of hardcoding system-specific values (plant codes, currencies, company codes) into every installation file, the content uses neutral placeholder tokens like `&PLN`. One central table says what each token becomes **per target system** — so standing up a new system is just picking a column, never re-typing values.

Layout — one row per **(variable, field, value, target system)**:

```
variable;field;value;sysid
&PLN;I_WERKS;HU01;HU-PROD
&PLN;I_WERKS;CZ01;CZ-PROD
&PLN;I_WERKS;DE01;DE-TEST
&CURRENC;I_CURRENCY;HUF;HU-PROD
&CURRENC;I_CURRENCY;CZK;CZ-PROD
```

- **`variable`** — the placeholder exactly as it appears in the TXT cell (e.g. `&PLN`).
- **`field`** — the column/header the variable lives under (e.g. `I_WERKS`). Keeps a variable unambiguous if it is ever used under two different columns.
- **`value`** — the real value for that system (e.g. `HU01`).
- **`sysid`** — the target system this value belongs to (e.g. `HU-PROD`).

A header row is written on save and tolerated (skipped) on load. Each variable appears once per system; at compose time you pick a single **sysid** and only that system's rows are applied.

- Adding a target system = adding rows with a new `sysid`. Purely additive — existing systems are untouched.
- A variable with **no value** (empty) for the chosen system is left unmapped: its cell keeps the placeholder (a visible "not mapped" signal), rather than being blanked silently.
- Variables are **auto-discovered** from the repository on step 3; the target-system step seeds an empty row per variable for the chosen system, so you only ever type the values.

A ready-to-use example is included at [`samples/sample-nomenclature.csv`](samples/sample-nomenclature.csv). It defines three target systems (`HU-PROD`, `CZ-PROD`, `DE-TEST`) across a few variables, and deliberately leaves one value empty for `DE-TEST` so you can see how an unmapped token keeps its placeholder in the output. Point step 3's **Catalog** browse field at a copy of it, or use it as the shape for your own.

---

## Requirements

- Windows 10/11 (64-bit).
- **.NET 8 SDK** (to build) / .NET 8 Desktop Runtime (to run). Download: <https://dotnet.microsoft.com/download/dotnet/8.0>
- Enough RAM for the solution DOM during Compose (roughly ~1 GB resident for a large ~34 MB XML; the app targets x64 with server GC).

This repository is **self-contained** — it includes everything needed to build. No external project references.

---

## Build

Clone and build the whole solution:

```bash
git clone https://github.com/vaclav-marko/SolutionReplicator.git
cd SolutionReplicator
dotnet build SolutionReplicator.sln
```

A clean build reports `0 Error(s)`.

> **If the app is already running**, Windows locks `bin/Debug/.../SolutionReplicator.exe` and the DLLs it copies. The build then reports `MSB3026`/`MSB3027` warnings on the final copy only — **not** a compilation error. Close the app and rebuild, or compile without the copy:
> ```bash
> dotnet build SolutionReplicator/SolutionReplicator.csproj -t:Compile
> ```

## Run

```bash
dotnet run --project SolutionReplicator/SolutionReplicator.csproj
```

or launch `SolutionReplicator/bin/Debug/net8.0-windows/SolutionReplicator.exe` directly.

---

## Step-by-step usage

1. **Load** — *Browse…* to the exported solution XML (the template). Click **Load**; the header shows the solution id, source country, and SI/BB counts. **Next**.
2. **Select** — tick scope items (ticking an SI ticks its BBs) and/or individual BBs. Use **Search**, **Select All**, **Deselect All**. **Next** is enabled once at least one item is selected.
3. **Repository & catalog** — *Browse…* the **Repository** to your folder of tokenized `.TXT` files, and *Browse…* the **Catalog** to a `.csv` file (it need not exist yet — it's created on save). The panel shows how many TXT files and distinct variables were found, and whether the CSV was loaded or is new. **Next** is enabled once the folder has at least one TXT.
4. **Target system & values** — pick a **Target system** from the dropdown or add a new one (enter its `sysid`). In the grid, edit each variable's **Value for this system**. Click **Save catalog** to write the CSV. **Next**.
5. **Compose** — Optionally tick **Rename solution to:** and enter a new solution id (pre-filled from the template). *Browse…* an **Output** folder, review the summary, click **Compose**. The log reports the consumed-file count, how many matched in the repo (and how many were unmatched/skipped), the output XML path and data folder, and how many files were written with substitutions. Then import the package into SAP Solution Builder and activate manually.

Last-used paths (template, repository, catalog, output) are remembered between runs and pre-filled (stored in `%LOCALAPPDATA%\SolutionReplicator\settings.json`).

---

## Output package

Written into the export folder:

```
<name>.xml                      # the template solution XML, filtered to the selection
<name>_installation_data/       # the matched repo .TXT files, placeholders substituted
    *.TXT
```

- `<name>` is the template's solution id, or a custom name if you ticked **Rename solution to:** in step 5.
- TXT files are written **UTF-16LE + BOM**, tab-delimited, CRLF; non-data lines are preserved verbatim; only the targeted columns are substituted (column-scoped, exact value match). `&PLN` under `I_WERKS` becomes e.g. `HU01`; a literal value under another column is left untouched.
- The solution XML is written UTF-8.
- Files are written under the **template's consumed filename** so the XML's references resolve.

---

## Project layout

```
SolutionReplicator/                       # repo root (self-contained, builds standalone)
├── SolutionReplicator.sln
├── SolutionReplicator/                    # the wizard shell app (WinExe, net8.0-windows, x64)
│   ├── App.xaml(.cs)
│   ├── Models/        (WizardContext, ConsumptionResult, PlaceholderTable)
│   ├── Services/      (ConsumptionResolver, ScopeItemMapper, PlaceholderTableStore,
│   │                   AppSettingsStore, TokenScanner, RepoFileMatcher, CatalogComposer)
│   ├── ViewModels/    (WizardHost, WizardStep, Load, SelectScope, RepoCatalog,
│   │                   ProfileValues, Compose)
│   └── Views/         (WizardShellWindow + one UserControl per step)
├── libs/                                  # bundled class libraries (built from source)
│   ├── SolutionBuilder/                   # XML parse/filter/transform, country rules
│   └── SapVariantReplacer/                # variant-file parse / extract / replace engine
└── tools/                                 # developer verification harnesses (not in the .sln)
    ├── ComposeProbe/                      # end-to-end placeholder-slice check
    └── ConsumptionProbe/                  # consumption-join regression check
```

`SolutionBuilder` and `SapVariantReplacer` were originally standalone WPF tools; here they are
bundled as **class libraries** so the shell reuses their proven logic and the whole thing builds
from a single clone. CommunityToolkit.Mvvm is unified on **8.4.0** across all projects.

---

## Architecture notes

- **Wizard** = `WizardHostViewModel` (owns the ordered steps + navigation) + `WizardStepViewModel` (abstract) + `WizardContext` (the single mutable state object passed to every step). Views resolve by implicit `DataTemplate` in `App.xaml` (step VM type → `UserControl`).
- **Compose pipeline** (`ComposeViewModel`):
  1. `ConsumptionResolver.Resolve(xml, selectedBbIds, selectedScopeItemIds)` → the consumed file set.
  2. `RepoFileMatcher.Match(consumedFiles, repoFolder)` → repo files matched by exact name (+ unmatched report).
  3. `SolutionProcessorService.PreparePackageAsync(…)` → the filtered, re-importable XML (solution id renamed if the checkbox was ticked).
  4. `ReplacementEngine.Apply(matchedFiles, changeSet, outputDataFolder, copyUnmodified: true)` → the populated TXTs.
- Selection is pure **string set-membership**, so custom `Z` / customer ids flow through unchanged.
- Placeholder substitution is **column-scoped** (matches on `(header, exact cell value)`), so `&X` under one column never matches `&X` under another.

---

## Verification harnesses

Two console harnesses (not part of the `.sln`) validate the logic headlessly. Each links the shell `.cs` files directly and references both libraries. They need **your own** sample solution XML / repository to exercise fully — they print a usage hint and exit cleanly when run without data.

```bash
cd tools/ComposeProbe
dotnet run -- <repoFolder> <templateXml>

cd tools/ConsumptionProbe
dotnet run -- <solutionXml> [<matchedDataFolder>]
```

---

## Known limitations

- **Output ships only the matched files**, but the solution XML still references the full `IMG_DEFINITION` / `BCSET_DEFINITION` catalogs (those sections aren't DOM-scoped). So the XML can reference files not shipped in `_installation_data`. **Validate on your first real Solution Builder re-import.**
- **Exact-name matching** assumes the repository files are named to match the template's consumed files one-to-one. Any consumed file without a repo match is reported and skipped (never fabricated).
- **Import to SAP + activation are manual** (out of scope by design).
- **Unmapped tokens** (no value for the chosen system) keep their placeholder in the output — intentional, so a half-filled system is obvious rather than silently blanked.
- The GUI is verified for startup/rendering and the compose logic is proven by the harnesses; a full interactive click-through against real data is a manual check.
