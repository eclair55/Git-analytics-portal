using System.Collections.Generic;

namespace GitAnalyticsDashboard.Models
{
    public class ReportData
    {
        public RepositorySummary Summary { get; set; } = new();
        public List<BranchInfo> Branches { get; set; } = new();
        public List<CommitInfo> RecentCommits { get; set; } = new();
        public List<DeveloperInfo> Developers { get; set; } = new();
        public List<FileStatistic> FileHotspots { get; set; } = new();
        public List<CommitComparison> Comparisons { get; set; } = new();
        public List<FileChangeInfo> FileChanges { get; set; } = new();
        public List<ModuleStatistic> Modules { get; set; } = new();
        public ReleaseNotes? ReleaseNotes { get; set; }
    }
}
