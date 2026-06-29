using System;
using System.Collections.Generic;

namespace GitAnalyticsDashboard.Models
{
    public class DeveloperInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int TotalCommits { get; set; }
        public int LinesAdded { get; set; }
        public int LinesRemoved { get; set; }
        public int FilesModified { get; set; }
        public HashSet<string> ActiveBranches { get; set; } = new HashSet<string>();
        public DateTimeOffset FirstCommit { get; set; }
        public DateTimeOffset LatestCommit { get; set; }
    }
}
