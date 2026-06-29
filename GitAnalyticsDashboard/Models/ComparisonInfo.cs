using System;
using System.Collections.Generic;

namespace GitAnalyticsDashboard.Models
{
    public class CommitComparison
    {
        public string Sha { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public DateTimeOffset Date { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        public List<FileComparison> FileChanges { get; set; } = new();
        public string Summary { get; set; } = string.Empty;
        public int FilesChangedCount => FileChanges.Count;
    }

    public class FileComparison
    {
        public string Path { get; set; } = string.Empty;
        public string ChangeType { get; set; } = string.Empty; // Added, Modified, Deleted, Renamed
        public string BeforeContent { get; set; } = string.Empty;
        public string AfterContent { get; set; } = string.Empty;
        public string Diff { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;

        public List<string> DetectedChanges { get; set; } = new();
    }
}
