using System.Collections.Concurrent;
using SapVariantReplacer.Models;

namespace SapVariantReplacer.Services;

/// <summary>
/// Extracts unique (FieldName, Value) pairs across all parsed variant files.
/// </summary>
public static class FieldExtractor
{
    /// <summary>
    /// Scans all data rows across all files and returns a deduplicated list of
    /// (FieldName, Value) entries. The dedup key is (FieldName, Value) — so
    /// I_LGNUM="100" and I_LGNUM="200" are two separate entries.
    /// </summary>
    public static List<FieldValueEntry> Extract(IReadOnlyList<VariantFile> files)
    {
        var dict = new ConcurrentDictionary<(string Field, string Value), int>();

        Parallel.ForEach(files, file =>
        {
            foreach (var line in file.Lines)
            {
                if (line.Kind != LineKind.Data)
                    continue;

                for (int col = 2; col < line.Cells.Length && col < file.Headers.Length; col++)
                {
                    var value = line.Cells[col];
                    if (string.IsNullOrEmpty(value))
                        continue;

                    var fieldName = file.Headers[col];
                    dict.AddOrUpdate((fieldName, value), 1, (_, count) => count + 1);
                }
            }
        });

        return dict
            .OrderBy(kvp => kvp.Key.Field, StringComparer.OrdinalIgnoreCase)
            .ThenBy(kvp => kvp.Key.Value, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => new FieldValueEntry
            {
                FieldName = kvp.Key.Field,
                OriginalValue = kvp.Key.Value,
                NewValue = kvp.Key.Value,
                OccurrenceCount = kvp.Value
            })
            .ToList();
    }
}
