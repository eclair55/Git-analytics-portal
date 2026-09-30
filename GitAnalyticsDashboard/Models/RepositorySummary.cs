using System;

namespace GitAnalyticsDashboard.Models
{
    public class RepositorySummary
    {
        public string RepositoryName { get; set; } = string.Empty;
        public int TotalCommits { get; set; }
        public int TotalBranches { get; set; }
        public int ActiveDevelopers { get; set; }
        public int CommitsToday { get; set; }
        public int CommitsThisWeek { get; set; }
        public int CommitsThisMonth { get; set; }
        public TimeSpan RepositoryAge { get; set; }
        public CommitInfo? LargestCommit { get; set; }
        public string MostActiveDeveloper { get; set; } = string.Empty;

        // Branch health
        public int ActiveBranches { get; set; }
        public int StaleBranches { get; set; }
        public int MergedBranches { get; set; }
        public int UnmergedBranches { get; set; }
        public double AverageBranchAgeDays { get; set; }
        public double AverageCommitsPerBranch { get; set; }

        // Starting-branch scan (populated when the report covers a scan range)
        public bool IsScopedScan { get; set; }
        public string StartingRef { get; set; } = string.Empty;
        public string StartingCommitSha { get; set; } = string.Empty;
        public string TargetRef { get; set; } = string.Empty;
        public string TargetCommitSha { get; set; } = string.Empty;
        public bool StartIsAncestorOfTarget { get; set; } = true;
        public int MergeCommits { get; set; }
        public int TotalFileChanges { get; set; }
        public int UniqueFilesChanged { get; set; }
        public int FilesAdded { get; set; }
        public int FilesModified { get; set; }
        public int FilesDeleted { get; set; }
        public int FilesRenamed { get; set; }
        public int TotalLinesAdded { get; set; }
        public int TotalLinesDeleted { get; set; }
        public List<string> ActiveFilters { get; set; } = new();
        public int FilteredOutCommits { get; set; }
        public int ExcludedFileChanges { get; set; }
    }
}
