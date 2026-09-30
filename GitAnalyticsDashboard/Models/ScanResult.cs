using System.Collections.Generic;
using System.Linq;
using LibGit2Sharp;

namespace GitAnalyticsDashboard.Models
{
    /// <summary>A branch, tag, SHA or revision expression resolved to a commit.</summary>
    public record ResolvedReference(string Name, Commit Commit, Branch? Branch);

    public class ScanResult
    {
        public ResolvedReference Start { get; set; } = null!;
        public ResolvedReference Target { get; set; } = null!;

        /// <summary>False when the target does not contain the starting commit (the branches have diverged).</summary>
        public bool StartIsAncestorOfTarget { get; set; }

        /// <summary>Scanned commits, newest first.</summary>
        public List<Commit> Commits { get; set; } = new();
        public Dictionary<string, List<string>> CommitBranchMap { get; set; } = new();
        public List<CommitInfo> CommitInfos { get; set; } = new();

        /// <summary>File changes, newest first.</summary>
        public List<FileChangeInfo> FileChanges { get; set; } = new();

        public ScanOptions Options { get; set; } = new();

        /// <summary>Commits in the range dropped by author/date filters, or whose changes were all excluded by path filters.</summary>
        public int FilteredOutCommits { get; set; }

        /// <summary>File changes dropped by the include/exclude path filters.</summary>
        public int ExcludedFileChanges { get; set; }

        /// <summary>Scanned commits other than the starting commit itself.</summary>
        public int NewCommitCount => Commits.Count(c => c.Sha != Start.Commit.Sha);
    }
}
