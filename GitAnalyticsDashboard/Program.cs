using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GitAnalyticsDashboard.Models;
using GitAnalyticsDashboard.Services;
using GitAnalyticsDashboard.Utilities;
using LibGit2Sharp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GitAnalyticsDashboard
{
    class Program
    {
        // Options that take no value
        private static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase) { "help", "no-default-excludes" };

        static int Main(string[] args)
        {
            var (namedArgs, positionalArgs) = ParseArgs(args);
            if (namedArgs.ContainsKey("help") || args.Contains("-h"))
            {
                PrintUsage();
                return 0;
            }

            // appsettings.json is copied next to the executable, so the app works from any working directory
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .Build();

            var serviceProvider = new ServiceCollection()
                .AddLogging(builder => builder.AddConsole())
                .AddSingleton<IConfiguration>(configuration)
                .AddSingleton<BranchAnalyzer>()
                .AddSingleton<CommitAnalyzer>()
                .AddSingleton<DeveloperAnalyzer>()
                .AddSingleton<FileAnalyzer>()
                .AddSingleton<ModuleAnalyzer>()
                .AddSingleton<CodeChangeAnalyzer>()
                .AddSingleton<ComparisonService>()
                .AddSingleton<StatisticsService>()
                .AddSingleton<ReleaseNotesGenerator>()
                .AddSingleton<ReportOrchestrator>()
                .AddSingleton<HtmlReportGenerator>()
                .AddSingleton<ExcelReportGenerator>()
                .AddSingleton<JsonReportGenerator>()
                .AddSingleton<GitScanningService>()
                .BuildServiceProvider();

            if (!TryBuildSettings(configuration, namedArgs, positionalArgs, out var settings, out var error))
            {
                Console.WriteLine(error);
                return 1;
            }

            Console.WriteLine("Git Analytics\n");

            try
            {
                return Run(serviceProvider, settings);
            }
            catch (LibGit2SharpException ex)
            {
                Console.WriteLine($"Git error: {ex.Message}");
                return 1;
            }
            catch (IOException ex)
            {
                // Typically report.xlsx being open in Excel
                Console.WriteLine($"Could not write reports: {ex.Message}");
                return 1;
            }
        }

        private record RunSettings(
            string? RepoArg,
            string? StartArg,
            string? TargetArg,
            string OutputDirectory,
            int StaleThresholdDays,
            string ReportTitle,
            int ComparisonLimit,
            int ConsoleRowLimit,
            List<string> IncludePaths,
            List<string> ExcludePaths,
            string? Author,
            DateTimeOffset? Since,
            DateTimeOffset? Until,
            List<string> MailmapEntries,
            ComparisonOptions ComparisonOptions,
            ReleaseNotesOptions ReleaseNotesOptions);

        private static bool TryBuildSettings(
            IConfiguration configuration,
            Dictionary<string, string> namedArgs,
            List<string> positionalArgs,
            out RunSettings settings,
            out string error)
        {
            settings = null!;
            error = string.Empty;

            DateTimeOffset? since = null, until = null;
            var sinceArg = GetArg(namedArgs, positionalArgs, "since", -1);
            var untilArg = GetArg(namedArgs, positionalArgs, "until", -1);
            if (sinceArg != null)
            {
                if (!TryParseDate(sinceArg, endOfDay: false, out var parsed))
                {
                    error = $"Invalid --since date: {sinceArg}";
                    return false;
                }
                since = parsed;
            }
            if (untilArg != null)
            {
                if (!TryParseDate(untilArg, endOfDay: true, out var parsed))
                {
                    error = $"Invalid --until date: {untilArg}";
                    return false;
                }
                until = parsed;
            }

            var excludes = namedArgs.ContainsKey("no-default-excludes")
                ? new List<string>()
                : ReadList(configuration, "ReportConfig:ExcludePaths");
            excludes.AddRange(PathFilter.SplitPatterns(GetArg(namedArgs, positionalArgs, "exclude", -1)));

            var includes = ReadList(configuration, "ReportConfig:IncludePaths");
            includes.AddRange(PathFilter.SplitPatterns(GetArg(namedArgs, positionalArgs, "include", -1)));

            settings = new RunSettings(
                RepoArg: GetArg(namedArgs, positionalArgs, "repo", 0),
                StartArg: GetArg(namedArgs, positionalArgs, "start", 1),
                TargetArg: GetArg(namedArgs, positionalArgs, "target", 2),
                OutputDirectory: GetArg(namedArgs, positionalArgs, "output", -1)
                    ?? configuration.GetValue<string>("ReportConfig:OutputDirectory")
                    ?? "./Reports",
                StaleThresholdDays: configuration.GetValue<int>("ReportConfig:StaleBranchThresholdDays", 30),
                ReportTitle: configuration.GetValue<string>("ReportConfig:ReportTitle") ?? "Git Analytics",
                ComparisonLimit: configuration.GetValue<int>("ReportConfig:ComparisonLimit", 100),
                ConsoleRowLimit: configuration.GetValue<int>("ReportConfig:ConsoleRowLimit", 200),
                IncludePaths: includes,
                ExcludePaths: excludes,
                Author: GetArg(namedArgs, positionalArgs, "author", -1),
                Since: since,
                Until: until,
                MailmapEntries: ReadList(configuration, "ReportConfig:Mailmap"),
                ComparisonOptions: new ComparisonOptions
                {
                    MaxFileBytes = configuration.GetValue<long>("ReportConfig:MaxComparisonFileKB", 256) * 1024,
                    MaxDiffChars = configuration.GetValue<int>("ReportConfig:MaxDiffChars", 200_000)
                },
                ReleaseNotesOptions: new ReleaseNotesOptions
                {
                    TicketPattern = configuration.GetValue<string>("ReportConfig:TicketPattern") ?? ReleaseNotesOptions.DefaultTicketPattern,
                    TicketUrlTemplate = configuration.GetValue<string>("ReportConfig:TicketUrlTemplate") ?? string.Empty
                });
            return true;
        }

        private static int Run(IServiceProvider serviceProvider, RunSettings settings)
        {
            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
            var orchestrator = serviceProvider.GetRequiredService<ReportOrchestrator>();
            var htmlGenerator = serviceProvider.GetRequiredService<HtmlReportGenerator>();
            var excelGenerator = serviceProvider.GetRequiredService<ExcelReportGenerator>();
            var jsonGenerator = serviceProvider.GetRequiredService<JsonReportGenerator>();
            var releaseNotesGenerator = serviceProvider.GetRequiredService<ReleaseNotesGenerator>();
            var scanningService = serviceProvider.GetRequiredService<GitScanningService>();

            // 1. Repository path
            string? repoPath;
            if (settings.RepoArg != null)
            {
                repoPath = GitScanningService.NormalizePath(settings.RepoArg);
                if (repoPath == null || !scanningService.ValidateRepositoryPath(repoPath))
                {
                    Console.WriteLine($"Invalid Git repository path: {settings.RepoArg}");
                    return 1;
                }
            }
            else
            {
                repoPath = scanningService.PromptRepositoryPath();
                if (repoPath == null)
                    return 1;
            }

            using var repo = new Repository(repoPath);
            var head = scanningService.ResolveHead(repo);
            if (head == null)
            {
                Console.WriteLine("Repository has no commits.");
                return 1;
            }

            // 2. Starting branch
            var start = settings.StartArg != null
                ? ResolveOrReport(scanningService, repo, settings.StartArg)
                : scanningService.PromptReference(repo, "Starting branch:", allowEmptyForHead: false);
            if (start == null)
                return 1;

            // 3. Target branch (defaults to HEAD; only prompted in fully interactive runs)
            ResolvedReference? target;
            if (settings.TargetArg != null)
                target = ResolveOrReport(scanningService, repo, settings.TargetArg);
            else if (settings.StartArg != null)
                target = head;
            else
                target = scanningService.PromptReference(repo, $"Target branch (press Enter for {head.Name}):", allowEmptyForHead: true);
            if (target == null)
                return 1;

            var scanOptions = new ScanOptions
            {
                PathFilter = new PathFilter(settings.IncludePaths, settings.ExcludePaths),
                Author = settings.Author,
                Since = settings.Since,
                Until = settings.Until,
                Mailmap = Mailmap.Load(repo, target.Commit, settings.MailmapEntries)
            };

            // 4-7. Commit traversal, branch detection, file change extraction and sorting
            Console.WriteLine($"\nScanning from branch: {start.Name} ({start.Commit.Sha[..7]})");
            Console.WriteLine($"Scanning commits up to: {target.Name} ({target.Commit.Sha[..7]})...");

            var scan = scanningService.Scan(repo, start, target, scanOptions);

            if (!scan.StartIsAncestorOfTarget)
            {
                Console.WriteLine($"Warning: {start.Name} is not an ancestor of {target.Name}. Scanning commits on {target.Name} that are not in the history before {start.Name}.");
            }

            if (scan.NewCommitCount == 0)
            {
                Console.WriteLine(scan.FilteredOutCommits > 0
                    ? $"No commits found after the specified starting branch that match the filters ({scan.FilteredOutCommits} filtered out)."
                    : "No commits found after the specified starting branch.");
                return 0;
            }

            // 8. Output/export to console
            scanningService.PrintSummary(scan);
            scanningService.PrintToConsole(scan.FileChanges, settings.ConsoleRowLimit);

            // Reports
            var reportData = orchestrator.AnalyzeScan(repo, scan, settings.StaleThresholdDays, settings.ComparisonLimit,
                settings.ComparisonOptions, settings.ReleaseNotesOptions);

            var repoOutputDir = Path.GetFullPath(Path.Combine(settings.OutputDirectory, reportData.Summary.RepositoryName));
            htmlGenerator.Generate(reportData, repoOutputDir, settings.ReportTitle);
            excelGenerator.Generate(reportData, Path.Combine(repoOutputDir, "report.xlsx"));
            jsonGenerator.Generate(reportData, Path.Combine(repoOutputDir, "report.json"));
            if (reportData.ReleaseNotes != null)
            {
                releaseNotesGenerator.WriteMarkdown(reportData.ReleaseNotes, Path.Combine(repoOutputDir, "CHANGELOG.md"));
            }
            ExportCsv(reportData, repoOutputDir);

            Console.WriteLine("\nReports:");
            Console.WriteLine($"  HTML:          {Path.Combine(repoOutputDir, "index.html")}");
            Console.WriteLine($"  Excel:         {Path.Combine(repoOutputDir, "report.xlsx")}");
            Console.WriteLine($"  JSON:          {Path.Combine(repoOutputDir, "report.json")}");
            Console.WriteLine($"  Release notes: {Path.Combine(repoOutputDir, "CHANGELOG.md")}");
            Console.WriteLine($"  CSV:           {Path.Combine(repoOutputDir, "file-changes.csv")}");
            Console.WriteLine($"                 {Path.Combine(repoOutputDir, "commits.csv")}");
            Console.WriteLine($"                 {Path.Combine(repoOutputDir, "modules.csv")}");

            logger.LogInformation("Reports generated successfully for {Repo} in {Dir}", reportData.Summary.RepositoryName, repoOutputDir);
            return 0;
        }

        private static ResolvedReference? ResolveOrReport(GitScanningService scanningService, Repository repo, string reference)
        {
            var resolved = scanningService.ResolveReference(repo, reference);
            if (resolved == null)
            {
                scanningService.PrintBranchNotFound(repo, reference.Trim());
            }
            return resolved;
        }

        private static void ExportCsv(ReportData reportData, string outputDir)
        {
            CsvExporter.ExportToCsv(reportData.FileChanges.Select(c => new
            {
                Date = c.ChangeDate.ToString("yyyy-MM-dd HH:mm:ss zzz"),
                c.FilePath,
                c.ChangeType,
                c.OldPath,
                c.AuthorName,
                c.AuthorEmail,
                c.CommitHash,
                c.Branch,
                c.CommitMessage,
                c.LinesAdded,
                c.LinesDeleted
            }), Path.Combine(outputDir, "file-changes.csv"));

            CsvExporter.ExportToCsv(reportData.RecentCommits.Select(c => new
            {
                Date = c.Date.ToString("yyyy-MM-dd HH:mm:ss zzz"),
                c.Sha,
                c.Author,
                c.Email,
                c.Branch,
                c.Message,
                c.IsMerge,
                c.FilesChanged,
                c.Insertions,
                c.Deletions
            }), Path.Combine(outputDir, "commits.csv"));

            CsvExporter.ExportToCsv(reportData.Modules, Path.Combine(outputDir, "modules.csv"));
        }

        private static List<string> ReadList(IConfiguration configuration, string key)
        {
            return configuration.GetSection(key).GetChildren()
                .Select(c => c.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .ToList();
        }

        // Date-only values for --until cover the whole day
        private static bool TryParseDate(string value, bool endOfDay, out DateTimeOffset result)
        {
            if (DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date))
            {
                result = new DateTimeOffset(endOfDay ? date.AddDays(1).AddTicks(-1) : date);
                return true;
            }
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out result);
        }

        // Accepts "--repo <path> --start <branch> [--target <branch>] [--output <dir>] ..." or positional "<repo> <start> [target]"
        private static (Dictionary<string, string> Named, List<string> Positional) ParseArgs(string[] args)
        {
            var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var positional = new List<string>();
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("--", StringComparison.Ordinal))
                {
                    var key = args[i][2..];
                    var takesValue = !Flags.Contains(key) && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
                    named[key] = takesValue ? args[++i] : string.Empty;
                }
                else
                {
                    positional.Add(args[i]);
                }
            }
            return (named, positional);
        }

        private static string? GetArg(Dictionary<string, string> named, List<string> positional, string name, int position)
        {
            if (named.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
            return position >= 0 && position < positional.Count ? positional[position] : null;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage: GitAnalyticsDashboard [--repo <path>] [--start <branch>] [--target <branch>] [--output <dir>]");
            Console.WriteLine("                             [--include <globs>] [--exclude <globs>] [--no-default-excludes]");
            Console.WriteLine("                             [--author <text>] [--since <date>] [--until <date>]");
            Console.WriteLine("       GitAnalyticsDashboard <repo> <start> [target]");
            Console.WriteLine();
            Console.WriteLine("Scans every commit from the starting branch up to the target (default: HEAD), including");
            Console.WriteLine("commits merged in from other branches, and writes HTML, Excel, JSON, CSV and CHANGELOG.md reports.");
            Console.WriteLine("Branches may be local or remote-tracking; tags and commit SHAs are also accepted.");
            Console.WriteLine();
            Console.WriteLine("  --include / --exclude  Comma-separated globs, e.g. \"src/**\" or \"*.Designer.cs,docs/\".");
            Console.WriteLine("                         Excludes add to ReportConfig:ExcludePaths unless --no-default-excludes is set.");
            Console.WriteLine("  --author               Matches part of the author name or email (after .mailmap).");
            Console.WriteLine("  --since / --until      Author date bounds, e.g. 2026-09-01 (--until includes the whole day).");
            Console.WriteLine();
            Console.WriteLine("Missing repository/branch values are prompted for interactively.");
        }
    }
}
