using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SolutionReplicator.Models;

namespace SolutionReplicator.Services;

/// <summary>
/// Loads and saves the placeholder nomenclature table as a semicolon-delimited CSV:
/// <c>variable;field;value;sysid</c>. A header row (<c>variable;field;value;sysid</c>) is written
/// on save and tolerated (skipped) on load. This is the client's central table — one file can
/// carry many target systems (SysId values) and serve many content repositories.
/// </summary>
public static class PlaceholderTableStore
{
    private const char Sep = ';';
    private const string Header = "variable;field;value;sysid";

    /// <summary>
    /// Loads the table from <paramref name="path"/>. Returns an empty table if the file is absent.
    /// Blank lines and a leading header row are skipped; rows with fewer than 4 columns are ignored.
    /// </summary>
    public static PlaceholderTable Load(string path)
    {
        var table = new PlaceholderTable();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return table;

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            var cols = line.Split(Sep);
            if (cols.Length < 4) continue;

            var variable = cols[0].Trim();
            // Skip a header row (any casing of the literal column names).
            if (variable.Equals("variable", StringComparison.OrdinalIgnoreCase)) continue;
            if (variable.Length == 0) continue;

            table.Rows.Add(new PlaceholderMapping
            {
                Variable = variable,
                Field = cols[1].Trim(),
                Value = cols[2].Trim(),
                SysId = cols[3].Trim()
            });
        }
        return table;
    }

    /// <summary>
    /// Saves the table as CSV (header + one row per mapping), temp-file-then-atomic-replace so a
    /// crash mid-write cannot corrupt the client's existing table.
    /// </summary>
    public static void Save(string path, PlaceholderTable table)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Table path is empty.", nameof(path));

        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.AppendLine(Header);
        foreach (var r in table.Rows)
            sb.AppendLine($"{r.Variable}{Sep}{r.Field}{Sep}{r.Value}{Sep}{r.SysId}");

        var tmp = path + ".tmp";
        File.WriteAllText(tmp, sb.ToString());

        if (File.Exists(path))
            File.Replace(tmp, path, destinationBackupFileName: null);
        else
            File.Move(tmp, path);
    }
}
