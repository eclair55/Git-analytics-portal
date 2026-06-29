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
            var fileStats = new Dictionary<string, FileStatistic>();
            var fileAuthors = new Dictionary<string, HashSet<string>>();

            foreach (var commit in repo.Commits.Take(commitLimit))
            {
                var author = commit.Author.Email;

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
