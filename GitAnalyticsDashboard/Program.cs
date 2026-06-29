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
                .AddSingleton<StatisticsService>()
                .AddSingleton<ReportOrchestrator>()
                .AddSingleton<HtmlReportGenerator>()
                .AddSingleton<ExcelReportGenerator>()
                .BuildServiceProvider();

            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
            var orchestrator = serviceProvider.GetRequiredService<ReportOrchestrator>();
            var htmlGenerator = serviceProvider.GetRequiredService<HtmlReportGenerator>();
            var excelGenerator = serviceProvider.GetRequiredService<ExcelReportGenerator>();

            var repoPaths = configuration.GetSection("ReportConfig:RepositoryPaths").Get<string[]>() ?? new[] { "." };
            var outputDir = configuration.GetValue<string>("ReportConfig:OutputDirectory") ?? "./Reports";
            var staleThreshold = configuration.GetValue<int>("ReportConfig:StaleBranchThresholdDays", 30);
            var reportTitle = configuration.GetValue<string>("ReportConfig:ReportTitle") ?? "Git Analytics";

            //foreach (var path in repoPaths)
            //{
                //try
                //{
                string fullPath;

                while (true)
                {
                    Console.Write("Enter Git repository path: ");
                    var input = Console.ReadLine();

                    if (string.IsNullOrWhiteSpace(input))
                    {
                        Console.WriteLine("Path cannot be empty.\n");
                        continue;
                    }

                    fullPath = Path.GetFullPath(input);

                    if (!Directory.Exists(fullPath))
                    {
                        Console.WriteLine("Directory does not exist.\n");
                        continue;
                    }

                    if (!LibGit2Sharp.Repository.IsValid(fullPath))
                    {
                        Console.WriteLine("Not a valid Git repository.\n");
                        continue;
                    }

                    break;
                }

                var reportData = orchestrator.Analyze(fullPath, staleThreshold);

                var repoOutputDir = Path.Combine(outputDir, reportData.Summary.RepositoryName);
                    htmlGenerator.Generate(reportData, repoOutputDir, reportTitle);
                    excelGenerator.Generate(reportData, Path.Combine(repoOutputDir, "report.xlsx"));

                    logger.LogInformation("Reports generated successfully for {Repo} in {Dir}", reportData.Summary.RepositoryName, repoOutputDir);
                //}
                //catch (Exception ex)
                //{
                //    logger.LogError(ex, "Error processing repository at {Path}", fullPath);
                //}
            //}
        }
    }
}
