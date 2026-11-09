# SAP Solution Builder - Country Localization Tool

A WPF application for creating localized SAP Best Practice solutions from an existing solution XML file.

## Features

- **Load** large SAP Best Practice solution XML files (streaming parser for metadata extraction)
- **Browse** scope items and building blocks in a collapsible tree view with checkboxes
- **Select/Deselect** individual scope items and building blocks to compose a custom solution
- **Search/Filter** scope items by name or ID
- **Process** to generate a new localized solution with:
  - Country code replacement in scope item IDs, building block IDs, and filenames
  - Filtering to include only selected scope items and their building blocks
  - Cleanup of related XML sections (APPLICATION_AREAS, TASK_CRIT, ASSOCIATION, etc.)
  - Copying and renaming of referenced data files

## Usage

1. **Source XML**: Browse to the SAP Best Practice solution XML file
2. **Data Folder**: The folder containing the TXT data files (auto-detected as sibling folder with same name as XML)
3. **Output Folder**: Where the new solution will be generated
4. **Solution Name**: Enter the new solution identifier
5. **Target Country**: 2-letter country code (e.g., CZ, US, FR)
6. **Source Country**: Auto-detected from the XML file's LAND1 field
7. Click **Load** to parse the XML and populate the tree view
8. Use checkboxes to select/deselect scope items and building blocks
9. Click **Process** to generate the new solution

## Output Structure

```
<output_folder>/
├── <new_solution_name>.xml          # Modified solution XML
└── <new_solution_name>_installation_data/
    ├── file1.TXT                     # Renamed data files
    ├── file2.TXT
    └── ...
```

## Country Code Replacement Rules

| Element Type | Example Before | Example After (target: CZ) |
|---|---|---|
| Scope Item PRJID | DE_BD3_OP | CZ_BD3_OP |
| Local Building Block | BLV (DE) | BLV (CZ) |
| Global Building Block | 1IM (XX) | 1IM (XX) (unchanged) |
| Filename with country | DATA_DE.TXT | DATA_CZ.TXT |
| Filename with counter | DATA_DE_01.TXT | DATA_CZ_01.TXT |
| Filename without country | DATA.TXT | DATA.TXT (unchanged) |

## Requirements

- .NET 8.0 Windows Desktop Runtime
- Windows 10 or later
- Sufficient RAM for loading large XML files (recommended: 16GB+)

## Building

```bash
cd SolutionBuilder
dotnet build
dotnet run
```

## Architecture

- **Models/** - Data classes (SolutionData, ScopeItem, BuildingBlock, TaskActivity)
- **ViewModels/** - MVVM ViewModels (MainViewModel, TreeNodeViewModel)
- **Services/** - Business logic
  - `XmlParserService` - Streaming XML parser for metadata extraction
  - `CountryCodeService` - Country code replacement logic
  - `SolutionProcessorService` - Full XML transformation and file copying
- **Converters/** - WPF value converters