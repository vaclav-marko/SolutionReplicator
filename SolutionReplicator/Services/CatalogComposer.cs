using System;
using System.Collections.Generic;
using System.Linq;
using SapVariantReplacer.Models;
using SolutionReplicator.Models;

namespace SolutionReplicator.Services;

/// <summary>
/// Pure logic tying the placeholder table (CSV) to the discovered tokens and the compose
/// change-set. Kept free of UI and I/O so the headless probe and the wizard view-models share
/// one code path.
/// </summary>
public static class CatalogComposer
{
    /// <summary>
    /// Ensures every discovered token exists as a row for the given system, so the user has a
    /// line to fill in. Adds a row (empty value) for any (variable, field) that has no row for
    /// <paramref name="sysId"/>; existing rows (and their values) are never clobbered. Tokens no
    /// longer present in the repo are kept. Returns the number of rows added.
    /// </summary>
    public static int SeedMissingTokens(PlaceholderTable table, IEnumerable<DiscoveredToken> discovered, string sysId)
    {
        if (string.IsNullOrWhiteSpace(sysId))
            return 0;

        var existing = new HashSet<(string, string, string)>(
            table.Rows.Select(r => (r.Variable, r.Field, r.SysId)),
            TupleComparer);

        int added = 0;
        foreach (var d in discovered)
        {
            if (existing.Add((d.Token, d.FieldName, sysId)))
            {
                table.Rows.Add(new PlaceholderMapping
                {
                    Variable = d.Token,
                    Field = d.FieldName,
                    Value = string.Empty,
                    SysId = sysId
                });
                added++;
            }
        }
        return added;
    }

    /// <summary>
    /// Builds the <see cref="FieldValueEntry"/> change-set for a chosen system: one entry per row
    /// whose SysId matches and whose value is non-empty. A variable with no value for the system is
    /// omitted, so its cell keeps the placeholder — a visible "unmapped" signal, not a blank.
    /// The returned entries feed <c>ReplacementEngine.Apply</c> unchanged.
    /// </summary>
    public static List<FieldValueEntry> BuildChangeSet(PlaceholderTable table, string sysId)
    {
        return table.Rows
            .Where(r => r.SysId.Equals(sysId, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrEmpty(r.Value))
            .Select(r => new FieldValueEntry
            {
                FieldName = r.Field,
                OriginalValue = r.Variable,
                NewValue = r.Value,
                OccurrenceCount = 0
            })
            .ToList();
    }

    private static readonly IEqualityComparer<(string, string, string)> TupleComparer =
        new TripleOrdinalIgnoreCase();

    private sealed class TripleOrdinalIgnoreCase : IEqualityComparer<(string, string, string)>
    {
        public bool Equals((string, string, string) x, (string, string, string) y) =>
            string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Item3, y.Item3, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string, string, string) t) => HashCode.Combine(
            t.Item1.ToLowerInvariant(), t.Item2.ToLowerInvariant(), t.Item3.ToLowerInvariant());
    }
}
