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
        private readonly ModuleAnalyzer _moduleAnalyzer;
        private readonly ReleaseNotesGenerator _releaseNotesGenerator;

        public ReportOrchestrator(
            ILogger<ReportOrchestrator> logger,
            BranchAnalyzer branchAnalyzer,
            CommitAnalyzer commitAnalyzer,
            DeveloperAnalyzer developerAnalyzer,
            FileAnalyzer fileAnalyzer,
            StatisticsService statisticsService,
            ComparisonService comparisonService,
            ModuleAnalyzer moduleAnalyzer,
            ReleaseNotesGenerator releaseNotesGenerator)
        {
            _moduleAnalyzer = moduleAnalyzer;
            _releaseNotesGenerator = releaseNotesGenerator;
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

        /// <summary>
        /// Builds report data for a starting-branch scan. Reuses the scan's per-commit diffs, so only the
        /// before/after comparison (which needs file contents) diffs commits again.
        /// </summary>
        public ReportData AnalyzeScan(
            Repository repo,
            ScanResult scan,
            int staleThresholdDays,
            int comparisonLimit = 100,
            ComparisonOptions? comparisonOptions = null,
            ReleaseNotesOptions? releaseNotesOptions = null)
        {
            comparisonOptions ??= new ComparisonOptions();
            comparisonOptions.PathFilter = scan.Options.PathFilter;
            comparisonOptions.Mailmap = scan.Options.Mailmap;

            _logger.LogInformation("Building reports for {Count} commits from {Start} to {Target}", scan.Commits.Count, scan.Start.Name, scan.Target.Name);

            var reportData = new ReportData
            {
                RecentCommits = scan.CommitInfos,
                FileChanges = scan.FileChanges
            };

            // Ahead/behind counts are relative to the starting branch (or the target when the start is a tag/SHA)
            var baseBranch = scan.Start.Branch ?? scan.Target.Branch;

            reportData.Branches = _branchAnalyzer.Analyze(repo, staleThresholdDays, baseBranch);
            reportData.Developers = _developerAnalyzer.Analyze(reportData.RecentCommits);
            reportData.FileHotspots = _fileAnalyzer.AnalyzeChanges(scan.FileChanges);
            reportData.Modules = _moduleAnalyzer.Analyze(scan.Target.Commit, scan.FileChanges, scan.Options.PathFilter);
            reportData.ReleaseNotes = _releaseNotesGenerator.Generate(scan, releaseNotesOptions);

            // Merge commits carry no file changes of their own in a scan (see GitScanningService)
            var comparisonCommits = scan.Commits.Where(c => c.Parents.Count() <= 1).Take(comparisonLimit).ToList();
            reportData.Comparisons = _comparisonService.Analyze(repo, comparisonCommits, scan.CommitBranchMap, comparisonOptions);

            reportData.Summary = _statisticsService.GenerateSummary(repo, reportData);
            _statisticsService.ApplyScanSummary(reportData.Summary, scan);

            return reportData;
        }
    }
}
