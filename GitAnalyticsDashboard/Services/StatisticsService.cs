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

            // Bare repositories have no working directory
            summary.RepositoryName = new System.IO.DirectoryInfo(repo.Info.WorkingDirectory ?? repo.Info.Path).Name;
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

        public void ApplyScanSummary(RepositorySummary summary, ScanResult scan)
        {
            summary.IsScopedScan = true;
            summary.StartingRef = scan.Start.Name;
            summary.StartingCommitSha = scan.Start.Commit.Sha;
            summary.TargetRef = scan.Target.Name;
            summary.TargetCommitSha = scan.Target.Commit.Sha;
            summary.StartIsAncestorOfTarget = scan.StartIsAncestorOfTarget;

            // Commit totals cover the scan range, not the whole repository history
            summary.TotalCommits = scan.Commits.Count;
            summary.MergeCommits = scan.CommitInfos.Count(c => c.IsMerge);

            summary.TotalFileChanges = scan.FileChanges.Count;
            summary.UniqueFilesChanged = scan.FileChanges.Select(c => c.FilePath).Distinct().Count();
            summary.FilesAdded = scan.FileChanges.Count(c => c.ChangeType == "Added");
            summary.FilesModified = scan.FileChanges.Count(c => c.ChangeType == "Modified");
            summary.FilesDeleted = scan.FileChanges.Count(c => c.ChangeType == "Deleted");
            summary.FilesRenamed = scan.FileChanges.Count(c => c.ChangeType == "Renamed");
            summary.TotalLinesAdded = scan.FileChanges.Sum(c => c.LinesAdded);
            summary.TotalLinesDeleted = scan.FileChanges.Sum(c => c.LinesDeleted);

            summary.ActiveFilters = scan.Options.Describe();
            summary.FilteredOutCommits = scan.FilteredOutCommits;
            summary.ExcludedFileChanges = scan.ExcludedFileChanges;
        }
    }
}
