using System;
using System.Collections.Generic;
using System.Linq;
using GitAnalyticsDashboard.Models;
using LibGit2Sharp;

namespace GitAnalyticsDashboard.Services
{
    public class StatisticsService
    {
        public RepositorySummary GenerateSummary(Repository repo, ReportData data)
        {
            var summary = new RepositorySummary();
            var now = DateTimeOffset.Now;

            summary.RepositoryName = new System.IO.DirectoryInfo(repo.Info.WorkingDirectory).Name;
            summary.TotalCommits = repo.Commits.Count();
            summary.TotalBranches = data.Branches.Count;
            summary.ActiveDevelopers = data.Developers.Count;

            summary.CommitsToday = data.RecentCommits.Count(c => (now - c.Date).TotalDays <= 1);
            summary.CommitsThisWeek = data.RecentCommits.Count(c => (now - c.Date).TotalDays <= 7);
            summary.CommitsThisMonth = data.RecentCommits.Count(c => (now - c.Date).TotalDays <= 30);

            if (data.RecentCommits.Any())
            {
                var firstCommit = repo.Commits.QueryBy(new CommitFilter { SortBy = CommitSortStrategies.Reverse }).First();
                summary.RepositoryAge = now - firstCommit.Author.When;
                summary.LargestCommit = data.RecentCommits.OrderByDescending(c => c.Insertions + c.Deletions).FirstOrDefault();
                summary.MostActiveDeveloper = data.Developers.FirstOrDefault()?.Name ?? "N/A";
            }

            summary.ActiveBranches = data.Branches.Count(b => !b.IsStale);
            summary.StaleBranches = data.Branches.Count(b => b.IsStale);
            summary.MergedBranches = data.Branches.Count(b => b.IsMerged);
            summary.UnmergedBranches = summary.TotalBranches - summary.MergedBranches;

            if (data.Branches.Any())
            {
                summary.AverageBranchAgeDays = data.Branches.Average(b => (now - b.LastActivityDate).TotalDays);
                summary.AverageCommitsPerBranch = data.Branches.Average(b => b.CommitCount);
            }

            return summary;
        }
    }
}
