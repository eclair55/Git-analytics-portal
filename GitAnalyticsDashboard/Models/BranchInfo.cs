using System;

namespace GitAnalyticsDashboard.Models
{
    public class BranchInfo
    {
        public string Name { get; set; } = string.Empty;
        public bool IsCurrent { get; set; }
        public string LatestCommitSha { get; set; } = string.Empty;
        public string LatestCommitMessage { get; set; } = string.Empty;
        public DateTimeOffset LatestCommitDate { get; set; }
        public string LatestCommitAuthor { get; set; } = string.Empty;
        public int CommitCount { get; set; }
        public int AheadCount { get; set; }
        public int BehindCount { get; set; }
        public bool IsMerged { get; set; }
        public DateTimeOffset LastActivityDate { get; set; }
        public bool IsStale { get; set; }
    }
}
