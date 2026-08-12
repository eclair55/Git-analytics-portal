using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GitAnalyticsDashboard.Models;
using GitAnalyticsDashboard.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GitAnalyticsDashboard
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            var serviceProvider = new ServiceCollection()
                .AddLogging(builder => builder.AddConsole())
                .AddSingleton<IConfiguration>(configuration)
                .AddSingleton<BranchAnalyzer>()
                .AddSingleton<CommitAnalyzer>()
                .AddSingleton<DeveloperAnalyzer>()
                .AddSingleton<FileAnalyzer>()
                .AddSingleton<CodeChangeAnalyzer>()
                .AddSingleton<ComparisonService>()
                .AddSingleton<StatisticsService>()
                .AddSingleton<ReportOrchestrator>()
                .AddSingleton<HtmlReportGenerator>()
                .AddSingleton<ExcelReportGenerator>()
                .AddSingleton<GitScanningService>()
                .BuildServiceProvider();

            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
            var orchestrator = serviceProvider.GetRequiredService<ReportOrchestrator>();
            var htmlGenerator = serviceProvider.GetRequiredService<HtmlReportGenerator>();
            var excelGenerator = serviceProvider.GetRequiredService<ExcelReportGenerator>();
            var scanningService = serviceProvider.GetRequiredService<GitScanningService>();

            var outputDir = configuration.GetValue<string>("ReportConfig:OutputDirectory") ?? "./Reports";
            var staleThreshold = configuration.GetValue<int>("ReportConfig:StaleBranchThresholdDays", 30);
            var reportTitle = configuration.GetValue<string>("ReportConfig:ReportTitle") ?? "Git Analytics";
            var comparisonLimit = configuration.GetValue<int>("ReportConfig:ComparisonLimit", 100);

            Console.WriteLine("Git Analytics\n");

            // 1. Prompt and Validate Repository Path
            Console.WriteLine("Repository path:");
            var repoPathInput = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(repoPathInput))
            {
                Console.WriteLine("Invalid Git repository path.");
                return;
            }
            var fullPath = Path.GetFullPath(repoPathInput);
            if (!scanningService.ValidateRepositoryPath(fullPath))
            {
                Console.WriteLine("Invalid Git repository path.");
                return;
            }

            // 2. Prompt Starting Branch Name
            Console.WriteLine("Starting branch:");
            var startingBranchName = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(startingBranchName))
            {
                Console.WriteLine("Branch not found: ");
                return;
            }
            startingBranchName = startingBranchName.Trim();

            // 3. Resolve starting branch and commit
            using (var repo = new LibGit2Sharp.Repository(fullPath))
            {
                var startingBranch = scanningService.ResolveStartingBranch(repo, startingBranchName);
                if (startingBranch == null)
                {
                    Console.WriteLine($"Branch not found: {startingBranchName}");
                    return;
                }

                Console.WriteLine($"\nScanning from branch: {startingBranchName}");
                Console.WriteLine("Scanning commits up to latest commit...");

                var startingCommit = startingBranch.Tip;
                var latestCommit = repo.Head.Tip;

                // 4. Commit traversal
                var scannedCommits = scanningService.GetCommitsForScan(repo, startingCommit, latestCommit);

                // Check if any commits after starting branch (or if the only descendant commit is the startingCommit itself,
                // meaning there are no subsequent/new commits after starting branch).
                if (!scannedCommits.Any(c => c.Sha != startingCommit.Sha))
                {
                    Console.WriteLine("No commits found after the specified starting branch.");
                    return;
                }

                // 5. Branch detection
                var commitBranchMap = scanningService.GetCommitBranchMap(repo);

                // 6. File change extraction
                var fileChanges = scanningService.ExtractFileChanges(repo, scannedCommits, commitBranchMap);

                // 7. Result sorting (newest first)
                var sortedChanges = scanningService.SortFileChanges(fileChanges);

                Console.WriteLine("\nCompleted.\n");
                Console.WriteLine($"Total commits analyzed: {scannedCommits.Count}");
                Console.WriteLine($"Total file changes: {fileChanges.Count}");

                // 8. Output/export to console
                scanningService.PrintToConsole(sortedChanges);

                // Produce report data for preservation of HTML/Excel reports
                var reportData = orchestrator.AnalyzeWithStartingBranch(
                    fullPath,
                    startingBranchName,
                    scannedCommits,
                    commitBranchMap,
                    staleThreshold,
                    comparisonLimit);

                var repoOutputDir = Path.Combine(outputDir, reportData.Summary.RepositoryName);
                htmlGenerator.Generate(reportData, repoOutputDir, reportTitle);
                excelGenerator.Generate(reportData, Path.Combine(repoOutputDir, "report.xlsx"));

                logger.LogInformation("Reports generated successfully for {Repo} in {Dir}", reportData.Summary.RepositoryName, repoOutputDir);
            }
        }
    }
}
