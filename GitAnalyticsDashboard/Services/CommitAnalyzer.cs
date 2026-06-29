using System.Collections.Generic;
using System.Linq;
using GitAnalyticsDashboard.Models;
using LibGit2Sharp;

namespace GitAnalyticsDashboard.Services
{
    public class CommitAnalyzer
    {
        public List<CommitInfo> Analyze(Repository repo, int limit = 1000)
        {
            var commits = new List<CommitInfo>();

            // Map of commit SHAs to branch names for local branches
            var commitBranchMap = new Dictionary<string, string>();
            foreach (var branch in repo.Branches.Where(b => !b.IsRemote))
            {
                foreach (var commit in repo.Commits.QueryBy(new CommitFilter { IncludeReachableFrom = branch }))
                {
                    if (!commitBranchMap.ContainsKey(commit.Sha))
                    {
                        commitBranchMap[commit.Sha] = branch.FriendlyName;
                    }
                }
            }

            foreach (var commit in repo.Commits.Take(limit))
            {
                var info = new CommitInfo
                {
                    Sha = commit.Sha,
                    Author = commit.Author.Name,
                    Email = commit.Author.Email,
                    Date = commit.Author.When,
                    Message = commit.MessageShort,
                    Branch = commitBranchMap.TryGetValue(commit.Sha, out var branchName) ? branchName : "detached"
                };

                if (commit.Parents.Any())
                {
                    var parent = commit.Parents.First();
                    var diff = repo.Diff.Compare<Patch>(parent.Tree, commit.Tree);
                    info.FilesChanged = diff.Count();
                    info.Insertions = diff.LinesAdded;
                    info.Deletions = diff.LinesDeleted;
                }
                else
                {
                    var diff = repo.Diff.Compare<Patch>(null, commit.Tree);
                    info.FilesChanged = diff.Count();
                    info.Insertions = diff.LinesAdded;
                    info.Deletions = diff.LinesDeleted;
                }
                commits.Add(info);
            }
            return commits;
        }
    }
}
