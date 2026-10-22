using System;
using System.Collections.Generic;
using System.Linq;

namespace SolutionReplicator.Models;

/// <summary>
/// One row of the placeholder CSV: a single (variable, field, value, system) mapping.
/// CSV layout is <c>variable;field;value;sysid</c> (semicolon-delimited).
/// </summary>
public sealed class PlaceholderMapping
{
    public string Variable { get; set; } = string.Empty;  // e.g. &PLN
    public string Field { get; set; } = string.Empty;     // e.g. I_WERKS
    public string Value { get; set; } = string.Empty;     // e.g. HU01
    public string SysId { get; set; } = string.Empty;     // e.g. HU-PROD
}

/// <summary>
/// The client's nomenclature table, loaded from a semicolon-delimited CSV. Each row binds one
/// placeholder variable (scoped to a field) to a real value for one target system (SysId).
/// One variable appears once per system; the processor picks a single SysId at compose time.
/// </summary>
public sealed class PlaceholderTable
{
    public List<PlaceholderMapping> Rows { get; } = new();

    /// <summary>Distinct target systems (SysId) present in the table, in first-seen order.</summary>
    public IReadOnlyList<string> Systems =>
        Rows.Select(r => r.SysId)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Distinct (variable, field) pairs across all systems, in first-seen order.</summary>
    public IReadOnlyList<(string Variable, string Field)> Variables =>
        Rows.Select(r => (r.Variable, r.Field))
            .Distinct()
            .ToList();
}
