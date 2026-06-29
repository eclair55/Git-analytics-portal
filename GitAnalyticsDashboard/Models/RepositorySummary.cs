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
    }
}
