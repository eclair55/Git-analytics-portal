using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GitAnalyticsDashboard.Models;
using LibGit2Sharp;

namespace GitAnalyticsDashboard.Services
{
    public class GitScanningService
    {
        // 1. User input
        public string PromptRepositoryPath()
        {
            while (true)
            {
                Console.WriteLine("Repository path:");
                var input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input))
                {
                    Console.WriteLine("Path cannot be empty.\n");
                    continue;
                }
                var fullPath = Path.GetFullPath(input);
                if (!ValidateRepositoryPath(fullPath))
                {
                    Console.WriteLine("Invalid Git repository path.\n");
                    continue;
                }
                return fullPath;
            }
        }

        public string PromptStartingBranch()
        {
            while (true)
            {
                Console.WriteLine("Starting branch:");
                var branchName = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(branchName))
                {
                    Console.WriteLine("Branch name cannot be empty.\n");
                    continue;
                }
                return branchName.Trim();
            }
        }

        // 2. Git repository validation
        public bool ValidateRepositoryPath(string path)
        {
            return Directory.Exists(path) && Repository.IsValid(path);
        }

        // 3. Starting branch resolution
        public Branch? ResolveStartingBranch(Repository repo, string branchName)
        {
            return repo.Branches[branchName];
        }

        // 4. Commit traversal
        public List<Commit> GetCommitsForScan(Repository repo, Commit startingCommit, Commit latestCommit)
        {
            var filter = new CommitFilter
            {
                IncludeReachableFrom = latestCommit,
                SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time
            };

            var excludeCommits = new List<Commit>();
            foreach (var parent in startingCommit.Parents)
            {
                excludeCommits.Add(parent);
            }
            if (excludeCommits.Any())
            {
                filter.ExcludeReachableFrom = excludeCommits;
            }

            var scanned = new List<Commit>();
            foreach (var commit in repo.Commits.QueryBy(filter))
            {
                if (IsDescendantOf(repo, commit, startingCommit))
                {
                    scanned.Add(commit);
                }
            }

            return scanned;
        }

        public bool IsDescendantOf(Repository repo, Commit commit, Commit potentialAncestor)
        {
            if (commit.Sha == potentialAncestor.Sha)
                return true;

            var divergence = repo.ObjectDatabase.CalculateHistoryDivergence(commit, potentialAncestor);
            return divergence.BehindBy == 0;
        }

        // 5. File change extraction
        public List<FileChangeInfo> ExtractFileChanges(Repository repo, List<Commit> commits, Dictionary<string, List<string>> commitBranchMap)
        {
            var results = new List<FileChangeInfo>();
            var compareOptions = new CompareOptions
            {
                Similarity = SimilarityOptions.Renames
            };

            foreach (var commit in commits)
            {
                var branchList = commitBranchMap.TryGetValue(commit.Sha, out var bList) ? bList : new List<string> { "detached" };
                var branchesString = string.Join(", ", branchList.OrderBy(b => b));

                var parent = commit.Parents.FirstOrDefault();
                var diff = repo.Diff.Compare<Patch>(parent?.Tree, commit.Tree, compareOptions);

                foreach (var change in diff)
                {
                    string changeType = change.Status switch
                    {
                        ChangeKind.Added => "Added",
                        ChangeKind.Deleted => "Deleted",
                        ChangeKind.Modified => "Modified",
                        ChangeKind.Renamed => "Renamed",
                        ChangeKind.Copied => "Copied",
                        ChangeKind.TypeChanged => "Modified",
                        _ => change.Status.ToString()
                    };

                    results.Add(new FileChangeInfo
                    {
                        FilePath = change.Path,
                        FileName = Path.GetFileName(change.Path),
                        ChangeDate = commit.Author.When,
                        AuthorName = commit.Author.Name,
                        AuthorEmail = commit.Author.Email ?? string.Empty,
                        CommitHash = commit.Sha,
                        CommitMessage = commit.MessageShort,
                        Branch = branchesString,
                        ChangeType = changeType
                    });
                }
            }

            return results;
        }

        // 6. Branch detection
        public Dictionary<string, List<string>> GetCommitBranchMap(Repository repo)
        {
            var commitBranchMap = new Dictionary<string, List<string>>();
            foreach (var branch in repo.Branches.Where(b => !b.IsRemote))
            {
                var filter = new CommitFilter { IncludeReachableFrom = branch };
                foreach (var commit in repo.Commits.QueryBy(filter))
                {
                    if (!commitBranchMap.TryGetValue(commit.Sha, out var branchList))
                    {
                        branchList = new List<string>();
                        commitBranchMap[commit.Sha] = branchList;
                    }
                    if (!branchList.Contains(branch.FriendlyName))
                    {
                        branchList.Add(branch.FriendlyName);
                    }
                }
            }
            return commitBranchMap;
        }

        // 7. Result sorting
        public List<FileChangeInfo> SortFileChanges(List<FileChangeInfo> changes)
        {
            return changes.OrderByDescending(c => c.ChangeDate).ToList();
        }

        // 8. Output/export
        public void PrintToConsole(List<FileChangeInfo> changes)
        {
            Console.WriteLine("\nResults:\n");
            Console.WriteLine($"{"Date",-23} {"File Path",-45} {"Author",-20} {"Commit",-12} {"Branch",-25} {"Change",-10}");
            Console.WriteLine(new string('-', 140));

            foreach (var c in changes)
            {
                var dateStr = c.ChangeDate.ToString("yyyy-MM-dd HH:mm:ss");
                var pathStr = c.FilePath.Length > 43 ? "..." + c.FilePath[^40..] : c.FilePath;
                var authorStr = c.AuthorName.Length > 18 ? c.AuthorName[..15] + "..." : c.AuthorName;
                var commitStr = c.CommitHash[..7];
                var branchStr = c.Branch.Length > 23 ? c.Branch[..20] + "..." : c.Branch;
                var changeStr = c.ChangeType;

                Console.WriteLine($"{dateStr,-23} {pathStr,-45} {authorStr,-20} {commitStr,-12} {branchStr,-25} {changeStr,-10}");
            }
        }
    }
}
