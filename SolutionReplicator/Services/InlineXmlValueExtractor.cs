using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using SapVariantReplacer.Models;

namespace SolutionReplicator.Services;

/// <summary>
/// Extracts editable (parameter, value) pairs embedded directly in the solution XML —
/// the BC-Set variant values in BCSET_DEFINITION/item/MDT_VARIANTS/item (PARAM +
/// VALUE/OLDVALUE). This is the "XML contains some data" source for the edit grid,
/// complementing the external TXT files. Scoped to the consumed parameters.
/// </summary>
public sealed class InlineXmlValueExtractor
{
    /// <param name="consumedParams">If non-empty, only parameters in this set are returned.</param>
    public List<FieldValueEntry> Extract(string xmlPath, IReadOnlySet<string> consumedParams)
    {
        // (FieldName, OriginalValue) -> occurrence count
        var counts = new Dictionary<(string Field, string Value), int>();

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

            if (reader.LocalName != "BCSET_DEFINITION")
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
                    var bc = XElement.Load(reader.ReadSubtree());
                    foreach (var v in bc.Element("MDT_VARIANTS")?.Elements("item") ?? Enumerable.Empty<XElement>())
                    {
                        string param = (v.Element("PARAM")?.Value ?? "").Trim();
                        if (param.Length == 0) continue;
                        if (consumedParams.Count > 0 && !consumedParams.Contains(param)) continue;

                        string value = NonEmpty(v, "VALUE") ?? NonEmpty(v, "OLDVALUE") ?? "";
                        if (value.Length == 0) continue;

                        var key = (param, value);
                        counts[key] = counts.TryGetValue(key, out var c) ? c + 1 : 1;
                    }
                }
            }
        }

        return counts
            .OrderBy(kvp => kvp.Key.Field, System.StringComparer.OrdinalIgnoreCase)
            .ThenBy(kvp => kvp.Key.Value, System.StringComparer.OrdinalIgnoreCase)
            .Select(kvp => new FieldValueEntry
            {
                FieldName = kvp.Key.Field,
                OriginalValue = kvp.Key.Value,
                NewValue = kvp.Key.Value,
                OccurrenceCount = kvp.Value
            })
            .ToList();
    }

    private static string? NonEmpty(XElement e, string name)
    {
        string? v = e.Element(name)?.Value;
        return string.IsNullOrEmpty(v) ? null : v;
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
