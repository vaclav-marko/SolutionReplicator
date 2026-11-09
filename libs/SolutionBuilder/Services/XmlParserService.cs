using System.Xml;
using SolutionBuilder.Models;

namespace SolutionBuilder.Services;

/// <summary>
/// Streaming XML parser that extracts solution metadata for the UI tree.
/// Uses XmlReader for memory efficiency on large files.
/// </summary>
public class XmlParserService
{
    public async Task<SolutionData> ParseSolutionMetadataAsync(string xmlFilePath, IProgress<string>? progress = null)
    {
        return await Task.Run(() => ParseSolutionMetadata(xmlFilePath, progress));
    }

    private SolutionData ParseSolutionMetadata(string xmlFilePath, IProgress<string>? progress)
    {
        var solution = new SolutionData();
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            IgnoreWhitespace = true,
            IgnoreComments = true
        };

        progress?.Report("Parsing solution metadata...");

        using var reader = XmlReader.Create(xmlFilePath, settings);

        // Navigate to root Solution element
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Solution")
                break;
        }

        // Read through Solution children at depth 1
        int solutionDepth = reader.Depth;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == solutionDepth)
                break;

            if (reader.NodeType != XmlNodeType.Element) continue;
            if (reader.Depth != solutionDepth + 1) continue;

            switch (reader.LocalName)
            {
                case "SCOPE":
                    solution.SolutionId = reader.ReadElementContentAsString();
                    break;
                case "LAND1":
                    solution.SourceCountry = reader.ReadElementContentAsString();
                    break;
                case "PKG_ID":
                    solution.PackageId = reader.ReadElementContentAsString();
                    break;
                case "PROD_ALIAS":
                    solution.ProductAlias = reader.ReadElementContentAsString();
                    break;
                case "PROD_VERS_ALIAS":
                    solution.ProductVersionAlias = reader.ReadElementContentAsString();
                    break;
                case "SCENARIOS_SEQUENCE":
                    progress?.Report("Parsing scope items...");
                    ParseScenariosSequence(reader, solution);
                    break;
                case "BUILDING_BLOCKS":
                    progress?.Report("Parsing building blocks...");
                    ParseBuildingBlocks(reader, solution);
                    break;
                default:
                    if (!reader.IsEmptyElement)
                        SkipElement(reader);
                    break;
            }
        }

        progress?.Report($"Parsed {solution.ScopeItems.Count} scope items, {solution.BuildingBlocks.Count} building blocks.");
        return solution;
    }

    private void ParseScenariosSequence(XmlReader reader, SolutionData solution)
    {
        int depth = reader.Depth;
        if (reader.IsEmptyElement) return;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                break;

            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "item" && reader.Depth == depth + 1)
            {
                var si = ParseScopeItem(reader);
                if (!string.IsNullOrEmpty(si.ProjectId))
                    solution.ScopeItems.Add(si);
            }
        }
    }

    private ScopeItem ParseScopeItem(XmlReader reader)
    {
        var si = new ScopeItem();
        int itemDepth = reader.Depth;
        if (reader.IsEmptyElement) return si;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == itemDepth)
                break;

            if (reader.NodeType != XmlNodeType.Element) continue;
            if (reader.Depth != itemDepth + 1) continue;

            switch (reader.LocalName)
            {
                case "PRJID":
                    si.ProjectId = reader.ReadElementContentAsString();
                    break;
                case "SIR_SI_COUNTRY":
                    si.SirSiCountry = reader.ReadElementContentAsString();
                    break;
                case "SIR_SI_ID":
                    si.SirSiId = reader.ReadElementContentAsString();
                    break;
                case "DESCRIPTION":
                    si.Description = ParseEnglishDescription(reader);
                    break;
                case "BB_SEQUENCE":
                    ParseBbSequence(reader, si);
                    break;
                default:
                    if (!reader.IsEmptyElement)
                        SkipElement(reader);
                    break;
            }
        }

        return si;
    }

    /// <summary>
    /// Parses a DESCRIPTION element and extracts the English text (SPRAS="E").
    /// Always fully consumes the DESCRIPTION element.
    /// </summary>
    private string ParseEnglishDescription(XmlReader reader)
    {
        int depth = reader.Depth;
        if (reader.IsEmptyElement) return string.Empty;

        string result = string.Empty;
        bool foundEnglish = false;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                break;

            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "item" && reader.Depth == depth + 1)
            {
                int itemDepth = reader.Depth;
                if (reader.IsEmptyElement) continue;

                string spras = "";
                string text = "";

                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == itemDepth)
                        break;

                    if (reader.NodeType == XmlNodeType.Element && reader.Depth == itemDepth + 1)
                    {
                        switch (reader.LocalName)
                        {
                            case "SPRAS":
                                spras = reader.ReadElementContentAsString();
                                break;
                            case "TEXT":
                                text = reader.ReadElementContentAsString();
                                break;
                            default:
                                if (!reader.IsEmptyElement)
                                    SkipElement(reader);
                                break;
                        }
                    }
                }

                if (spras == "E")
                {
                    result = text;
                    foundEnglish = true;
                }
                else if (!foundEnglish && string.IsNullOrEmpty(result))
                {
                    result = text;
                }
            }
        }

        return result;
    }

    private void ParseBbSequence(XmlReader reader, ScopeItem si)
    {
        int depth = reader.Depth;
        if (reader.IsEmptyElement) return;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                break;

            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "item" && reader.Depth == depth + 1)
            {
                int itemDepth = reader.Depth;
                if (reader.IsEmptyElement) continue;

                string bbId = "";

                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == itemDepth)
                        break;

                    if (reader.NodeType == XmlNodeType.Element && reader.Depth == itemDepth + 1)
                    {
                        if (reader.LocalName == "BBID")
                        {
                            bbId = reader.ReadElementContentAsString();
                        }
                        else if (!reader.IsEmptyElement)
                        {
                            SkipElement(reader);
                        }
                    }
                }

                if (!string.IsNullOrEmpty(bbId))
                    si.BuildingBlockIds.Add(bbId);
            }
        }
    }

    private void ParseBuildingBlocks(XmlReader reader, SolutionData solution)
    {
        int depth = reader.Depth;
        if (reader.IsEmptyElement) return;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                break;

            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "item" && reader.Depth == depth + 1)
            {
                var bb = ParseBuildingBlock(reader);
                if (!string.IsNullOrEmpty(bb.BbId))
                    solution.BuildingBlocks.Add(bb);
            }
        }
    }

    private BuildingBlock ParseBuildingBlock(XmlReader reader)
    {
        var bb = new BuildingBlock();
        int itemDepth = reader.Depth;
        if (reader.IsEmptyElement) return bb;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == itemDepth)
                break;

            if (reader.NodeType != XmlNodeType.Element) continue;
            if (reader.Depth != itemDepth + 1) continue;

            switch (reader.LocalName)
            {
                case "BBID":
                    bb.BbId = reader.ReadElementContentAsString();
                    break;
                case "SIR_BB_ID":
                    bb.SirBbId = reader.ReadElementContentAsString();
                    break;
                case "SIR_BB_COUNTRY":
                    bb.SirBbCountry = reader.ReadElementContentAsString();
                    break;
                case "DESCRIPTION":
                    bb.Description = ParseEnglishDescription(reader);
                    break;
                case "TASKS":
                    ParseTasks(reader, bb);
                    break;
                case "IMG_SUBOBJECTS":
                    ParseImgSubObjects(reader, bb);
                    break;
                default:
                    if (!reader.IsEmptyElement)
                        SkipElement(reader);
                    break;
            }
        }

        return bb;
    }

    private void ParseTasks(XmlReader reader, BuildingBlock bb)
    {
        int depth = reader.Depth;
        if (reader.IsEmptyElement) return;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                break;

            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "item" && reader.Depth == depth + 1)
            {
                var task = ParseTask(reader);
                if (!string.IsNullOrEmpty(task.ObjectId) || !string.IsNullOrEmpty(task.FileName))
                    bb.Tasks.Add(task);
            }
        }
    }

    private TaskActivity ParseTask(XmlReader reader)
    {
        var task = new TaskActivity();
        int itemDepth = reader.Depth;
        if (reader.IsEmptyElement) return task;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == itemDepth)
                break;

            if (reader.NodeType != XmlNodeType.Element) continue;
            if (reader.Depth != itemDepth + 1) continue;

            switch (reader.LocalName)
            {
                case "OBJID":
                    task.ObjectId = reader.ReadElementContentAsString();
                    break;
                case "OBJTY":
                    task.ObjectType = reader.ReadElementContentAsString();
                    break;
                case "ORDER_NUMBER":
                    task.OrderNumber = reader.ReadElementContentAsString();
                    break;
                case "FILENAME":
                    task.FileName = reader.ReadElementContentAsString();
                    break;
                case "DESCRIPTION":
                    task.Description = ParseEnglishDescription(reader);
                    break;
                default:
                    if (!reader.IsEmptyElement)
                        SkipElement(reader);
                    break;
            }
        }

        return task;
    }

    private void ParseImgSubObjects(XmlReader reader, BuildingBlock bb)
    {
        int depth = reader.Depth;
        if (reader.IsEmptyElement) return;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                break;

            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "item" && reader.Depth == depth + 1)
            {
                int itemDepth = reader.Depth;
                if (reader.IsEmptyElement) continue;

                string filename = "";

                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == itemDepth)
                        break;

                    if (reader.NodeType == XmlNodeType.Element && reader.Depth == itemDepth + 1)
                    {
                        if (reader.LocalName == "FILENAME")
                        {
                            filename = reader.ReadElementContentAsString();
                        }
                        else if (!reader.IsEmptyElement)
                        {
                            SkipElement(reader);
                        }
                    }
                }

                if (!string.IsNullOrEmpty(filename))
                    bb.ImgSubObjectFiles.Add(filename);
            }
        }
    }

    private void SkipElement(XmlReader reader)
    {
        if (reader.IsEmptyElement) return;
        int depth = reader.Depth;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                break;
        }
    }
}