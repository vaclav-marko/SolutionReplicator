using System.Collections.Concurrent;
using System.IO;
using System.Text;
using SapVariantReplacer.Models;

namespace SapVariantReplacer.Services;

/// <summary>
/// Applies field-value replacements to parsed files and writes them to an output folder.
/// </summary>
public static class ReplacementEngine
{
    private static readonly Encoding Utf16Le = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);

    /// <summary>
    /// Returns a preview summary: how many files will change and total cell replacements.
    /// </summary>
    public static (int FilesAffected, int TotalReplacements) Preview(
        IReadOnlyList<VariantFile> files,
        IReadOnlyList<FieldValueEntry> changes)
    {
        var changeMap = BuildChangeMap(changes);
        if (changeMap.Count == 0)
            return (0, 0);

        int filesAffected = 0;
        int totalReplacements = 0;

        foreach (var file in files)
        {
            int fileReplacements = CountReplacements(file, changeMap);
            if (fileReplacements > 0)
            {
                filesAffected++;
                totalReplacements += fileReplacements;
            }
        }

        return (filesAffected, totalReplacements);
    }

    /// <summary>
    /// Applies all changes and writes files to outputFolder.
    /// Returns the number of files modified.
    /// </summary>
    public static int Apply(
        IReadOnlyList<VariantFile> files,
        IReadOnlyList<FieldValueEntry> changes,
        string outputFolder,
        bool copyUnmodified,
        IProgress<(int Current, int Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputFolder);

        var changeMap = BuildChangeMap(changes);
        int modifiedCount = 0;
        int total = files.Count;

        // Phase 1: Build all modified content in parallel (pure CPU).
        var outputs = new ConcurrentBag<(string FileName, string? Content, string? SourcePath)>();

        Parallel.ForEach(files, new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Environment.ProcessorCount
        },
        file =>
        {
            if (changeMap.Count == 0 || CountReplacements(file, changeMap) == 0)
            {
                if (copyUnmodified)
                    outputs.Add((file.FileName, null, file.FilePath));
            }
            else
            {
                var content = BuildModifiedContent(file, changeMap);
                outputs.Add((file.FileName, content, null));
                Interlocked.Increment(ref modifiedCount);
            }
        });

        // Phase 2: Write files with bounded parallelism.
        // Full parallelism overwhelms real-time AV scanners; 4 concurrent writes
        // keeps the disk busy without triggering an AV storm.
        int written = 0;

        Parallel.ForEach(outputs, new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = 4
        },
        output =>
        {
            var outPath = Path.Combine(outputFolder, output.FileName);
            if (output.Content is not null)
                File.WriteAllText(outPath, output.Content, Utf16Le);
            else if (output.SourcePath is not null)
                File.Copy(output.SourcePath, outPath, overwrite: true);

            var current = Interlocked.Increment(ref written);
            progress?.Report((current, total));
        });

        return modifiedCount;
    }

    private static string BuildModifiedContent(
        VariantFile file,
        Dictionary<(string Field, string Value), string> changeMap)
    {
        var sb = new StringBuilder();
        bool first = true;

        foreach (var line in file.Lines)
        {
            if (!first) sb.Append("\r\n");
            first = false;

            if (line.Kind != LineKind.Data)
            {
                sb.Append(line.RawLine);
                continue;
            }

            for (int col = 0; col < line.Cells.Length; col++)
            {
                if (col > 0) sb.Append('\t');

                if (col >= 2 && col < file.Headers.Length)
                {
                    var fieldName = file.Headers[col];
                    var cellValue = line.Cells[col];

                    if (!string.IsNullOrEmpty(cellValue)
                        && changeMap.TryGetValue((fieldName, cellValue), out var newValue))
                    {
                        sb.Append(newValue);
                    }
                    else
                    {
                        sb.Append(line.Cells[col]);
                    }
                }
                else
                {
                    sb.Append(line.Cells[col]);
                }
            }
        }

        sb.Append("\r\n");
        return sb.ToString();
    }

    /// <summary>
    /// Builds a lookup: (FieldName, OriginalValue) → NewValue for entries that actually changed.
    /// </summary>
    private static Dictionary<(string Field, string Value), string> BuildChangeMap(
        IReadOnlyList<FieldValueEntry> changes)
    {
        var map = new Dictionary<(string, string), string>();
        foreach (var entry in changes)
        {
            if (entry.IsChanged)
                map[(entry.FieldName, entry.OriginalValue)] = entry.NewValue;
        }
        return map;
    }

    private static int CountReplacements(
        VariantFile file,
        Dictionary<(string Field, string Value), string> changeMap)
    {
        int count = 0;
        foreach (var line in file.Lines)
        {
            if (line.Kind != LineKind.Data)
                continue;

            for (int col = 2; col < line.Cells.Length && col < file.Headers.Length; col++)
            {
                var cellValue = line.Cells[col];
                if (!string.IsNullOrEmpty(cellValue)
                    && changeMap.ContainsKey((file.Headers[col], cellValue)))
                {
                    count++;
                }
            }
        }
        return count;
    }
}
