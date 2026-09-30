using System.Collections.Generic;
using System.Linq;
using GitAnalyticsDashboard.Models;
using LibGit2Sharp;

namespace GitAnalyticsDashboard.Services
{
    public class FileAnalyzer
    {
        public List<FileStatistic> Analyze(Repository repo, int commitLimit = 500)
        {
            return Analyze(repo, repo.Commits.Take(commitLimit).ToList());
        }

        /// <summary>Hotspots from already-extracted file changes, so no additional diffs are needed.</summary>
        public List<FileStatistic> AnalyzeChanges(IEnumerable<FileChangeInfo> changes, int top = 20)
        {
            return changes
                .GroupBy(c => c.FilePath)
                .Select(g => new FileStatistic
                {
                    Path = g.Key,
                    CommitCount = g.Select(c => c.CommitHash).Distinct().Count(),
                    ContributorCount = g.Select(c => c.AuthorEmail).Distinct().Count(),
                    TotalInsertions = g.Sum(c => c.LinesAdded),
                    TotalDeletions = g.Sum(c => c.LinesDeleted)
                })
                .OrderByDescending(f => f.CommitCount)
                .ThenByDescending(f => f.TotalInsertions + f.TotalDeletions)
                .Take(top)
                .ToList();
        }

        public List<FileStatistic> Analyze(Repository repo, List<Commit> commits)
        {
            var fileStats = new Dictionary<string, FileStatistic>();
            var fileAuthors = new Dictionary<string, HashSet<string>>();

            foreach (var commit in commits)
            {
                var author = commit.Author.Email ?? string.Empty;

                if (commit.Parents.Any())
                {
                    var parent = commit.Parents.First();
                    var diff = repo.Diff.Compare<Patch>(parent.Tree, commit.Tree);

                    foreach (var change in diff)
                    {
                        var path = change.Path;
                        if (!fileStats.TryGetValue(path, out var stat))
                        {
                            stat = new FileStatistic { Path = path };
                            fileStats[path] = stat;
                            fileAuthors[path] = new HashSet<string>();
                        }

                        stat.CommitCount++;
                        stat.TotalInsertions += change.LinesAdded;
                        stat.TotalDeletions += change.LinesDeleted;
                        fileAuthors[path].Add(author);
                    }
                }
                else
                {
                    // Initial commit
                    var diff = repo.Diff.Compare<Patch>(null, commit.Tree);
                    foreach (var change in diff)
                    {
                        var path = change.Path;
                        if (!fileStats.TryGetValue(path, out var stat))
                        {
                            stat = new FileStatistic { Path = path };
                            fileStats[path] = stat;
                            fileAuthors[path] = new HashSet<string>();
                        }
                        stat.CommitCount++;
                        stat.TotalInsertions += change.LinesAdded;
                        stat.TotalDeletions += change.LinesDeleted;
                        fileAuthors[path].Add(author);
                    }
                }
            }

            foreach (var stat in fileStats.Values)
            {
                stat.ContributorCount = fileAuthors[stat.Path].Count;
            }

            return fileStats.Values.OrderByDescending(f => f.CommitCount).Take(20).ToList();
        }
    }
}
