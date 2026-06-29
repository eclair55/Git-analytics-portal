namespace GitAnalyticsDashboard.Models
{
    public class FileStatistic
    {
        public string Path { get; set; } = string.Empty;
        public int CommitCount { get; set; }
        public int ContributorCount { get; set; }
        public int TotalInsertions { get; set; }
        public int TotalDeletions { get; set; }
    }
}
