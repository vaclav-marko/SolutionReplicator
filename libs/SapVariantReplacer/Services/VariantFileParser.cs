using System.Collections.Concurrent;
using System.IO;
using System.Text;
using SapVariantReplacer.Models;

namespace SapVariantReplacer.Services;

/// <summary>
/// Parses SAP variant text files (tab-delimited, UTF-16 LE with BOM).
/// </summary>
public static class VariantFileParser
{
    public static (List<VariantFile> Files, List<string> Errors) ParseFolder(
        string folderPath,
        IProgress<(int Current, int Total, string FileName)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var filePaths = Directory.GetFiles(folderPath, "*.TXT", SearchOption.TopDirectoryOnly);
        var results = new ConcurrentBag<VariantFile>();
        var errors = new ConcurrentBag<string>();

        // Phase 1: Read all files into memory with bounded parallelism.
        // Full parallelism overwhelms real-time AV scanners (Defender), but fully
        // sequential under-utilises the disk. 4 concurrent reads is the sweet spot.
        var fileData = new ConcurrentBag<(string Path, byte[] Bytes)>();
        int readCount = 0;

        Parallel.ForEach(filePaths, new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = 4
        },
        fp =>
        {
            try
            {
                var bytes = File.ReadAllBytes(fp);
                fileData.Add((fp, bytes));
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(fp)}: {ex.Message}");
            }

            var current = Interlocked.Increment(ref readCount);
            progress?.Report((current, filePaths.Length, Path.GetFileName(fp)));
        });

        // Phase 2: Parse file content in parallel (pure CPU — no file I/O).
        Parallel.ForEach(fileData, new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Environment.ProcessorCount
        },
        item =>
        {
            try
            {
                var raw = DecodeFileBytes(item.Bytes);
                var vf = ParseRawText(item.Path, raw);
                if (vf is not null)
                    results.Add(vf);
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(item.Path)}: {ex.Message}");
            }
        });

        return ([.. results], [.. errors]);
    }

    public static VariantFile? ParseFile(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        var raw = DecodeFileBytes(bytes);
        return ParseRawText(filePath, raw);
    }

    private static VariantFile? ParseRawText(string filePath, string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var rawLines = raw.Split(["\r\n", "\n"], StringSplitOptions.None);

        if (rawLines.Length == 0 || !rawLines[0].Contains("[VARIANT]"))
            return null;

        var headers = rawLines[0].Split('\t');
        var lines = new List<VariantLine>(rawLines.Length);

        for (int i = 0; i < rawLines.Length; i++)
        {
            var line = rawLines[i];
            if (string.IsNullOrEmpty(line) && i == rawLines.Length - 1)
                continue;

            var cells = line.Split('\t');
            LineKind kind;

            if (i == 0)
                kind = LineKind.Header;
            else if (line.StartsWith("*REFERENCING", StringComparison.Ordinal))
                kind = LineKind.Referencing;
            else if (line.StartsWith('*'))
                kind = LineKind.Description;
            else
                kind = LineKind.Data;

            lines.Add(new VariantLine
            {
                Kind = kind,
                Cells = cells,
                RawLine = line
            });
        }

        return new VariantFile
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            Headers = headers,
            Lines = lines
        };
    }

    private static string DecodeFileBytes(byte[] bytes)
    {
        if (bytes.Length < 2)
            return string.Empty;

        if (bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);

        int offset = (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) ? 3 : 0;
        return Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
    }
}
