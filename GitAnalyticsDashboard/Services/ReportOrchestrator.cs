using System;
using System.Collections.Generic;
using System.Linq;
using GitAnalyticsDashboard.Models;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;

namespace GitAnalyticsDashboard.Services
{
    public class ReportOrchestrator
    {
        private readonly ILogger<ReportOrchestrator> _logger;
        private readonly BranchAnalyzer _branchAnalyzer;
        private readonly CommitAnalyzer _commitAnalyzer;
        private readonly DeveloperAnalyzer _developerAnalyzer;
        private readonly FileAnalyzer _fileAnalyzer;
        private readonly StatisticsService _statisticsService;
        private readonly ComparisonService _comparisonService;

        public ReportOrchestrator(
            ILogger<ReportOrchestrator> logger,
            BranchAnalyzer branchAnalyzer,
            CommitAnalyzer commitAnalyzer,
            DeveloperAnalyzer developerAnalyzer,
            FileAnalyzer fileAnalyzer,
            StatisticsService statisticsService,
            ComparisonService comparisonService)
        {
            _logger = logger;
            _branchAnalyzer = branchAnalyzer;
            _commitAnalyzer = commitAnalyzer;
            _developerAnalyzer = developerAnalyzer;
            _fileAnalyzer = fileAnalyzer;
            _statisticsService = statisticsService;
            _comparisonService = comparisonService;
        }

        public ReportData Analyze(string path, int staleThresholdDays, string? defaultBranchName = null, int comparisonLimit = 100)
        {
            _logger.LogInformation("Analyzing repository at {Path}", path);
            var reportData = new ReportData();

            using (var repo = new Repository(path))
            {
                var mainBranch = defaultBranchName != null ? repo.Branches[defaultBranchName] : (repo.Branches["main"] ?? repo.Branches["master"]);
                if (mainBranch == null && repo.Branches.Any())
                {
                    mainBranch = repo.Branches.First(b => b.IsCurrentRepositoryHead);
                }

                if (mainBranch == null)
                {
                    _logger.LogWarning("No suitable branch found for analysis.");
                    return reportData;
                }

                reportData.Branches = _branchAnalyzer.Analyze(repo, staleThresholdDays, mainBranch);
                reportData.RecentCommits = _commitAnalyzer.Analyze(repo);
                reportData.Developers = _developerAnalyzer.Analyze(reportData.RecentCommits);
                reportData.FileHotspots = _fileAnalyzer.Analyze(repo);
                reportData.Comparisons = _comparisonService.Analyze(repo, mainBranch, comparisonLimit);
                reportData.Summary = _statisticsService.GenerateSummary(repo, reportData);
            }

            return reportData;
        }

        public ReportData AnalyzeWithStartingBranch(
            string path,
            string startingBranchName,
            List<Commit> scannedCommits,
            Dictionary<string, List<string>> commitBranchMap,
            int staleThresholdDays,
            int comparisonLimit = 100)
        {
            _logger.LogInformation("Analyzing repository at {Path} starting from {StartingBranch}", path, startingBranchName);
            var reportData = new ReportData();

            using (var repo = new Repository(path))
            {
                var startingBranch = repo.Branches[startingBranchName];

                reportData.Branches = _branchAnalyzer.Analyze(repo, staleThresholdDays, startingBranch);

                // Convert scanned commits to CommitInfo using our commitBranchMap and parent diffs
                var commitInfos = new List<CommitInfo>();
                foreach (var commit in scannedCommits)
                {
                    var branchList = commitBranchMap.TryGetValue(commit.Sha, out var bList) ? bList : new List<string> { "detached" };
                    var branchesString = string.Join(", ", branchList.OrderBy(b => b));

                    var info = new CommitInfo
                    {
                        Sha = commit.Sha,
                        Author = commit.Author.Name,
                        Email = commit.Author.Email ?? string.Empty,
                        Date = commit.Author.When,
                        Message = commit.MessageShort,
                        Branch = branchesString
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

                    commitInfos.Add(info);
                }

                reportData.RecentCommits = commitInfos;
                reportData.Developers = _developerAnalyzer.Analyze(reportData.RecentCommits);
                reportData.FileHotspots = _fileAnalyzer.Analyze(repo, scannedCommits);
                reportData.Comparisons = _comparisonService.Analyze(repo, scannedCommits.Take(comparisonLimit).ToList(), commitBranchMap);
                reportData.Summary = _statisticsService.GenerateSummary(repo, reportData);
            }

            return reportData;
        }
    }
}
