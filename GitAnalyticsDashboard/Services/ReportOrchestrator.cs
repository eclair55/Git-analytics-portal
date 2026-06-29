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

        public ReportOrchestrator(
            ILogger<ReportOrchestrator> logger,
            BranchAnalyzer branchAnalyzer,
            CommitAnalyzer commitAnalyzer,
            DeveloperAnalyzer developerAnalyzer,
            FileAnalyzer fileAnalyzer,
            StatisticsService statisticsService)
        {
            _logger = logger;
            _branchAnalyzer = branchAnalyzer;
            _commitAnalyzer = commitAnalyzer;
            _developerAnalyzer = developerAnalyzer;
            _fileAnalyzer = fileAnalyzer;
            _statisticsService = statisticsService;
        }

        public ReportData Analyze(string path, int staleThresholdDays, string? defaultBranchName = null)
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

                reportData.Branches = _branchAnalyzer.Analyze(repo, staleThresholdDays, mainBranch);
                reportData.RecentCommits = _commitAnalyzer.Analyze(repo);
                reportData.Developers = _developerAnalyzer.Analyze(reportData.RecentCommits);
                reportData.FileHotspots = _fileAnalyzer.Analyze(repo);
                reportData.Summary = _statisticsService.GenerateSummary(repo, reportData);
            }

            return reportData;
        }
    }
}
