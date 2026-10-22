using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace SolutionReplicator.Services;

/// <summary>One scope item with its building-block ids, as defined in the solution XML.</summary>
public sealed class ScopeItemInfo
{
    public required string ProjectId { get; init; }     // PRJID, e.g. XX_287_OP
    public string SirSiId { get; init; } = string.Empty; // SIR_SI_ID, e.g. 287
    public string Country { get; init; } = string.Empty; // SIR_SI_COUNTRY
    public string Description { get; init; } = string.Empty;
    public List<string> BuildingBlockIds { get; } = new();
}

/// <summary>
/// Reads the real scope-item → building-block mapping. The SI definitions live in a
/// top-level Solution/SCENARIOS section (sibling of SCENARIOS_SEQUENCE): each item has
/// PRJID, DESCRIPTION, and BB_SEQUENCE/item/BBID. This is NOT the SCENARIOS_SEQUENCE
/// (ordering only, which is why the reused XmlParserService leaves BuildingBlockIds
/// empty), nor the APPLICATION_AREAS/item/SCENARIOS (singular SCENARIO refs). Items are
/// identified by the presence of a PRJID. Streams per-item to stay memory-safe.
/// </summary>
public sealed class ScopeItemMapper
{
    public List<ScopeItemInfo> Map(string xmlPath)
    {
        var result = new List<ScopeItemInfo>();
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            IgnoreWhitespace = true,
            IgnoreComments = true
        };
        using var reader = XmlReader.Create(xmlPath, settings);

        while (reader.Read())
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Solution")
                break;

        int rootDepth = reader.Depth;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == rootDepth) break;
            if (reader.NodeType != XmlNodeType.Element || reader.Depth != rootDepth + 1) continue;

            // The SI-def list is the top-level SCENARIOS section; its items carry PRJID.
            if (reader.LocalName != "SCENARIOS")
            {
                if (!reader.IsEmptyElement) Skip(reader);
                continue;
            }

            int depth = reader.Depth;
            if (reader.IsEmptyElement) continue;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth) break;
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "item" && reader.Depth == depth + 1)
                {
                    var sc = XElement.Load(reader.ReadSubtree());
                    if (!string.IsNullOrEmpty(sc.Element("PRJID")?.Value))
                        result.Add(ReadScenario(sc));
                }
            }
        }

        return result;
    }

    private static ScopeItemInfo ReadScenario(XElement sc)
    {
        var info = new ScopeItemInfo
        {
            ProjectId = sc.Element("PRJID")?.Value.Trim() ?? "",
            SirSiId = sc.Element("SIR_SI_ID")?.Value.Trim() ?? "",
            Country = sc.Element("SIR_SI_COUNTRY")?.Value.Trim() ?? "",
            Description = EnglishText(sc.Element("DESCRIPTION"))
        };

        foreach (var item in sc.Element("BB_SEQUENCE")?.Elements("item") ?? Enumerable.Empty<XElement>())
        {
            string bbid = item.Element("BBID")?.Value.Trim() ?? "";
            if (bbid.Length > 0) info.BuildingBlockIds.Add(bbid);
        }
        return info;
    }

    /// <summary>Pick the English (SPRAS="E") text, else the first available.</summary>
    private static string EnglishText(XElement? description)
    {
        if (description is null) return "";
        string? first = null;
        foreach (var item in description.Elements("item"))
        {
            string text = item.Element("TEXT")?.Value ?? "";
            first ??= text;
            if (string.Equals(item.Element("SPRAS")?.Value, "E", StringComparison.OrdinalIgnoreCase))
                return text;
        }
        return first ?? "";
    }

    private static void Skip(XmlReader reader)
    {
        if (reader.IsEmptyElement) return;
        int depth = reader.Depth;
        while (reader.Read())
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                break;
    }
}
