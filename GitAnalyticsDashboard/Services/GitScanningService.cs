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
        private const string DetachedLabel = "detached";
        private const string RemotePrefix = "refs/remotes/";

        private static readonly CompareOptions DiffOptions = new CompareOptions
        {
            Similarity = SimilarityOptions.Renames
        };

        // 1. User input
        public string? PromptRepositoryPath()
        {
            while (true)
            {
                Console.WriteLine("Repository path:");
                var input = Console.ReadLine();
                if (input == null)
                {
                    return null; // Input stream closed
                }
                if (string.IsNullOrWhiteSpace(input))
                {
                    Console.WriteLine("Path cannot be empty.\n");
                    continue;
                }
                var fullPath = NormalizePath(input);
                if (fullPath == null || !ValidateRepositoryPath(fullPath))
                {
                    Console.WriteLine("Invalid Git repository path.\n");
                    continue;
                }
                return fullPath;
            }
        }

        /// <summary>
        /// Prompts until the input resolves to a commit. When <paramref name="allowEmptyForHead"/> is set,
        /// an empty answer selects the current HEAD. Returns null if the input stream is closed.
        /// </summary>
        public ResolvedReference? PromptReference(Repository repo, string label, bool allowEmptyForHead)
        {
            while (true)
            {
                Console.WriteLine(label);
                var input = Console.ReadLine();
                if (input == null)
                {
                    return null;
                }
                if (string.IsNullOrWhiteSpace(input))
                {
                    if (allowEmptyForHead)
                    {
                        return ResolveHead(repo);
                    }
                    Console.WriteLine("Branch name cannot be empty.\n");
                    continue;
                }

                var resolved = ResolveReference(repo, input);
                if (resolved != null)
                {
                    return resolved;
                }
                PrintBranchNotFound(repo, input.Trim());
            }
        }

        public void PrintBranchNotFound(Repository repo, string name)
        {
            Console.WriteLine($"Branch not found: {name}");
            var suggestions = SuggestBranches(repo, name);
            if (suggestions.Any())
            {
                Console.WriteLine($"Available branches: {string.Join(", ", suggestions)}");
            }
            Console.WriteLine();
        }

        public List<string> SuggestBranches(Repository repo, string name, int max = 10)
        {
            var names = GetLabelBranches(repo).Select(b => b.FriendlyName).OrderBy(n => n).ToList();
            var similar = names.Where(n => n.Contains(name, StringComparison.OrdinalIgnoreCase)).ToList();
            return (similar.Any() ? similar : names).Take(max).ToList();
        }

        public static string? NormalizePath(string input)
        {
            try
            {
                // Paths pasted from Explorer are often wrapped in quotes
                return Path.GetFullPath(input.Trim().Trim('"'));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return null;
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
            return ResolveBranch(repo, branchName);
        }

        public Branch? ResolveBranch(Repository repo, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            name = name.Trim();
            var branch = repo.Branches[name];
            if (branch != null)
                return branch;

            // Fresh clones usually only have remote-tracking copies of feature/release branches
            return repo.Branches
                .Where(b => b.IsRemote && StripRemote(b) == name)
                .OrderBy(b => GetRemoteName(b) == "origin" ? 0 : 1)
                .FirstOrDefault();
        }

        /// <summary>Resolves a branch (local or remote), tag, SHA or revision expression such as HEAD~3.</summary>
        public ResolvedReference? ResolveReference(Repository repo, string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
                return null;

            reference = reference.Trim();
            var branch = ResolveBranch(repo, reference);
            if (branch?.Tip != null)
                return new ResolvedReference(branch.FriendlyName, branch.Tip, branch);

            try
            {
                // ^{commit} peels annotated tags down to the commit they point at
                if (repo.Lookup($"{reference}^{{commit}}") is Commit commit)
                    return new ResolvedReference(reference, commit, null);
            }
            catch (LibGit2SharpException)
            {
                // Invalid or ambiguous revision expression
            }

            return null;
        }

        public ResolvedReference? ResolveHead(Repository repo)
        {
            var tip = repo.Head.Tip;
            if (tip == null)
                return null;

            return repo.Info.IsHeadDetached
                ? new ResolvedReference("HEAD", tip, null)
                : new ResolvedReference(repo.Head.FriendlyName, tip, repo.Head);
        }

        /// <summary>Runs the full scan: commit traversal, branch detection and file change extraction, diffing each commit once.</summary>
        public ScanResult Scan(Repository repo, ResolvedReference start, ResolvedReference target, ScanOptions? options = null)
        {
            options ??= new ScanOptions();
            var rangeCommits = GetCommitsForScan(repo, start.Commit, target.Commit);

            // Author/date filters work on the canonical (mailmapped) identity
            var commits = rangeCommits.Where(c =>
            {
                var (name, email) = options.Mailmap.Map(c.Author);
                return options.IsAuthorIncluded(name, email, c.Author.When);
            }).ToList();

            var commitBranchMap = GetCommitBranchMap(repo, commits, start.Commit.Parents);

            var result = new ScanResult
            {
                Start = start,
                Target = target,
                StartIsAncestorOfTarget = IsDescendantOf(repo, target.Commit, start.Commit),
                CommitBranchMap = commitBranchMap,
                Options = options
            };

            var fileChanges = new List<FileChangeInfo>();
            foreach (var commit in commits)
            {
                var analysis = AnalyzeCommit(repo, commit, commitBranchMap, options);
                result.ExcludedFileChanges += analysis.ExcludedChanges;

                // A commit that only touched excluded paths (e.g. bin/ or .vs/) is noise for this scan
                if (!analysis.Info.IsMerge && analysis.Changes.Count == 0 && analysis.ExcludedChanges > 0)
                    continue;

                result.Commits.Add(commit);
                result.CommitInfos.Add(analysis.Info);
                fileChanges.AddRange(analysis.Changes);
            }

            result.FilteredOutCommits = rangeCommits.Count - result.Commits.Count;
            result.FileChanges = SortFileChanges(fileChanges);

            return result;
        }

        // 4. Commit traversal
        public List<Commit> GetCommitsForScan(Repository repo, Commit startingCommit, Commit latestCommit)
        {
            // Everything reachable from the latest commit that was not already history before the starting commit:
            // the starting commit, its descendants, and commits merged in from branches that forked earlier.
            var filter = new CommitFilter
            {
                IncludeReachableFrom = latestCommit,
                SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time
            };

            var parents = startingCommit.Parents.ToList();
            if (parents.Any())
            {
                filter.ExcludeReachableFrom = parents;
            }

            return repo.Commits.QueryBy(filter).ToList();
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
            var options = new ScanOptions();
            return commits.SelectMany(c => AnalyzeCommit(repo, c, commitBranchMap, options).Changes).ToList();
        }

        private (CommitInfo Info, List<FileChangeInfo> Changes, int ExcludedChanges) AnalyzeCommit(
            Repository repo, Commit commit, Dictionary<string, List<string>> commitBranchMap, ScanOptions options)
        {
            var branchesString = FormatBranches(commitBranchMap, commit.Sha);
            var parents = commit.Parents.ToList();
            var changes = new List<FileChangeInfo>();
            var excluded = 0;
            var (authorName, authorEmail) = options.Mailmap.Map(commit.Author);

            var info = new CommitInfo
            {
                Sha = commit.Sha,
                Author = authorName,
                Email = authorEmail,
                Date = commit.Author.When,
                Message = commit.MessageShort,
                Branch = branchesString,
                IsMerge = parents.Count > 1
            };

            // The commits a merge brings in are scanned individually, so diffing the merge against its
            // first parent would report the same file changes twice.
            if (info.IsMerge)
                return (info, changes, excluded);

            var patch = repo.Diff.Compare<Patch>(parents.FirstOrDefault()?.Tree, commit.Tree, DiffOptions);
            foreach (var change in patch)
            {
                if (!options.PathFilter.IsIncluded(change.Path))
                {
                    excluded++;
                    continue;
                }

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

                changes.Add(new FileChangeInfo
                {
                    FilePath = change.Path,
                    FileName = Path.GetFileName(change.Path),
                    OldPath = change.Status == ChangeKind.Renamed || change.Status == ChangeKind.Copied ? change.OldPath : string.Empty,
                    ChangeDate = commit.Author.When,
                    AuthorName = authorName,
                    AuthorEmail = authorEmail,
                    CommitHash = commit.Sha,
                    CommitMessage = commit.MessageShort,
                    Branch = branchesString,
                    ChangeType = changeType,
                    LinesAdded = change.LinesAdded,
                    LinesDeleted = change.LinesDeleted
                });
            }

            // Totals cover only the files that passed the path filter
            info.FilesChanged = changes.Count;
            info.Insertions = changes.Sum(c => c.LinesAdded);
            info.Deletions = changes.Sum(c => c.LinesDeleted);

            return (info, changes, excluded);
        }

        private static string FormatBranches(Dictionary<string, List<string>> commitBranchMap, string sha)
        {
            var branchList = commitBranchMap.TryGetValue(sha, out var bList) ? bList : new List<string> { DetachedLabel };
            return string.Join(", ", branchList.OrderBy(b => b));
        }

        // 6. Branch detection
        public Dictionary<string, List<string>> GetCommitBranchMap(Repository repo)
        {
            var commitBranchMap = new Dictionary<string, List<string>>();
            foreach (var branch in GetLabelBranches(repo))
            {
                var filter = new CommitFilter { IncludeReachableFrom = branch };
                foreach (var commit in repo.Commits.QueryBy(filter))
                {
                    AddBranch(commitBranchMap, commit.Sha, branch.FriendlyName);
                }
            }
            return commitBranchMap;
        }

        /// <summary>
        /// Branch map limited to <paramref name="scope"/>. Walks stop at <paramref name="excludeReachableFrom"/>
        /// so branch detection only covers the scan range instead of every branch's full history.
        /// </summary>
        public Dictionary<string, List<string>> GetCommitBranchMap(Repository repo, IEnumerable<Commit> scope, IEnumerable<Commit>? excludeReachableFrom = null)
        {
            var inScope = new HashSet<string>(scope.Select(c => c.Sha));
            var exclude = excludeReachableFrom?.ToList() ?? new List<Commit>();
            var commitBranchMap = new Dictionary<string, List<string>>();

            foreach (var branch in GetLabelBranches(repo))
            {
                var filter = new CommitFilter { IncludeReachableFrom = branch };
                if (exclude.Any())
                {
                    filter.ExcludeReachableFrom = exclude;
                }

                foreach (var commit in repo.Commits.QueryBy(filter))
                {
                    if (inScope.Contains(commit.Sha))
                    {
                        AddBranch(commitBranchMap, commit.Sha, branch.FriendlyName);
                    }
                }
            }
            return commitBranchMap;
        }

        /// <summary>
        /// Local branches plus remote-tracking branches that have no local counterpart
        /// (so a clone that never checked out "feature/x" still labels its commits).
        /// </summary>
        public List<Branch> GetLabelBranches(Repository repo)
        {
            var local = repo.Branches.Where(b => !b.IsRemote && b.Tip != null).ToList();
            var localNames = new HashSet<string>(local.Select(b => b.FriendlyName));

            var remote = repo.Branches.Where(b => b.IsRemote
                && b.Tip != null
                && !b.CanonicalName.EndsWith("/HEAD", StringComparison.Ordinal)
                && !localNames.Contains(StripRemote(b)));

            return local.Concat(remote).ToList();
        }

        private static void AddBranch(Dictionary<string, List<string>> commitBranchMap, string sha, string branchName)
        {
            if (!commitBranchMap.TryGetValue(sha, out var branchList))
            {
                branchList = new List<string>();
                commitBranchMap[sha] = branchList;
            }
            if (!branchList.Contains(branchName))
            {
                branchList.Add(branchName);
            }
        }

        private static string GetRemoteName(Branch remoteBranch)
        {
            var name = remoteBranch.CanonicalName.StartsWith(RemotePrefix, StringComparison.Ordinal)
                ? remoteBranch.CanonicalName[RemotePrefix.Length..]
                : remoteBranch.FriendlyName;
            var slash = name.IndexOf('/');
            return slash > 0 ? name[..slash] : name;
        }

        // "origin/feature/x" -> "feature/x"
        private static string StripRemote(Branch remoteBranch)
        {
            var remoteName = GetRemoteName(remoteBranch);
            return remoteBranch.FriendlyName.StartsWith(remoteName + "/", StringComparison.Ordinal)
                ? remoteBranch.FriendlyName[(remoteName.Length + 1)..]
                : remoteBranch.FriendlyName;
        }

        // 7. Result sorting
        public List<FileChangeInfo> SortFileChanges(List<FileChangeInfo> changes)
        {
            return changes
                .OrderByDescending(c => c.ChangeDate)
                .ThenBy(c => c.CommitHash, StringComparer.Ordinal)
                .ThenBy(c => c.FilePath, StringComparer.Ordinal)
                .ToList();
        }

        // 8. Output/export
        public void PrintSummary(ScanResult scan)
        {
            var merges = scan.CommitInfos.Count(c => c.IsMerge);
            Console.WriteLine("\nCompleted.\n");
            Console.WriteLine($"Scan range: {scan.Start.Name} ({ShortSha(scan.Start.Commit.Sha)}) -> {scan.Target.Name} ({ShortSha(scan.Target.Commit.Sha)})");
            Console.WriteLine($"Total commits analyzed: {scan.Commits.Count} ({merges} merge commits)");
            Console.WriteLine($"Total file changes: {scan.FileChanges.Count} across {scan.FileChanges.Select(c => c.FilePath).Distinct().Count()} files");
            Console.WriteLine($"Lines: +{scan.FileChanges.Sum(c => c.LinesAdded)} / -{scan.FileChanges.Sum(c => c.LinesDeleted)}");

            var filters = scan.Options.Describe();
            if (filters.Any())
            {
                Console.WriteLine($"Filters: {string.Join("; ", filters)}");
                Console.WriteLine($"Filtered out: {scan.FilteredOutCommits} commits, {scan.ExcludedFileChanges} file changes");
            }
        }

        public void PrintToConsole(List<FileChangeInfo> changes, int maxRows = int.MaxValue)
        {
            Console.WriteLine("\nResults:\n");
            Console.WriteLine($"{"Date",-20} {"File Path",-45} {"Author",-20} {"Commit",-8} {"Branch",-25} {"Change",-9} {"+/-",-12}");
            Console.WriteLine(new string('-', 145));

            foreach (var c in changes.Take(maxRows))
            {
                var dateStr = c.ChangeDate.ToString("yyyy-MM-dd HH:mm:ss");
                var pathStr = c.FilePath.Length > 43 ? "..." + c.FilePath[^40..] : c.FilePath;
                var authorStr = c.AuthorName.Length > 18 ? c.AuthorName[..15] + "..." : c.AuthorName;
                var commitStr = ShortSha(c.CommitHash);
                var branchStr = c.Branch.Length > 23 ? c.Branch[..20] + "..." : c.Branch;
                var linesStr = $"+{c.LinesAdded}/-{c.LinesDeleted}";

                Console.WriteLine($"{dateStr,-20} {pathStr,-45} {authorStr,-20} {commitStr,-8} {branchStr,-25} {c.ChangeType,-9} {linesStr,-12}");
            }

            if (changes.Count > maxRows)
            {
                Console.WriteLine($"... {changes.Count - maxRows} more file changes not shown (see the CSV/Excel reports for the full list).");
            }
        }

        private static string ShortSha(string sha) => sha.Length > 7 ? sha[..7] : sha;
    }
}
