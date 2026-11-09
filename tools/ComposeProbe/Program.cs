using System.IO;
using System.Text;
using SapVariantReplacer.Models;
using SapVariantReplacer.Services;
using SolutionReplicator.Models;
using SolutionReplicator.Services;

// ComposeProbe — authoritative headless proof of the placeholder compose slice on the REAL
// client-repo sample files, before any WPF exists.
//
// Usage: dotnet run                 (defaults below)
//        dotnet run -- <repo> <xml>

string repo = args.Length > 0 ? args[0] : @".\sample-repo";
string xml = args.Length > 1 ? args[1] : @".\sample-solution.xml";

int failures = 0;
void Check(string label, bool ok)
{
    Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {label}");
    if (!ok) failures++;
}

Console.WriteLine($"Repo: {repo}  Exists: {Directory.Exists(repo)}");
Console.WriteLine($"XML : {xml}  Exists: {File.Exists(xml)}");
if (!Directory.Exists(repo)) return 1;

// ── A. Token discovery ───────────────────────────────────────────────────────
Console.WriteLine("\n[A] Token discovery");
var discovered = TokenScanner.Scan(repo);
foreach (var d in discovered)
    Console.WriteLine($"      found {d.Token} under {d.FieldName} (x{d.OccurrenceCount})");
Check("discovers (I_WERKS, &PLN)",
    discovered.Any(d => d.FieldName == "I_WERKS" && d.Token == "&PLN"));
Check("&PLN occurs twice across repo",
    discovered.Where(d => d.Token == "&PLN").Sum(d => d.OccurrenceCount) == 2);

// ── C. Table seed + round-trip (CSV) ─────────────────────────────────────────
Console.WriteLine("\n[C] Nomenclature CSV seed + round-trip");
var table = new PlaceholderTable();
int added = CatalogComposer.SeedMissingTokens(table, discovered, "HU-PROD");
foreach (var r in table.Rows.Where(r => r.Variable == "&PLN" && r.SysId == "HU-PROD"))
    r.Value = "HU01";
Check("seeded at least one variable row", added >= 1);

// re-seed must not add duplicates or clobber values
int addedAgain = CatalogComposer.SeedMissingTokens(table, discovered, "HU-PROD");
Check("re-seed adds nothing (no clobber)", addedAgain == 0);
Check("HU-PROD value for &PLN survives re-seed",
    table.Rows.First(r => r.Variable == "&PLN" && r.SysId == "HU-PROD").Value == "HU01");

var tmpCat = Path.Combine(Path.GetTempPath(), $"probe-table-{Guid.NewGuid():N}.csv");
PlaceholderTableStore.Save(tmpCat, table);
var reloaded = PlaceholderTableStore.Load(tmpCat);
Check("round-trip preserves systems", reloaded.Systems.SequenceEqual(table.Systems));
Check("round-trip preserves &PLN=HU01 for HU-PROD",
    reloaded.Rows.First(r => r.Variable == "&PLN" && r.SysId == "HU-PROD").Value == "HU01");

// adding a second system preserves the first
CatalogComposer.SeedMissingTokens(table, discovered, "CZ-PROD");
table.Rows.First(r => r.Variable == "&PLN" && r.SysId == "CZ-PROD").Value = "CZ01";
Check("adding system preserves HU-PROD",
    table.Rows.First(r => r.Variable == "&PLN" && r.SysId == "HU-PROD").Value == "HU01");
Check("table now has two systems", table.Systems.Count == 2);

// ── Substitution (compose core) ──────────────────────────────────────────────
Console.WriteLine("\n[Substitution] &PLN -> HU01 via ReplacementEngine (unchanged)");
var changes = CatalogComposer.BuildChangeSet(table, "HU-PROD");
Console.WriteLine($"      change-set rows: {changes.Count}");
var (files, parseErrors) = VariantFileParser.ParseFolder(repo);
foreach (var e in parseErrors) Console.WriteLine($"      parse error: {e}");

var outDir = Path.Combine(Path.GetTempPath(), $"probe-out-{Guid.NewGuid():N}");
int modified = ReplacementEngine.Apply(files, changes, outDir, copyUnmodified: true);
Console.WriteLine($"      files modified: {modified}, output: {outDir}");
Check("at least one file modified", modified >= 1);

// Inspect outputs: &PLN became HU01 under I_WERKS only; I_STAND untouched; BOM + CRLF preserved.
foreach (var outFile in Directory.GetFiles(outDir, "*.TXT"))
{
    var name = Path.GetFileName(outFile);
    var bytes = File.ReadAllBytes(outFile);
    bool bom = bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE;
    Check($"{name}: UTF-16LE BOM present", bom);

    var text = Encoding.Unicode.GetString(bytes, bom ? 2 : 0, bytes.Length - (bom ? 2 : 0));
    Check($"{name}: contains CRLF", text.Contains("\r\n"));
    Check($"{name}: no &PLN remaining", !text.Contains("&PLN"));
    Check($"{name}: contains HU01", text.Contains("HU01"));
    // V_T499S must keep its literal I_STAND value HUS1 (proves column scope)
    if (name.StartsWith("V_T499S", StringComparison.OrdinalIgnoreCase))
        Check($"{name}: I_STAND literal 'HUS1' untouched", text.Contains("HUS1"));
}

// ── B. Matcher against the template's consumed set ───────────────────────────
if (File.Exists(xml))
{
    Console.WriteLine("\n[B] Exact-name matcher against template consumed set");
    // Resolve the whole template (all BBs) just to prove the repo files appear in ConsumedFiles.
    // In the app this is scoped to the ticked selection.
    var resolver = new ConsumptionResolver();
    var allBbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    // Use ScopeItemMapper to gather every BB id in the template.
    var mapper = new ScopeItemMapper();
    foreach (var si in mapper.Map(xml))
        foreach (var bb in si.BuildingBlockIds)
            allBbs.Add(bb);
    Console.WriteLine($"      template BBs: {allBbs.Count}");
    var consumed = resolver.Resolve(xml, allBbs);
    Console.WriteLine($"      consumed files: {consumed.ConsumedFiles.Count}");

    var repoNames = Directory.GetFiles(repo, "*.TXT").Select(Path.GetFileName).ToList();
    foreach (var rn in repoNames)
        Check($"template consumes repo file '{rn}'", consumed.ConsumedFiles.Contains(rn!));

    var match = RepoFileMatcher.Match(consumed.ConsumedFiles, repo);
    Console.WriteLine($"      matched={match.MatchedCount}, unmatched(consumed w/o repo file)={match.UnmatchedCount}");
    Check("both repo files matched", match.MatchedCount == repoNames.Count);
}
else
{
    Console.WriteLine("\n[B] skipped (template XML not found)");
}

// cleanup
try { File.Delete(tmpCat); Directory.Delete(outDir, true); } catch { }

Console.WriteLine($"\n{(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED")}");
return failures == 0 ? 0 : 2;
