using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GitAnalyticsDashboard.Models;
using GitAnalyticsDashboard.Utilities;
using LibGit2Sharp;

namespace GitAnalyticsDashboard.Services
{
    public class ComparisonService
    {
        private readonly CodeChangeAnalyzer _codeChangeAnalyzer;

        public ComparisonService(CodeChangeAnalyzer codeChangeAnalyzer)
        {
            _codeChangeAnalyzer = codeChangeAnalyzer;
        }

        public List<CommitComparison> Analyze(Repository repo, Branch targetBranch, int limit = 100)
        {
            var comparisons = new List<CommitComparison>();
            var branchName = targetBranch.FriendlyName;

            var commits = repo.Commits.QueryBy(new CommitFilter { IncludeReachableFrom = targetBranch }).Take(limit);

            foreach (var commit in commits)
            {
                var comparison = new CommitComparison
                {
                    Sha = commit.Sha,
                    Author = commit.Author.Name,
                    Date = commit.Author.When,
                    Message = commit.MessageShort,
                    Branch = branchName
                };

                var parent = commit.Parents.FirstOrDefault();
                var changes = repo.Diff.Compare<Patch>(parent?.Tree, commit.Tree);

                foreach (var change in changes)
                {
                    var fileComp = new FileComparison
                    {
                        Path = change.Path,
                        ChangeType = change.Status.ToString(),
                        Diff = change.Patch
                    };

                    // Get Before Content
                    if (parent != null && (change.Status == ChangeKind.Modified || change.Status == ChangeKind.Deleted || change.Status == ChangeKind.Renamed))
                    {
                        var oldEntry = parent[change.OldPath];
                        if (oldEntry != null && oldEntry.TargetType == TreeEntryTargetType.Blob)
                        {
                            var blob = (Blob)oldEntry.Target;
                            fileComp.BeforeContent = blob.GetContentText();
                        }
                    }

                    // Get After Content
                    if (change.Status != ChangeKind.Deleted)
                    {
                        var newEntry = commit[change.Path];
                        if (newEntry != null && newEntry.TargetType == TreeEntryTargetType.Blob)
                        {
                            var blob = (Blob)newEntry.Target;
                            fileComp.AfterContent = blob.GetContentText();
                        }
                    }

                    fileComp.DetectedChanges = _codeChangeAnalyzer.Analyze(fileComp.Path, fileComp.BeforeContent, fileComp.AfterContent);
                    fileComp.Summary = GenerateFileSummary(fileComp);

                    comparison.FileChanges.Add(fileComp);
                }

                comparison.Summary = GenerateCommitSummary(comparison);
                comparisons.Add(comparison);
            }

            return comparisons;
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

            return sb.ToString();
        }
    }
}
