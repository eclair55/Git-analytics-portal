using System.IO;
using System.Linq;
using System.Text.Json;
using GitAnalyticsDashboard.Models;

namespace GitAnalyticsDashboard.Services
{
    /// <summary>
    /// Writes the full report data as JSON for Power BI and other tools. Before/after file contents
    /// and raw diffs are left out to keep the file small; they are in the HTML and Excel reports.
    /// </summary>
    public class JsonReportGenerator
    {
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public void Generate(ReportData data, string filePath)
        {
            File.WriteAllText(filePath, Serialize(data));
        }

        public string Serialize(ReportData data)
        {
            var document = new
            {
                SchemaVersion = 1,
                data.Summary,
                data.Branches,
                Commits = data.RecentCommits,
                data.Developers,
                data.FileHotspots,
                data.Modules,
                data.FileChanges,
                data.ReleaseNotes,
                Comparisons = data.Comparisons.Select(c => new
                {
                    c.Sha,
                    c.Author,
                    c.Date,
                    c.Message,
                    c.Branch,
                    Files = c.FileChanges.Select(f => new
                    {
                        f.Path,
                        f.ChangeType,
                        f.Summary,
                        f.DetectedChanges,
                        f.IsBinary,
                        f.IsTruncated
                    })
                })
            };

            return JsonSerializer.Serialize(document, SerializerOptions);
        }
    }
}
