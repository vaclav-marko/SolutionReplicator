using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SapVariantReplacer.Services;

namespace SolutionReplicator.Services;

/// <summary>One distinct placeholder discovered in the repository.</summary>
public sealed record DiscoveredToken(string FieldName, string Token, int OccurrenceCount);

/// <summary>
/// Scans a client repository folder for placeholder tokens inside the TXT variant files.
/// A token is a whole data cell matching <see cref="TokenPattern"/> (e.g. <c>&amp;PLN</c>).
///
/// Reuses the existing variant-file pipeline wholesale: <see cref="VariantFileParser.ParseFolder"/>
/// to parse and <see cref="FieldExtractor.Extract"/> to produce distinct (FieldName, Value) pairs,
/// then filters those pairs down to the ones whose value is a token. No new parsing logic.
/// </summary>
public static class TokenScanner
{
    /// <summary>A token is an ampersand followed by upper-case letters, digits, or underscores.</summary>
    public static readonly Regex TokenPattern = new(@"^&[A-Z0-9_]+$", RegexOptions.Compiled);

    public static IReadOnlyList<DiscoveredToken> Scan(string repoFolder)
    {
        var (files, _) = VariantFileParser.ParseFolder(repoFolder);
        var entries = FieldExtractor.Extract(files);

        return entries
            .Where(e => TokenPattern.IsMatch(e.OriginalValue))
            .Select(e => new DiscoveredToken(e.FieldName, e.OriginalValue, e.OccurrenceCount))
            .ToList();
    }
}
