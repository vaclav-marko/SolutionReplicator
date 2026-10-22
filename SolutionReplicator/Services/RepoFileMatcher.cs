using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SolutionReplicator.Services;

/// <summary>Result of matching a consumed file set against a repository folder.</summary>
public sealed class RepoMatchResult
{
    /// <summary>Absolute paths of repo files that matched a consumed file name.</summary>
    public List<string> MatchedFilePaths { get; } = new();

    /// <summary>Consumed file names that had no corresponding file in the repo.</summary>
    public List<string> UnmatchedConsumed { get; } = new();

    public int MatchedCount => MatchedFilePaths.Count;
    public int UnmatchedCount => UnmatchedConsumed.Count;
}

/// <summary>
/// Matches the files a scope consumes (from <c>ConsumptionResult.ConsumedFiles</c>) to the actual
/// TXT files in the client repository folder, by <b>exact filename</b> (case-insensitive).
///
/// The repository files are exported for the same template, so names line up one-to-one. Any
/// consumed file without a repo match is reported (never fabricated), so a divergent repo is
/// caught loudly instead of producing a silently incomplete package.
/// </summary>
public static class RepoFileMatcher
{
    public static RepoMatchResult Match(IReadOnlySet<string> consumedFiles, string repoFolder)
    {
        var result = new RepoMatchResult();

        var repoByName = Directory
            .GetFiles(repoFolder, "*.TXT", SearchOption.TopDirectoryOnly)
            .ToDictionary(p => Path.GetFileName(p), p => p, System.StringComparer.OrdinalIgnoreCase);

        foreach (var consumed in consumedFiles)
        {
            if (repoByName.TryGetValue(consumed, out var repoPath))
                result.MatchedFilePaths.Add(repoPath);
            else
                result.UnmatchedConsumed.Add(consumed);
        }

        return result;
    }
}
