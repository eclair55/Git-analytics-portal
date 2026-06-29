using System.Collections.Generic;
using System.Linq;
using GitAnalyticsDashboard.Models;

namespace GitAnalyticsDashboard.Services
{
    public class DeveloperAnalyzer
    {
        public List<DeveloperInfo> Analyze(List<CommitInfo> commits)
        {
            var developerMap = new Dictionary<string, DeveloperInfo>();

            foreach (var commit in commits)
            {
                if (!developerMap.TryGetValue(commit.Email, out var devInfo))
                {
                    devInfo = new DeveloperInfo
                    {
                        Name = commit.Author,
                        Email = commit.Email,
                        FirstCommit = commit.Date,
                        LatestCommit = commit.Date
                    };
                    developerMap[commit.Email] = devInfo;
                }

                devInfo.TotalCommits++;
                devInfo.LinesAdded += commit.Insertions;
                devInfo.LinesRemoved += commit.Deletions;
                devInfo.FilesModified += commit.FilesChanged;

                if (commit.Date < devInfo.FirstCommit) devInfo.FirstCommit = commit.Date;
                if (commit.Date > devInfo.LatestCommit) devInfo.LatestCommit = commit.Date;
            }

            return developerMap.Values.OrderByDescending(d => d.TotalCommits).ToList();
        }
    }
}
