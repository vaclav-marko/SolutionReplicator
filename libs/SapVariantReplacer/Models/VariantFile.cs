namespace SapVariantReplacer.Models;

/// <summary>
/// Represents a parsed SAP variant file.
/// </summary>
public sealed class VariantFile
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }

    /// <summary>Column headers from the first line (e.g. "[VARIANT]", "[DESCRIPTION]", "I_LGNUM", …).</summary>
    public required string[] Headers { get; init; }

    /// <summary>All raw lines of the file (preserving order and content).</summary>
    public required List<VariantLine> Lines { get; init; }
}

/// <summary>
/// One line of the variant file, keeping raw cells and a classification.
/// </summary>
public sealed class VariantLine
{
    public required LineKind Kind { get; init; }

    /// <summary>Tab-split cells. Index matches the header index.</summary>
    public required string[] Cells { get; init; }

    /// <summary>The original raw line text (used for fallback / non-data lines).</summary>
    public required string RawLine { get; init; }
}

public enum LineKind
{
    Header,
    Description,
    Referencing,
    Data
}

/// <summary>
/// A unique (FieldName + OriginalValue) pair shown in the editing grid.
/// </summary>
public sealed class FieldValueEntry : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public required string FieldName { get; init; }
    public required string OriginalValue { get; init; }

    private string _newValue = string.Empty;
    public string NewValue
    {
        get => _newValue;
        set
        {
            if (SetProperty(ref _newValue, value))
            {
                OnPropertyChanged(nameof(IsChanged));
            }
        }
    }

    /// <summary>Number of cells across all files that match this (FieldName, OriginalValue).</summary>
    public required int OccurrenceCount { get; init; }

    public bool IsChanged => NewValue != OriginalValue;
}
