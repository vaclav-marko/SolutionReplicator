using System.Text.RegularExpressions;

namespace SolutionBuilder.Services;

/// <summary>
/// Handles all country code replacement logic for scope items, building blocks, and filenames.
/// </summary>
public class CountryCodeService
{
    private readonly string _sourceCountry;
    private readonly string _targetCountry;

    public CountryCodeService(string sourceCountry, string targetCountry)
    {
        _sourceCountry = sourceCountry.ToUpperInvariant();
        _targetCountry = targetCountry.ToUpperInvariant();
    }

    /// <summary>
    /// Replaces country code in a Scope Item Project ID.
    /// e.g. "DE_BD3_OP" -> "CZ_BD3_OP"
    /// </summary>
    public string ReplaceInProjectId(string projectId)
    {
        if (string.IsNullOrEmpty(projectId)) return projectId;

        // Pattern: starts with source country code followed by underscore
        if (projectId.StartsWith(_sourceCountry + "_", StringComparison.OrdinalIgnoreCase))
        {
            return _targetCountry + projectId[_sourceCountry.Length..];
        }

        return projectId;
    }

    /// <summary>
    /// Replaces country code in a Building Block ID.
    /// e.g. "BLV (DE)" -> "BLV (CZ)", "1IM (XX)" stays unchanged
    /// </summary>
    public string ReplaceInBbId(string bbId)
    {
        if (string.IsNullOrEmpty(bbId)) return bbId;

        // Pattern: "ID (CC)" where CC is the country code
        string pattern = $@"\({Regex.Escape(_sourceCountry)}\)";
        return Regex.Replace(bbId, pattern, $"({_targetCountry})");
    }

    /// <summary>
    /// Replaces country code in a filename.
    /// Three patterns:
    /// 1. No country code: "SMBERP_SCWM_WDGCM_O001_24B.TXT" -> no change
    /// 2. With country code: "SMBERP_SCWM_WDGCM_O001_24B_DE.TXT" -> "..._CZ.TXT"
    /// 3. With country code + counter: "SMBERP_SCWM_WDGCM_O001_24B_DE_01.TXT" -> "..._CZ_01.TXT"
    /// </summary>
    public string ReplaceInFilename(string filename)
    {
        if (string.IsNullOrEmpty(filename)) return filename;

        // Pattern with counter: _DE_01.TXT or _DE_01.txt
        string patternWithCounter = $@"_{Regex.Escape(_sourceCountry)}_(\d+)(\.TXT|\.txt)$";
        if (Regex.IsMatch(filename, patternWithCounter))
        {
            return Regex.Replace(filename, patternWithCounter, $"_{_targetCountry}_$1$2");
        }

        // Pattern without counter: _DE.TXT or _DE.txt
        string patternSimple = $@"_{Regex.Escape(_sourceCountry)}(\.TXT|\.txt)$";
        if (Regex.IsMatch(filename, patternSimple))
        {
            return Regex.Replace(filename, patternSimple, $"_{_targetCountry}$1");
        }

        // No country code found - return unchanged
        return filename;
    }

    /// <summary>
    /// Determines if a filename contains the source country code.
    /// </summary>
    public bool FilenameHasCountryCode(string filename)
    {
        if (string.IsNullOrEmpty(filename)) return false;

        string patternWithCounter = $@"_{Regex.Escape(_sourceCountry)}_\d+(\.TXT|\.txt)$";
        string patternSimple = $@"_{Regex.Escape(_sourceCountry)}(\.TXT|\.txt)$";

        return Regex.IsMatch(filename, patternWithCounter) || Regex.IsMatch(filename, patternSimple);
    }

    /// <summary>
    /// Replaces the country code in SIR_SI_COUNTRY or SIR_BB_COUNTRY fields.
    /// </summary>
    public string ReplaceCountryField(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        if (value.Equals(_sourceCountry, StringComparison.OrdinalIgnoreCase))
            return _targetCountry;

        return value;
    }

    /// <summary>
    /// Replaces country code in generic text content that might reference the source country.
    /// Used for SOLUID and SCOPE fields.
    /// </summary>
    public string ReplaceInSolutionId(string solutionId, string newSolutionName)
    {
        return newSolutionName;
    }
}