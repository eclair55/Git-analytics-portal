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
    }
}
