using System.IO;
using System.Text;
using System.Xml;
using SolutionBuilder.Models;

namespace SolutionBuilder.Services;

/// <summary>
/// Processes the XML file: filters scope items/BBs, renames country codes, copies files.
/// Uses a two-pass approach:
/// Pass 1: Read and buffer the XML into an XmlDocument (sections at a time for memory)
/// Pass 2: Filter and transform, then write output
/// 
/// Given the file size (~2M elements), we use XmlDocument since we need random access for filtering.
/// Modern .NET can handle this with sufficient RAM.
/// </summary>
public class SolutionProcessorService
{
    /// <summary>
    /// Result of <see cref="PreparePackageAsync"/>: the saved (filtered+transformed) XML
    /// path, the target installation-data folder, and the old→new file-name map so the
    /// caller can copy/transform the data files itself.
    /// </summary>
    public sealed class PreparedPackage
    {
        public required string OutputXmlPath { get; init; }
        public required string OutputDataFolder { get; init; }
        public required Dictionary<string, string> FileMapping { get; init; }
    }

    /// <summary>
    /// Main processing method (back-compat): prepares the package and copies every
    /// referenced data file as-is.
    /// </summary>
    public async Task ProcessAsync(
        string sourceXmlPath,
        string sourceDataFolder,
        string outputFolder,
        string newSolutionName,
        string targetCountry,
        string sourceCountry,
        HashSet<string> selectedScopeItemIds,
        HashSet<string> selectedBbIds,
        IProgress<string>? progress = null)
    {
        var prepared = await PreparePackageAsync(
            sourceXmlPath, outputFolder, newSolutionName, targetCountry, sourceCountry,
            selectedScopeItemIds, selectedBbIds, renameSolution: true, progress);

        await Task.Run(() =>
        {
            progress?.Report($"Copying {prepared.FileMapping.Count} data files...");
            CopyDataFiles(sourceDataFolder, prepared.OutputDataFolder, prepared.FileMapping, progress);
            progress?.Report($"Done! Output written to: {outputFolder}");
        });
    }

    /// <summary>
    /// Filters the solution to the selection, optionally rewrites country codes and the
    /// solution/package id, saves the resulting XML, and returns the old→new data-file
    /// name map — WITHOUT copying any data files (the caller does that, e.g. to apply
    /// value edits). Country rewrite is a no-op when sourceCountry == targetCountry;
    /// the solution/package id is left unchanged when <paramref name="renameSolution"/>
    /// is false.
    /// </summary>
    public async Task<PreparedPackage> PreparePackageAsync(
        string sourceXmlPath,
        string outputFolder,
        string newSolutionName,
        string targetCountry,
        string sourceCountry,
        HashSet<string> selectedScopeItemIds,
        HashSet<string> selectedBbIds,
        bool renameSolution = true,
        IProgress<string>? progress = null)
    {
        return await Task.Run(() => PreparePackage(
            sourceXmlPath, outputFolder, newSolutionName, targetCountry, sourceCountry,
            selectedScopeItemIds, selectedBbIds, renameSolution, progress));
    }

    private PreparedPackage PreparePackage(
        string sourceXmlPath,
        string outputFolder,
        string newSolutionName,
        string targetCountry,
        string sourceCountry,
        HashSet<string> selectedScopeItemIds,
        HashSet<string> selectedBbIds,
        bool renameSolution,
        IProgress<string>? progress)
    {
        var countryService = new CountryCodeService(sourceCountry, targetCountry);

        // Collect file mappings (old filename -> new filename) during XML processing
        var fileMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Output paths
        string outputXmlPath = Path.Combine(outputFolder, newSolutionName + ".xml");
        string outputDataFolder = Path.Combine(outputFolder, newSolutionName + "_installation_data");
        Directory.CreateDirectory(outputFolder);
        Directory.CreateDirectory(outputDataFolder);

        progress?.Report("Loading XML document (this may take a moment for large files)...");

        // Load full XML document
        var doc = new XmlDocument();
        doc.PreserveWhitespace = false;
        doc.Load(sourceXmlPath);

        var root = doc.DocumentElement;
        if (root == null)
        {
            progress?.Report("ERROR: No root element found in XML.");
            return new PreparedPackage { OutputXmlPath = outputXmlPath, OutputDataFolder = outputDataFolder, FileMapping = fileMapping };
        }

        progress?.Report("Filtering scope items...");
        FilterScenariosSequence(root, selectedScopeItemIds);
        FilterScenarios(root, selectedScopeItemIds);

        progress?.Report("Filtering building blocks...");
        FilterBuildingBlocks(root, selectedBbIds);

        progress?.Report("Filtering related sections...");
        FilterApplicationAreas(root, selectedScopeItemIds);
        FilterTaskCrit(root, selectedBbIds);
        FilterAssociation(root, selectedBbIds);
        FilterSecTasksText(root, selectedBbIds);

        progress?.Report("Applying transformations...");
        ApplyTransformations(root, countryService, newSolutionName, renameSolution, fileMapping);

        progress?.Report("Saving XML...");
        // Match the SAP export serialization: UTF-8 WITHOUT BOM, no indentation
        // (the /SMB/ Solution Builder importer rejects a BOM / reformatted structure).
        var writerSettings = new XmlWriterSettings
        {
            Indent = false,
            OmitXmlDeclaration = false,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        };
        using (var writer = XmlWriter.Create(outputXmlPath, writerSettings))
        {
            doc.Save(writer);
        }

        return new PreparedPackage
        {
            OutputXmlPath = outputXmlPath,
            OutputDataFolder = outputDataFolder,
            FileMapping = fileMapping
        };
    }

    #region Filtering

    private void FilterScenariosSequence(XmlElement root, HashSet<string> selectedScopeItemIds)
    {
        var scenariosSeq = root.SelectSingleNode("SCENARIOS_SEQUENCE");
        if (scenariosSeq == null) return;

        var itemsToRemove = new List<XmlNode>();
        foreach (XmlNode item in scenariosSeq.SelectNodes("item")!)
        {
            var prjIdNode = item.SelectSingleNode("PRJID");
            if (prjIdNode != null && !selectedScopeItemIds.Contains(prjIdNode.InnerText))
            {
                itemsToRemove.Add(item);
            }
        }

        foreach (var item in itemsToRemove)
            scenariosSeq.RemoveChild(item);
    }

    /// <summary>
    /// Filters the top-level SCENARIOS section (the scope-item definitions, each with
    /// PRJID + BB_SEQUENCE) to the selected scope items, so it stays consistent with the
    /// filtered SCENARIOS_SEQUENCE. (This section is distinct from APPLICATION_AREAS/SCENARIOS.)
    /// </summary>
    private void FilterScenarios(XmlElement root, HashSet<string> selectedScopeItemIds)
    {
        var scenarios = root.SelectSingleNode("SCENARIOS");
        if (scenarios == null) return;

        var itemsToRemove = new List<XmlNode>();
        foreach (XmlNode item in scenarios.SelectNodes("item")!)
        {
            var prjIdNode = item.SelectSingleNode("PRJID");
            if (prjIdNode != null && !selectedScopeItemIds.Contains(prjIdNode.InnerText))
            {
                itemsToRemove.Add(item);
            }
        }

        foreach (var item in itemsToRemove)
            scenarios.RemoveChild(item);
    }

    private void FilterBuildingBlocks(XmlElement root, HashSet<string> selectedBbIds)
    {
        var buildingBlocks = root.SelectSingleNode("BUILDING_BLOCKS");
        if (buildingBlocks == null) return;

        var itemsToRemove = new List<XmlNode>();
        foreach (XmlNode item in buildingBlocks.SelectNodes("item")!)
        {
            var bbIdNode = item.SelectSingleNode("BBID");
            if (bbIdNode != null && !selectedBbIds.Contains(bbIdNode.InnerText))
            {
                itemsToRemove.Add(item);
            }
        }

        foreach (var item in itemsToRemove)
            buildingBlocks.RemoveChild(item);
    }

    private void FilterApplicationAreas(XmlElement root, HashSet<string> selectedScopeItemIds)
    {
        var appAreas = root.SelectSingleNode("APPLICATION_AREAS");
        if (appAreas == null) return;

        // Within each application area, filter the SCENARIOS/item list
        foreach (XmlNode areaItem in appAreas.SelectNodes("item")!)
        {
            var scenarios = areaItem.SelectSingleNode("SCENARIOS");
            if (scenarios == null) continue;

            var itemsToRemove = new List<XmlNode>();
            foreach (XmlNode scenItem in scenarios.SelectNodes("item")!)
            {
                var scenNode = scenItem.SelectSingleNode("SCENARIO");
                if (scenNode != null && !selectedScopeItemIds.Contains(scenNode.InnerText))
                {
                    itemsToRemove.Add(scenItem);
                }
            }

            foreach (var item in itemsToRemove)
                scenarios.RemoveChild(item);
        }

        // Remove application areas that have no scenarios left
        var emptyAreas = new List<XmlNode>();
        foreach (XmlNode areaItem in appAreas.SelectNodes("item")!)
        {
            var scenarios = areaItem.SelectSingleNode("SCENARIOS");
            if (scenarios != null && scenarios.SelectNodes("item")!.Count == 0)
            {
                emptyAreas.Add(areaItem);
            }
        }

        foreach (var item in emptyAreas)
            appAreas.RemoveChild(item);
    }

    private void FilterTaskCrit(XmlElement root, HashSet<string> selectedBbIds)
    {
        var taskCrit = root.SelectSingleNode("TASK_CRIT");
        if (taskCrit == null) return;

        var itemsToRemove = new List<XmlNode>();
        foreach (XmlNode item in taskCrit.SelectNodes("item")!)
        {
            var bbIdNode = item.SelectSingleNode("BBID");
            if (bbIdNode != null && !selectedBbIds.Contains(bbIdNode.InnerText))
            {
                itemsToRemove.Add(item);
            }
        }

        foreach (var item in itemsToRemove)
            taskCrit.RemoveChild(item);
    }

    private void FilterAssociation(XmlElement root, HashSet<string> selectedBbIds)
    {
        var association = root.SelectSingleNode("ASSOCIATION");
        if (association == null) return;

        var itemsToRemove = new List<XmlNode>();
        foreach (XmlNode item in association.SelectNodes("item")!)
        {
            var bbIdNode = item.SelectSingleNode("BBID_PRI");
            if (bbIdNode != null && !selectedBbIds.Contains(bbIdNode.InnerText))
            {
                itemsToRemove.Add(item);
            }
        }

        foreach (var item in itemsToRemove)
            association.RemoveChild(item);
    }

    private void FilterSecTasksText(XmlElement root, HashSet<string> selectedBbIds)
    {
        var secTasksText = root.SelectSingleNode("SEC_TASKS_TEXT");
        if (secTasksText == null) return;

        // SEC_TASKS_TEXT items reference BBs indirectly through OBJID_SEC
        // We filter based on the FILENAME_SEC matching files from selected BBs
        // For now, keep all SEC_TASKS_TEXT items that reference any selected BB
        // This section doesn't have BBID directly, so we keep it all for safety
        // unless we can correlate via other means.
        // Actually looking at structure, there's no direct BB reference - keep all.
    }

    #endregion

    #region Transformations

    private void ApplyTransformations(XmlElement root, CountryCodeService countryService,
        string newSolutionName, bool renameSolution, Dictionary<string, string> fileMapping)
    {
        // Walk all text nodes and transform based on their parent element name
        ApplyTransformationsRecursive(root, countryService, newSolutionName, renameSolution, fileMapping);
    }

    private void ApplyTransformationsRecursive(XmlNode node, CountryCodeService countryService,
        string newSolutionName, bool renameSolution, Dictionary<string, string> fileMapping)
    {
        if (node.NodeType == XmlNodeType.Text || node.NodeType == XmlNodeType.CDATA)
        {
            string parentName = node.ParentNode?.LocalName ?? "";
            string originalValue = node.Value ?? "";
            string newValue = TransformTextValue(originalValue, parentName, countryService, newSolutionName, renameSolution, fileMapping);

            if (newValue != originalValue)
                node.Value = newValue;

            return;
        }

        // Process child nodes (copy list to avoid modification during iteration)
        var children = new List<XmlNode>();
        foreach (XmlNode child in node.ChildNodes)
            children.Add(child);

        foreach (var child in children)
            ApplyTransformationsRecursive(child, countryService, newSolutionName, renameSolution, fileMapping);
    }

    private string TransformTextValue(string value, string parentElementName,
        CountryCodeService countryService, string newSolutionName, bool renameSolution,
        Dictionary<string, string> fileMapping)
    {
        if (string.IsNullOrEmpty(value)) return value;

        switch (parentElementName)
        {
            // Solution id (SCOPE / SOLUID) — renamed only when the user asked to.
            case "SCOPE":
            case "SOLUID":
            case "SOLUTION_ID":
                return renameSolution ? newSolutionName : value;

            // Package id is NOT the solution id — never overwrite it (doing so breaks import).
            case "PKG_ID":
            case "PACKAGE_ID":
                return value;

            // Country fields
            case "LAND1":
            case "SIR_SI_COUNTRY":
            case "SIR_BB_COUNTRY":
                return countryService.ReplaceCountryField(value);

            // Scope Item Project ID
            case "PRJID":
            case "SCENARIO": // in APPLICATION_AREAS
                return countryService.ReplaceInProjectId(value);

            // Building Block IDs
            case "BBID":
            case "BBID_PRI":
            case "PREQ_BB":
                return countryService.ReplaceInBbId(value);

            // Filenames
            case "FILENAME":
            case "FILENAME_PRI":
            case "FILENAME_SEC":
            case "PRIM_FILE":
            case "SEC_FILE":
            case "MDF_EXT_FILE":
            case "VAR_EXT_FILE":
            case "EXT_FILENAME":
                string newFilename = countryService.ReplaceInFilename(value);
                if (!fileMapping.ContainsKey(value))
                {
                    fileMapping[value] = newFilename;
                }
                return newFilename;

            // PARENT field that may contain BB references like "1IM (XX)->00000"
            case "PARENT":
                if (value.Contains("("))
                    return countryService.ReplaceInBbId(value);
                return value;

            // ENTITY_VALUE in TARGET_SYSTEMS
            case "ENTITY_VALUE":
                if (renameSolution && (value.Contains("BP_OP") || value.Contains("_S4HANA")))
                    return newSolutionName;
                return value;

            default:
                return value;
        }
    }

    #endregion

    #region File Copying

    private void CopyDataFiles(string sourceDataFolder, string outputDataFolder,
        Dictionary<string, string> fileMapping, IProgress<string>? progress)
    {
        int copied = 0;
        int missing = 0;
        var missingFiles = new List<string>();

        foreach (var kvp in fileMapping)
        {
            string oldFilename = kvp.Key;
            string newFilename = kvp.Value;

            if (string.IsNullOrWhiteSpace(oldFilename)) continue;

            string sourcePath = Path.Combine(sourceDataFolder, oldFilename);
            string destPath = Path.Combine(outputDataFolder, newFilename);

            if (File.Exists(sourcePath))
            {
                File.Copy(sourcePath, destPath, overwrite: true);
                copied++;
            }
            else
            {
                missing++;
                missingFiles.Add(oldFilename);
            }
        }

        progress?.Report($"Files copied: {copied}, Missing: {missing}");

        if (missingFiles.Count > 0 && missingFiles.Count <= 20)
        {
            foreach (var f in missingFiles)
                progress?.Report($"  Missing: {f}");
        }
        else if (missingFiles.Count > 20)
        {
            progress?.Report($"  (Too many missing files to list - showing first 10:)");
            foreach (var f in missingFiles.Take(10))
                progress?.Report($"  Missing: {f}");
        }
    }

    #endregion
}