namespace GitAnalyticsDashboard.Models
{
    public class ModuleStatistic
    {
        /// <summary>Project directory (e.g. "src/Api") or top-level folder; "(root)" for files at the repository root.</summary>
        public string Module { get; set; } = string.Empty;

        /// <summary>Project file that defines the module (e.g. "Api.csproj"), empty for plain folders.</summary>
        public string ProjectFile { get; set; } = string.Empty;

        public int FileChanges { get; set; }
        public int FilesTouched { get; set; }
        public int Commits { get; set; }
        public int Contributors { get; set; }
        public int LinesAdded { get; set; }
        public int LinesDeleted { get; set; }
        public int Added { get; set; }
        public int Modified { get; set; }
        public int Deleted { get; set; }
        public int Renamed { get; set; }

        public int Churn => LinesAdded + LinesDeleted;
    }
}
