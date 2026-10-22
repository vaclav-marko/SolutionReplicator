using System.IO;
using SapVariantReplacer.Services;
using SolutionBuilder.Services;
using SolutionReplicator.Models;
using SolutionReplicator.Services;

string xml = args.Length > 0 ? args[0] : @"C:\_testing\BP_OP_ENTPR_S4HANA2025_HUV12.xml";

Console.WriteLine($"XML: {xml}  Exists: {File.Exists(xml)}");
if (!File.Exists(xml)) return 1;

var parser = new XmlParserService();
var sol = await parser.ParseSolutionMetadataAsync(xml);
Console.WriteLine($"Solution: {sol.SolutionId} | Country: {sol.SourceCountry} | SIs: {sol.ScopeItems.Count} | BBs: {sol.BuildingBlocks.Count}");

var resolver = new ConsumptionResolver();

// --- Test 1: targeted known BBs (worked examples) ---
var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "1IM (XX)", "2AI (HU)" };
var r1 = resolver.Resolve(xml, known);
Console.WriteLine($"\n[Test 1] BBs {{1IM (XX), 2AI (HU)}}: files={r1.ConsumedFiles.Count}, params={r1.ConsumedParams.Count}");
Console.WriteLine($"  V_T582A_V_T582A_1IM.TXT present : {r1.ConsumedFiles.Contains("V_T582A_V_T582A_1IM.TXT")}");
Console.WriteLine($"  SMBERP_SCWM_DLVPPFC0DMW_O1_2AI.TXT present : {r1.ConsumedFiles.Contains("SMBERP_SCWM_DLVPPFC0DMW_O1_2AI.TXT")}");
Console.WriteLine("  sample params: " + string.Join(", ", r1.ConsumedParams.Take(20).Select(p => p.Param)));

// --- Test 2: SI -> BB mapping via ScopeItemMapper ---
var mapper = new ScopeItemMapper();
var scopeItems = mapper.Map(xml);
int withBbs = scopeItems.Count(s => s.BuildingBlockIds.Count > 0);
Console.WriteLine($"\n[Test 2] ScopeItemMapper: {scopeItems.Count} scope items, {withBbs} with >=1 BB");
var si287 = scopeItems.FirstOrDefault(s => s.ProjectId == "XX_287_OP");
Console.WriteLine($"  XX_287_OP: desc='{si287?.Description}', BBs={si287?.BuildingBlockIds.Count}, " +
                  $"[{string.Join(", ", si287?.BuildingBlockIds ?? new())}]");

// --- Test 3: realistic subset from a sales-ish SI; confirm small set + common params ---
var richSi = scopeItems.Where(s => s.BuildingBlockIds.Count > 0)
                       .OrderByDescending(s => s.BuildingBlockIds.Count).First();
var bbs = new HashSet<string>(richSi.BuildingBlockIds, StringComparer.OrdinalIgnoreCase);
var r3 = resolver.Resolve(xml, bbs);
Console.WriteLine($"\n[Test 3] SI {richSi.ProjectId} ({bbs.Count} BBs): files={r3.ConsumedFiles.Count} (expect « 14000), params={r3.ConsumedParams.Count}");
foreach (var w in new[] { "I_WERKS", "I_BUKRS", "I_PSTYV", "I_KTOPL", "I_LAND1" })
    Console.WriteLine($"    param {w}: {r3.ConsumedParams.Any(p => p.Param == w)}");

// --- Test 4: a param's source file + central-data alias wiring ---
var sample = r3.ConsumedParams.FirstOrDefault(p => p.SourceFiles.Count > 0);
if (sample is not null)
    Console.WriteLine($"\n[Test 4] e.g. {sample.Param} (field {sample.FieldName}, central '{sample.CentralDataAlias}') " +
                      $"from file(s): {string.Join(", ", sample.SourceFiles.Take(3))}");

// --- Test 5: full consumed-file parse/extract pipeline against the matched DEV12 pair ---
string devXml = @"C:\SAPDevelop\SolutionXMLParser\BP_OP_ENTPR_S4HANA2025_DEV12.xml";
string devFolder = @"C:\SAPDevelop\SolutionXMLParser\BP_OP_ENTPR_S4HANA2025_DEV12";
if (File.Exists(devXml) && Directory.Exists(devFolder))
{
    var devSis = mapper.Map(devXml);
    // pick a mid-size SI so parsing is quick but non-trivial
    var pick = devSis.Where(s => s.BuildingBlockIds.Count is > 0 and <= 10)
                     .OrderByDescending(s => s.BuildingBlockIds.Count)
                     .FirstOrDefault() ?? devSis.First(s => s.BuildingBlockIds.Count > 0);
    var devBbs = new HashSet<string>(pick.BuildingBlockIds, StringComparer.OrdinalIgnoreCase);
    var cons = resolver.Resolve(devXml, devBbs);

    var onDisk = Directory.EnumerateFiles(devFolder, "*.TXT", SearchOption.TopDirectoryOnly)
        .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key!, g => g.First(), StringComparer.OrdinalIgnoreCase);

    var vfiles = new List<SapVariantReplacer.Models.VariantFile>();
    int present = 0, missing = 0;
    foreach (var name in cons.ConsumedFiles)
    {
        if (onDisk.TryGetValue(name, out var p))
        {
            var vf = VariantFileParser.ParseFile(p);
            if (vf is not null) { vfiles.Add(vf); present++; } else missing++;
        }
        else missing++;
    }
    var entries = FieldExtractor.Extract(vfiles);
    var groups = entries.GroupBy(e => e.FieldName).OrderByDescending(g => g.Count()).ToList();

    Console.WriteLine($"\n[Test 5] DEV12 pipeline: SI {pick.ProjectId} ({devBbs.Count} BBs)");
    Console.WriteLine($"  consumed={cons.ConsumedFiles.Count}, present on disk={present}, missing={missing}");
    Console.WriteLine($"  parsed variant files={vfiles.Count}, unique (param,value) rows={entries.Count}, distinct params(groups)={groups.Count}");
    Console.WriteLine("  top groups: " + string.Join(", ", groups.Take(6).Select(g => $"{g.Key}×{g.Count()}")));

    // --- Test 6: full Process pipeline (PreparePackage + apply edits), country unchanged ---
    var editEntry = entries.FirstOrDefault(e => e.FieldName == "I_BUKRS") ?? entries.FirstOrDefault();
    if (editEntry is not null)
    {
        const string marker = "ZZZZ9";
        string origVal = editEntry.OriginalValue;
        editEntry.NewValue = marker; // simulate a user edit

        string outDir = Path.Combine(Path.GetTempPath(), "SolReplProbe_" + Guid.NewGuid().ToString("N")[..8]);
        var proc = new SolutionProcessorService();
        var prepared = await proc.PreparePackageAsync(
            devXml, outDir, "Z_REPL_TEST", pick.Country, pick.Country,
            new HashSet<string> { pick.ProjectId }, devBbs, renameSolution: true);

        int modified = ReplacementEngine.Apply(vfiles, entries, prepared.OutputDataFolder, copyUnmodified: true);

        int outCount = Directory.Exists(prepared.OutputDataFolder)
            ? Directory.GetFiles(prepared.OutputDataFolder, "*.TXT").Length : 0;
        bool xmlOk = File.Exists(prepared.OutputXmlPath);

        // Find the marker in an output file and confirm UTF-16LE BOM.
        int filesWithMarker = 0; bool bomOk = false;
        foreach (var fp in Directory.GetFiles(prepared.OutputDataFolder, "*.TXT"))
        {
            var bytes = File.ReadAllBytes(fp);
            if (!bomOk && bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) bomOk = true;
            var text = System.Text.Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (text.Contains('\t' + marker + '\t') || text.Contains('\t' + marker + "\r") || text.EndsWith('\t' + marker))
                filesWithMarker++;
        }

        Console.WriteLine($"\n[Test 6] Process pipeline -> {outDir}");
        Console.WriteLine($"  XML written={xmlOk}, data files written={outCount}, engine modifiedCount={modified}");
        Console.WriteLine($"  edited param {editEntry.FieldName}: '{origVal}' -> '{marker}'; files containing marker={filesWithMarker}; UTF-16LE BOM ok={bomOk}");
        try { Directory.Delete(outDir, recursive: true); } catch { /* leave for inspection on failure */ }
    }
}
else
{
    Console.WriteLine($"\n[Test 5] skipped — DEV12 pair not found ({devXml})");
}

// --- Test 7: inline XML value extraction (the "XML contains data" source) ---
var inline = new InlineXmlValueExtractor();
var allInline = inline.Extract(xml, new HashSet<string>());
Console.WriteLine($"\n[Test 7] inline XML values (BC-Set) in {Path.GetFileName(xml)}: {allInline.Count} (param,value) rows (unscoped)");
Console.WriteLine("  sample: " + string.Join(", ", allInline.Take(8).Select(e => $"{e.FieldName}={e.OriginalValue}")));

// scoped to a selection's consumed params
var si7 = scopeItems.Where(s => s.BuildingBlockIds.Count > 0).OrderByDescending(s => s.BuildingBlockIds.Count).First();
var cons7 = resolver.Resolve(xml, new HashSet<string>(si7.BuildingBlockIds, StringComparer.OrdinalIgnoreCase));
var scopedParams = new HashSet<string>(cons7.ConsumedParams.Select(p => p.Param), StringComparer.OrdinalIgnoreCase);
var scopedInline = inline.Extract(xml, scopedParams);
Console.WriteLine($"  scoped to SI {si7.ProjectId} ({scopedParams.Count} consumed params): {scopedInline.Count} inline rows shown in the edit grid");

// --- Test 8: regenerate a single-SI HU package with the structural fixes ---
{
    var huSi = scopeItems.Where(s => s.BuildingBlockIds.Count is > 0 and <= 5).FirstOrDefault()
               ?? scopeItems.First(s => s.BuildingBlockIds.Count > 0);
    var huBbs = new HashSet<string>(huSi.BuildingBlockIds, StringComparer.OrdinalIgnoreCase);
    var proc = new SolutionProcessorService();
    var prep = await proc.PreparePackageAsync(
        xml, @"C:\_testing", "BP_OP_ENTPR_S4HANA2025_VMA2", "HU", "HU",
        new HashSet<string> { huSi.ProjectId }, huBbs, renameSolution: true);

    var ob = File.ReadAllBytes(prep.OutputXmlPath);
    bool hasBom = ob.Length >= 3 && ob[0] == 0xEF && ob[1] == 0xBB && ob[2] == 0xBF;
    var head = System.Text.Encoding.UTF8.GetString(ob, 0, Math.Min(200, ob.Length));
    string pkg = System.Text.RegularExpressions.Regex.Match(head.Length > 0 ? System.Text.Encoding.UTF8.GetString(ob) : "", @"<PKG_ID>([^<]*)</PKG_ID>").Groups[1].Value;
    int prjids = System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.UTF8.GetString(ob), "<PRJID>").Count;
    int newlines = ob.Count(b => b == (byte)'\n');

    Console.WriteLine($"\n[Test 8] regenerated {prep.OutputXmlPath}");
    Console.WriteLine($"  SI {huSi.ProjectId} ({huBbs.Count} BBs); size={ob.Length:N0} bytes");
    Console.WriteLine($"  BOM present={hasBom} (want False); PKG_ID='{pkg}' (want BP_OP_ENTPR); <PRJID> count={prjids} (want small); newlines={newlines} (want ~few)");
    Console.WriteLine($"  head: {head.Replace("\n", "\\n").Substring(0, Math.Min(90, head.Length))}");
}

Console.WriteLine("\nDONE");
return 0;
