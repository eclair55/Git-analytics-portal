using System.Collections.Generic;
using System.Linq;
using GitAnalyticsDashboard.Models;
using GitAnalyticsDashboard.Utilities;
using LibGit2Sharp;

namespace GitAnalyticsDashboard.Services
{
    public class BranchAnalyzer
    {
        public List<BranchInfo> Analyze(Repository repo, int staleThresholdDays, Branch? mainBranch)
        {
            var branches = new List<BranchInfo>();
            foreach (var branch in repo.Branches.Where(b => !b.IsRemote))
            {
                var latestCommit = branch.Tip;
                var branchInfo = new BranchInfo
                {
                    Name = branch.FriendlyName,
                    IsCurrent = branch.IsCurrentRepositoryHead,
                    LatestCommitSha = latestCommit.Sha,
                    LatestCommitMessage = latestCommit.MessageShort,
                    LatestCommitDate = latestCommit.Author.When,
                    LatestCommitAuthor = latestCommit.Author.Name,
                    LastActivityDate = latestCommit.Author.When,
                    IsStale = DateUtilities.IsStale(latestCommit.Author.When, staleThresholdDays)
                };

                branchInfo.CommitCount = repo.Commits.QueryBy(new CommitFilter { IncludeReachableFrom = branch }).Count();

                if (mainBranch != null && branch.FriendlyName != mainBranch.FriendlyName)
                {
                    var divergence = repo.ObjectDatabase.CalculateHistoryDivergence(branch.Tip, mainBranch.Tip);
                    branchInfo.AheadCount = divergence.AheadBy ?? 0;
                    branchInfo.BehindCount = divergence.BehindBy ?? 0;
                    branchInfo.IsMerged = repo.ObjectDatabase.CalculateHistoryDivergence(mainBranch.Tip, branch.Tip).BehindBy == 0;
                }
                else if (mainBranch != null && branch.FriendlyName == mainBranch.FriendlyName)
                {
                    branchInfo.IsMerged = true;
                }

                branches.Add(branchInfo);
            }
            return branches;
        }
    }
}
