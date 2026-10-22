using System.Collections.Generic;

namespace SolutionReplicator.Models;

/// <summary>
/// The set of installation-data files and import parameters that a selection of
/// scope items / building blocks actually consumes, computed from the solution XML
/// by <see cref="Services.ConsumptionResolver"/>.
/// </summary>
public sealed class ConsumptionResult
{
    /// <summary>Original (pre-rename) TXT file names the selection pulls in.</summary>
    public HashSet<string> ConsumedFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Distinct import parameters (I_*) consumed by the selection.</summary>
    public List<ConsumedParameter> ConsumedParams { get; } = new();
}

/// <summary>
/// One import parameter consumed by the current selection, with where it comes from.
/// </summary>
public sealed class ConsumedParameter
{
    public required string Param { get; init; }            // e.g. I_WERKS
    public string FieldName { get; init; } = string.Empty; // DDIC field, e.g. WERKS
    public string FieldText { get; init; } = string.Empty; // description
    public string CentralDataAlias { get; init; } = string.Empty; // e.g. D14;I_WERKS

    /// <summary>TXT files (original names) that carry this parameter.</summary>
    public HashSet<string> SourceFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
}
