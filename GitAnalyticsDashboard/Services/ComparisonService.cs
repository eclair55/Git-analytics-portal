using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GitAnalyticsDashboard.Models;
using GitAnalyticsDashboard.Utilities;
using LibGit2Sharp;

namespace GitAnalyticsDashboard.Services
{
    public class ComparisonOptions
    {
        public PathFilter PathFilter { get; set; } = PathFilter.None;
        public Mailmap Mailmap { get; set; } = Mailmap.Empty;

        /// <summary>Files larger than this (either side) keep their diff summary but no before/after contents.</summary>
        public long MaxFileBytes { get; set; } = 256 * 1024;

        /// <summary>Diffs longer than this are cut, keeping the report size bounded.</summary>
        public int MaxDiffChars { get; set; } = 200_000;
    }

    public class ComparisonService
    {
        private const string TruncatedMarker = "\n\\ Diff truncated: exceeds the configured MaxDiffChars limit\n";

        private readonly CodeChangeAnalyzer _codeChangeAnalyzer;

        public ComparisonService(CodeChangeAnalyzer codeChangeAnalyzer)
        {
            _codeChangeAnalyzer = codeChangeAnalyzer;
        }

        public List<CommitComparison> Analyze(Repository repo, Branch targetBranch, int limit = 100)
        {
            var commits = repo.Commits.QueryBy(new CommitFilter { IncludeReachableFrom = targetBranch }).Take(limit).ToList();
            var commitBranchMap = commits.ToDictionary(c => c.Sha, c => new List<string> { targetBranch.FriendlyName });
            return Analyze(repo, commits, commitBranchMap);
        }

        public List<CommitComparison> Analyze(Repository repo, List<Commit> commits, Dictionary<string, List<string>> commitBranchMap, ComparisonOptions? options = null)
        {
            options ??= new ComparisonOptions();
            var compareOptions = new CompareOptions { Similarity = SimilarityOptions.Renames };
            var comparisons = new List<CommitComparison>();

            foreach (var commit in commits)
            {
                var branchList = commitBranchMap.TryGetValue(commit.Sha, out var bList) ? bList : new List<string> { "detached" };
                var branchesString = string.Join(", ", branchList.OrderBy(b => b));

                var comparison = new CommitComparison
                {
                    Sha = commit.Sha,
                    Author = options.Mailmap.Map(commit.Author).Name,
                    Date = commit.Author.When,
                    Message = commit.MessageShort,
                    Branch = branchesString
                };

                var parent = commit.Parents.FirstOrDefault();
                var changes = repo.Diff.Compare<Patch>(parent?.Tree, commit.Tree, compareOptions);

                foreach (var change in changes)
                {
                    if (!options.PathFilter.IsIncluded(change.Path))
                        continue;

                    var fileComp = new FileComparison
                    {
                        Path = change.Path,
                        ChangeType = change.Status.ToString(),
                        IsBinary = change.IsBinaryComparison
                    };

                    var oldBlob = parent != null && (change.Status == ChangeKind.Modified || change.Status == ChangeKind.Deleted || change.Status == ChangeKind.Renamed)
                        ? GetBlob(parent, change.OldPath ?? change.Path)
                        : null;
                    var newBlob = change.Status != ChangeKind.Deleted ? GetBlob(commit, change.Path) : null;

                    fileComp.IsBinary |= (oldBlob?.IsBinary ?? false) || (newBlob?.IsBinary ?? false);
                    var tooLarge = (oldBlob?.Size ?? 0) > options.MaxFileBytes || (newBlob?.Size ?? 0) > options.MaxFileBytes;

                    if (fileComp.IsBinary)
                    {
                        fileComp.Summary = "Binary file; contents not shown.";
                        comparison.FileChanges.Add(fileComp);
                        continue;
                    }

                    fileComp.Diff = change.Patch;
                    if (fileComp.Diff.Length > options.MaxDiffChars)
                    {
                        fileComp.Diff = fileComp.Diff[..options.MaxDiffChars] + TruncatedMarker;
                        fileComp.IsTruncated = true;
                    }

                    if (tooLarge)
                    {
                        fileComp.IsTruncated = true;
                        fileComp.Summary = $"File exceeds {options.MaxFileBytes / 1024} KB; before/after contents not captured.";
                        comparison.FileChanges.Add(fileComp);
                        continue;
                    }

                    fileComp.BeforeContent = oldBlob?.GetContentText() ?? string.Empty;
                    fileComp.AfterContent = newBlob?.GetContentText() ?? string.Empty;

                    fileComp.DetectedChanges = _codeChangeAnalyzer.Analyze(fileComp.Path, fileComp.BeforeContent, fileComp.AfterContent);
                    fileComp.Summary = GenerateFileSummary(fileComp);

                    comparison.FileChanges.Add(fileComp);
                }

                comparison.Summary = GenerateCommitSummary(comparison);
                comparisons.Add(comparison);
            }

            return comparisons;
        }

        private static Blob? GetBlob(Commit commit, string path)
        {
            var entry = commit[path];
            return entry != null && entry.TargetType == TreeEntryTargetType.Blob ? (Blob)entry.Target : null;
        }

        private string GenerateFileSummary(FileComparison file)
        {
            if (!file.DetectedChanges.Any()) return "No specific code structures detected.";
            return string.Join(", ", file.DetectedChanges);
        }

        private string GenerateCommitSummary(CommitComparison commit)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Files Changed: {commit.FilesChangedCount}");

            var added = commit.FileChanges.Where(f => f.ChangeType == "Added").Select(f => f.Path).ToList();
            if (added.Any()) sb.AppendLine($"Added: {string.Join(", ", added)}");

            var modified = commit.FileChanges.Where(f => f.ChangeType == "Modified").Select(f => f.Path).ToList();
            if (modified.Any()) sb.AppendLine($"Modified: {string.Join(", ", modified)}");

            var deleted = commit.FileChanges.Where(f => f.ChangeType == "Deleted").Select(f => f.Path).ToList();
            if (deleted.Any()) sb.AppendLine($"Deleted: {string.Join(", ", deleted)}");

            var renamed = commit.FileChanges.Where(f => f.ChangeType == "Renamed").Select(f => f.Path).ToList();
            if (renamed.Any()) sb.AppendLine($"Renamed: {string.Join(", ", renamed)}");

            return sb.ToString();
        }
    }
}
