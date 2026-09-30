using System;

namespace GitAnalyticsDashboard.Models
{
    public class FileChangeInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string OldPath { get; set; } = string.Empty; // Set for renamed/copied files
        public DateTimeOffset ChangeDate { get; set; }
        public string AuthorName { get; set; } = string.Empty;
        public string AuthorEmail { get; set; } = string.Empty;
        public string CommitHash { get; set; } = string.Empty;
        public string CommitMessage { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        public string ChangeType { get; set; } = string.Empty;
        public int LinesAdded { get; set; }
        public int LinesDeleted { get; set; }
    }
}
