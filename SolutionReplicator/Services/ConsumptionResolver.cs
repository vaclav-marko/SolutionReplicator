using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using SolutionReplicator.Models;

namespace SolutionReplicator.Services;

/// <summary>
/// Computes the installation-data files and import parameters a selection of building
/// blocks consumes, by streaming the solution XML.
///
/// Performance notes:
///   BUILDING_BLOCKS (30k+ items) and IMG_DEFINITION (50k+ items) are the hot sections.
///   Both use a direct XmlReader path that reads only the discriminator field and skips
///   non-matching items without allocating an XElement, giving ~50-100× speedup over
///   loading every item as XElement. Smaller sections (BCSET, ASSOCIATION, ADDITION)
///   keep the XElement approach for simplicity.
/// </summary>
public sealed class ConsumptionResolver
{
    private const char KeySep = '\u0001';

    public ConsumptionResult Resolve(
        string xmlPath,
        IReadOnlySet<string> selectedBbIds,
        IReadOnlySet<string>? selectedScopeItemIds = null)
    {
        var result = new ConsumptionResult();
        var imgKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bcsetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var additionPairs = new List<(string Prim, string Sec)>();
        var paramMap = new Dictionary<string, ConsumedParameter>(StringComparer.OrdinalIgnoreCase);

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

            switch (reader.LocalName)
            {
                case "BUILDING_BLOCKS":
                    ProcessBuildingBlocksDirect(reader, selectedBbIds, result.ConsumedFiles, imgKeys, bcsetNames);
                    break;
                case "BCSET_DEFINITION":
                    ForEachItem(reader, el => HandleBcset(el, bcsetNames, result.ConsumedFiles, paramMap));
                    break;
                case "IMG_DEFINITION":
                    ProcessImgDefinitionDirect(reader, imgKeys, result.ConsumedFiles, paramMap);
                    break;
                case "ASSOCIATION":
                    ForEachItem(reader, el => HandleAssociation(el, selectedBbIds, result.ConsumedFiles));
                    break;
                case "ADDITION":
                    ForEachItem(reader, el => additionPairs.Add((Val(el, "PRIM_FILE"), Val(el, "SEC_FILE"))));
                    break;
                default:
                    if (!reader.IsEmptyElement) SkipElement(reader);
                    break;
            }
        }

        // Fix-point over ADDITION companions.
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var (prim, sec) in additionPairs)
                if (!string.IsNullOrEmpty(sec) && result.ConsumedFiles.Contains(prim) && result.ConsumedFiles.Add(sec))
                    changed = true;
        }

        result.ConsumedParams.AddRange(paramMap.Values.OrderBy(p => p.Param, StringComparer.OrdinalIgnoreCase));
        return result;
    }

    // ---- fast direct-reader sections ----------------------------------------

    /// <summary>
    /// Streams BUILDING_BLOCKS items. Reads BBID (first field) with the raw reader;
    /// non-selected items are skipped in one Skip() call without building XElement.
    /// </summary>
    private static void ProcessBuildingBlocksDirect(
        XmlReader outer,
        IReadOnlySet<string> selectedBbIds,
        ISet<string> files,
        ISet<string> imgKeys,
        ISet<string> bcsetNames)
    {
        int sectionDepth = outer.Depth;
        if (outer.IsEmptyElement) return;

        while (outer.Read())
        {
            if (outer.NodeType == XmlNodeType.EndElement && outer.Depth == sectionDepth) break;
            if (outer.NodeType != XmlNodeType.Element || outer.LocalName != "item" || outer.Depth != sectionDepth + 1) continue;

            // Use a subtree reader so the outer reader always ends up after </item>.
            using var sub = outer.ReadSubtree();
            sub.Read(); // position on <item>

            ReadBuildingBlockItem(sub, selectedBbIds, files, imgKeys, bcsetNames);
        }
    }

    private static void ReadBuildingBlockItem(
        XmlReader sub,
        IReadOnlySet<string> selectedBbIds,
        ISet<string> files,
        ISet<string> imgKeys,
        ISet<string> bcsetNames)
    {
        // sub is positioned on <item>. Advance into first child.
        if (!AdvanceToFirstChild(sub)) return;

        bool selected = false;

        // Forward-only read through the item without double-advancing.
        // After ReadLeafValue() or sub.Skip(), the reader is at the next sibling
        // (or parent EndElement) — so the while checks again WITHOUT a Read().
        while (!sub.EOF && sub.NodeType != XmlNodeType.EndElement)
        {
            if (sub.NodeType != XmlNodeType.Element) { sub.Read(); continue; }

            string name = sub.LocalName;

            if (!selected)
            {
                if (name == "BBID")
                {
                    string bbid = ReadLeafValue(sub); // advances past </BBID>
                    if (!selectedBbIds.Contains(bbid)) return; // subtree disposed → outer skips item
                    selected = true;
                    continue; // reader already at next sibling
                }
                sub.Skip(); // not BBID yet, skip this element
                continue;
            }

            // BB is selected — process sub-sections we care about.
            switch (name)
            {
                case "TASKS":
                    ReadTasksSection(sub, files, bcsetNames);
                    break;
                case "IMG_SUBOBJECTS":
                    ReadImgSubobjectsSection(sub, files, imgKeys);
                    break;
                default:
                    sub.Skip();
                    break;
            }
            // After ReadTasksSection / ReadImgSubobjectsSection / Skip,
            // the reader is positioned at the next sibling.
        }
    }

    private static void ReadTasksSection(XmlReader sub, ISet<string> files, ISet<string> bcsetNames)
    {
        if (sub.IsEmptyElement) { sub.Read(); return; }
        int depth = sub.Depth;
        sub.Read(); // into TASKS

        while (!sub.EOF && !(sub.NodeType == XmlNodeType.EndElement && sub.Depth == depth))
        {
            if (sub.NodeType != XmlNodeType.Element || sub.LocalName != "item") { sub.Read(); continue; }

            // Read a TASKS/item directly
            using var itemSub = sub.ReadSubtree();
            itemSub.Read();
            if (!AdvanceToFirstChild(itemSub)) continue;

            string filename = "", objty = "", objid = "";
            while (!itemSub.EOF && itemSub.NodeType != XmlNodeType.EndElement)
            {
                if (itemSub.NodeType != XmlNodeType.Element) { itemSub.Read(); continue; }
                switch (itemSub.LocalName)
                {
                    case "FILENAME": filename = ReadLeafValue(itemSub); break;
                    case "OBJTY":    objty    = ReadLeafValue(itemSub); break;
                    case "OBJID":    objid    = ReadLeafValue(itemSub); break;
                    default:         itemSub.Skip(); break;
                }
            }
            AddFile(files, filename);
            if (string.Equals(objty, "BCS", StringComparison.OrdinalIgnoreCase) && objid.Length > 0)
                bcsetNames.Add(objid);
        }
        // After the loop, sub is at </TASKS> EndElement.
        // Advance past it so the caller is at the next sibling.
        if (!sub.EOF && sub.NodeType == XmlNodeType.EndElement) sub.Read();
    }

    private static void ReadImgSubobjectsSection(XmlReader sub, ISet<string> files, ISet<string> imgKeys)
    {
        if (sub.IsEmptyElement) { sub.Read(); return; }
        int depth = sub.Depth;
        sub.Read(); // into IMG_SUBOBJECTS

        while (!sub.EOF && !(sub.NodeType == XmlNodeType.EndElement && sub.Depth == depth))
        {
            if (sub.NodeType != XmlNodeType.Element || sub.LocalName != "item") { sub.Read(); continue; }

            using var itemSub = sub.ReadSubtree();
            itemSub.Read();
            if (!AdvanceToFirstChild(itemSub)) continue;

            string filename = "", act = "", objtype = "", objname = "", subname = "";
            while (!itemSub.EOF && itemSub.NodeType != XmlNodeType.EndElement)
            {
                if (itemSub.NodeType != XmlNodeType.Element) { itemSub.Read(); continue; }
                switch (itemSub.LocalName)
                {
                    case "FILENAME":      filename = ReadLeafValue(itemSub); break;
                    case "IMG_ACTIVITY":  act      = ReadLeafValue(itemSub); break;
                    case "CUST_OBJTYPE":  objtype  = ReadLeafValue(itemSub); break;
                    case "CUST_OBJNAME":  objname  = ReadLeafValue(itemSub); break;
                    case "SUBOBJNAME":    subname  = ReadLeafValue(itemSub); break;
                    default:              itemSub.Skip(); break;
                }
            }
            AddFile(files, filename);
            if (act.Length > 0)
                imgKeys.Add(ImgKey(act, objtype, objname, subname));
        }
        if (!sub.EOF && sub.NodeType == XmlNodeType.EndElement) sub.Read();
    }

    /// <summary>
    /// Streams IMG_DEFINITION items. Reads IMG_ACTIVITY first; if no imgKey shares that
    /// activity, skips the item immediately. Then checks the full 4-field key.
    /// </summary>
    private static void ProcessImgDefinitionDirect(
        XmlReader outer,
        IReadOnlySet<string> imgKeys,
        ISet<string> files,
        Dictionary<string, ConsumedParameter> paramMap)
    {
        // Pre-build a set of active IMG_ACTIVITY values for O(1) pre-filter.
        var activeActivities = new HashSet<string>(
            imgKeys.Select(k => k.Split(KeySep)[0]),
            StringComparer.OrdinalIgnoreCase);

        int sectionDepth = outer.Depth;
        if (outer.IsEmptyElement) return;

        while (outer.Read())
        {
            if (outer.NodeType == XmlNodeType.EndElement && outer.Depth == sectionDepth) break;
            if (outer.NodeType != XmlNodeType.Element || outer.LocalName != "item" || outer.Depth != sectionDepth + 1) continue;

            using var sub = outer.ReadSubtree();
            sub.Read();

            ReadImgDefinitionItem(sub, activeActivities, imgKeys, files, paramMap);
        }
    }

    private static void ReadImgDefinitionItem(
        XmlReader sub,
        IReadOnlySet<string> activeActivities,
        IReadOnlySet<string> imgKeys,
        ISet<string> files,
        Dictionary<string, ConsumedParameter> paramMap)
    {
        if (!AdvanceToFirstChild(sub)) return;

        string act = "", objtype = "", objname = "", subname = "";
        bool actChecked = false;
        var tdcAliasToFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var fieldParams = new List<(string Param, string Field, string Central)>();

        while (!sub.EOF && sub.NodeType != XmlNodeType.EndElement)
        {
            if (sub.NodeType != XmlNodeType.Element) { sub.Read(); continue; }

            string name = sub.LocalName;

            // Read the key fields first; bail early if activity doesn't match any key.
            switch (name)
            {
                case "IMG_ACTIVITY":
                    act = ReadLeafValue(sub);
                    if (!activeActivities.Contains(act)) return; // fast skip
                    actChecked = true;
                    continue;
                case "CUST_OBJTYPE":
                    objtype = ReadLeafValue(sub);
                    continue;
                case "CUST_OBJNAME":
                    objname = ReadLeafValue(sub);
                    continue;
                case "SUBOBJNAME":
                    subname = ReadLeafValue(sub);
                    actChecked = true; // all 4 fields read
                    // Check full key now that we have all 4 fields
                    if (!imgKeys.Contains(ImgKey(act, objtype, objname, subname))) return;
                    continue;
                case "TDC" when actChecked:
                    ReadTdcSection(sub, files, tdcAliasToFile);
                    continue;
                case "FIELDS" when actChecked:
                    ReadFieldsSection(sub, fieldParams);
                    continue;
                default:
                    sub.Skip();
                    continue;
            }
        }

        // If we never found IMG_ACTIVITY or SUBOBJNAME, nothing to do.
        if (!actChecked || !imgKeys.Contains(ImgKey(act, objtype, objname, subname))) return;

        foreach (var (param, field, central) in fieldParams)
        {
            string srcFile = ResolveCentralFile(central, tdcAliasToFile);
            AddParam(paramMap, param, field, "", central, srcFile);
        }
    }

    private static void ReadTdcSection(
        XmlReader sub, ISet<string> files, Dictionary<string, string> aliasToFile)
    {
        if (sub.IsEmptyElement) { sub.Read(); return; }
        int depth = sub.Depth;
        sub.Read();

        while (!sub.EOF && !(sub.NodeType == XmlNodeType.EndElement && sub.Depth == depth))
        {
            if (sub.NodeType != XmlNodeType.Element || sub.LocalName != "item") { sub.Read(); continue; }
            using var itemSub = sub.ReadSubtree();
            itemSub.Read();
            if (!AdvanceToFirstChild(itemSub)) continue;

            string alias = "", file = "";
            while (!itemSub.EOF && itemSub.NodeType != XmlNodeType.EndElement)
            {
                if (itemSub.NodeType != XmlNodeType.Element) { itemSub.Read(); continue; }
                switch (itemSub.LocalName)
                {
                    case "TDC_ALIAS":    alias = ReadLeafValue(itemSub); break;
                    case "VAR_EXT_FILE": file  = ReadLeafValue(itemSub); break;
                    default:             itemSub.Skip(); break;
                }
            }
            AddFile(files, file);
            if (alias.Length > 0 && file.Length > 0) aliasToFile[alias] = file;
        }
        if (!sub.EOF && sub.NodeType == XmlNodeType.EndElement) sub.Read();
    }

    private static void ReadFieldsSection(XmlReader sub, List<(string, string, string)> fieldParams)
    {
        if (sub.IsEmptyElement) { sub.Read(); return; }
        int depth = sub.Depth;
        sub.Read();

        while (!sub.EOF && !(sub.NodeType == XmlNodeType.EndElement && sub.Depth == depth))
        {
            if (sub.NodeType != XmlNodeType.Element || sub.LocalName != "item") { sub.Read(); continue; }
            using var itemSub = sub.ReadSubtree();
            itemSub.Read();
            if (!AdvanceToFirstChild(itemSub)) continue;

            string param = "", field = "", central = "";
            while (!itemSub.EOF && itemSub.NodeType != XmlNodeType.EndElement)
            {
                if (itemSub.NodeType != XmlNodeType.Element) { itemSub.Read(); continue; }
                switch (itemSub.LocalName)
                {
                    case "PARAM":       param   = ReadLeafValue(itemSub); break;
                    case "FIELDNAME":   field   = ReadLeafValue(itemSub); break;
                    case "CENTRALDATA": central = ReadLeafValue(itemSub); break;
                    default:            itemSub.Skip(); break;
                }
            }
            if (param.Length > 0) fieldParams.Add((param, field, central));
        }
        if (!sub.EOF && sub.NodeType == XmlNodeType.EndElement) sub.Read();
    }

    // ---- XElement-based handlers (small sections) ---------------------------

    private void HandleBcset(
        XElement bc, IReadOnlySet<string> bcsetNames,
        ISet<string> files, Dictionary<string, ConsumedParameter> paramMap)
    {
        string name = Val(bc, "MDF_BCSET");
        if (name.Length == 0 || !bcsetNames.Contains(name)) return;

        AddFile(files, Val(bc, "MDF_EXT_FILE"));
        string firstFile = "";
        foreach (var a in Items(bc, "MDT_TDC_ALIAS"))
        {
            string file = Val(a, "VAR_EXT_FILE");
            AddFile(files, file);
            if (firstFile.Length == 0 && file.Length > 0) firstFile = file;
        }

        foreach (var v in Items(bc, "MDT_VARIANTS"))
        {
            string param = Val(v, "PARAM");
            if (param.Length == 0) continue;
            AddParam(paramMap, param, Val(v, "FIELDNAME"), Val(v, "FIELDTEXT"), Val(v, "CENTRALDATA"), firstFile);
        }
    }

    private void HandleAssociation(XElement a, IReadOnlySet<string> selectedBbIds, ISet<string> files)
    {
        if (!selectedBbIds.Contains(Val(a, "BBID_PRI"))) return;
        AddFile(files, Val(a, "FILENAME_PRI"));
        AddFile(files, Val(a, "FILENAME_SEC"));
    }

    // ---- shared streaming helpers -------------------------------------------

    private static void ForEachItem(XmlReader reader, System.Action<XElement> onItem)
    {
        int depth = reader.Depth;
        if (reader.IsEmptyElement) return;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth) break;
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "item" && reader.Depth == depth + 1)
            {
                var el = XElement.Load(reader.ReadSubtree());
                onItem(el);
            }
        }
    }

    /// <summary>
    /// Reads a simple leaf element's text value and advances the reader to the next sibling.
    /// Safe to call in a "while not EOF, while not EndElement" loop without an extra Read().
    /// </summary>
    private static string ReadLeafValue(XmlReader r)
    {
        if (r.IsEmptyElement) { r.Read(); return ""; }
        string value = r.ReadElementContentAsString().Trim();
        // ReadElementContentAsString advances the reader past the end tag to the next sibling.
        return value;
    }

    /// <summary>Advance the reader into the first child of the current element.</summary>
    private static bool AdvanceToFirstChild(XmlReader r)
    {
        if (r.IsEmptyElement) return false;
        r.Read();
        return !r.EOF && r.NodeType != XmlNodeType.EndElement;
    }

    private static IEnumerable<XElement> Items(XElement parent, string listName) =>
        parent.Element(listName)?.Elements("item") ?? Enumerable.Empty<XElement>();

    private static string Val(XElement e, string name) => e.Element(name)?.Value.Trim() ?? "";

    private static string ImgKey(string act, string objType, string objName, string subName) =>
        string.Join(KeySep, act.Trim(), objType.Trim(), objName.Trim(), subName.Trim()).ToUpperInvariant();

    private static void AddFile(ISet<string> files, string name)
    {
        if (!string.IsNullOrWhiteSpace(name)) files.Add(name.Trim());
    }

    private static string ResolveCentralFile(string central, Dictionary<string, string> aliasToFile)
    {
        if (string.IsNullOrEmpty(central)) return "";
        string alias = central.Split(';')[0].Trim();
        return aliasToFile.TryGetValue(alias, out var file) ? file : "";
    }

    private static void AddParam(
        Dictionary<string, ConsumedParameter> map,
        string param, string field, string fieldText, string central, string srcFile)
    {
        if (!map.TryGetValue(param, out var p))
        {
            p = new ConsumedParameter { Param = param, FieldName = field, FieldText = fieldText, CentralDataAlias = central };
            map[param] = p;
        }
        if (!string.IsNullOrEmpty(srcFile)) p.SourceFiles.Add(srcFile);
    }

    private static void SkipElement(XmlReader reader)
    {
        if (reader.IsEmptyElement) return;
        int depth = reader.Depth;
        while (reader.Read())
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                break;
    }
}
