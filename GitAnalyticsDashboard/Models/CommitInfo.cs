using System;

namespace GitAnalyticsDashboard.Models
{
    public class CommitInfo
    {
        public string Sha { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public DateTimeOffset Date { get; set; }
        public string Branch { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public int FilesChanged { get; set; }
        public int Insertions { get; set; }
        public int Deletions { get; set; }
        public bool IsMerge { get; set; }
    }
}
