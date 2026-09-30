using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GitAnalyticsDashboard.Models;
using GitAnalyticsDashboard.Services;
using LibGit2Sharp;
using Xunit;

namespace GitAnalyticsDashboard.Tests
{
    public partial class GitScanningServiceTests : IDisposable
    {
        private readonly string _tempRepoPath;
        private readonly GitScanningService _scanningService;

        public GitScanningServiceTests()
        {
            _scanningService = new GitScanningService();
            _tempRepoPath = Path.Combine(Path.GetTempPath(), "GitAnalyticsTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRepoPath);
            Repository.Init(_tempRepoPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempRepoPath))
            {
                foreach (var file in Directory.EnumerateFiles(_tempRepoPath, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                Directory.Delete(_tempRepoPath, true);
            }
        }

        private void CommitFile(Repository repo, string relativePath, string content, string commitMessage, string authorName = "John Doe", string authorEmail = "john@example.com", DateTimeOffset? date = null)
        {
            var fullPath = Path.Combine(_tempRepoPath, relativePath);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(fullPath, content);
            Commands.Stage(repo, relativePath);

            var sigDate = date ?? DateTimeOffset.Now;
            var signature = new Signature(authorName, authorEmail, sigDate);
            repo.Commit(commitMessage, signature, signature);
        }

        private static ReportOrchestrator CreateOrchestrator()
        {
            return new ReportOrchestrator(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<ReportOrchestrator>.Instance,
                new BranchAnalyzer(), new CommitAnalyzer(), new DeveloperAnalyzer(), new FileAnalyzer(),
                new StatisticsService(), new ComparisonService(new CodeChangeAnalyzer()),
                new ModuleAnalyzer(), new ReleaseNotesGenerator());
        }

        private Commit CommitFiles(Repository repo, string commitMessage, params (string Path, string Content)[] files)
        {
            foreach (var (relativePath, content) in files)
            {
                var fullPath = Path.Combine(_tempRepoPath, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllText(fullPath, content);
                Commands.Stage(repo, relativePath);
            }
            var signature = new Signature("John Doe", "john@example.com", DateTimeOffset.Now);
            return repo.Commit(commitMessage, signature, signature);
        }

        private ScanResult ScanFromRelease(Repository repo, ScanOptions? options = null)
        {
            return _scanningService.Scan(repo, _scanningService.ResolveReference(repo, "release/1.0")!, _scanningService.ResolveHead(repo)!, options);
        }

        private void DeleteFile(Repository repo, string relativePath, string commitMessage)
        {
            var fullPath = Path.Combine(_tempRepoPath, relativePath);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
            Commands.Stage(repo, relativePath);
            var signature = new Signature("John Doe", "john@example.com", DateTimeOffset.Now);
            repo.Commit(commitMessage, signature, signature);
        }

        private void RenameFile(Repository repo, string oldRelativePath, string newRelativePath, string commitMessage)
        {
            var oldFullPath = Path.Combine(_tempRepoPath, oldRelativePath);
            var newFullPath = Path.Combine(_tempRepoPath, newRelativePath);
            var dir = Path.GetDirectoryName(newFullPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.Move(oldFullPath, newFullPath);
            Commands.Stage(repo, oldRelativePath);
            Commands.Stage(repo, newRelativePath);
            var signature = new Signature("John Doe", "john@example.com", DateTimeOffset.Now);
            repo.Commit(commitMessage, signature, signature);
        }

        [Fact]
        public void Test_1_ValidRepositoryPath()
        {
            Assert.True(_scanningService.ValidateRepositoryPath(_tempRepoPath));
        }

        [Fact]
        public void Test_2_InvalidRepositoryPath()
        {
            var invalidPath = Path.Combine(_tempRepoPath, "invalid_subdir_12345");
            Assert.False(_scanningService.ValidateRepositoryPath(invalidPath));
        }

        [Fact]
        public void Test_3_ValidStartingBranch()
        {
            using (var repo = new Repository(_tempRepoPath))
            {
                CommitFile(repo, "test.txt", "hello", "Initial commit");
                var branch = _scanningService.ResolveStartingBranch(repo, "master");
                // Git Init creates master or main
                if (branch == null)
                {
                    branch = _scanningService.ResolveStartingBranch(repo, "main");
                }
                Assert.NotNull(branch);
            }
        }

        [Fact]
        public void Test_4_InvalidStartingBranch()
        {
            using (var repo = new Repository(_tempRepoPath))
            {
                CommitFile(repo, "test.txt", "hello", "Initial commit");
                var branch = _scanningService.ResolveStartingBranch(repo, "non_existent_branch_name");
                Assert.Null(branch);
            }
        }

        [Fact]
        public void Test_5_RepositoryWithCommitsAfterStartingBranch()
        {
            using (var repo = new Repository(_tempRepoPath))
            {
                CommitFile(repo, "test1.txt", "v1", "Commit 1");

                // Create starting branch at Commit 1
                var startingBranch = repo.CreateBranch("release/1.0");

                // Commit 2 (after starting branch)
                CommitFile(repo, "test2.txt", "v2", "Commit 2");

                var startingCommit = startingBranch.Tip;
                var latestCommit = repo.Head.Tip;

                var scannedCommits = _scanningService.GetCommitsForScan(repo, startingCommit, latestCommit);

                // Should find Commit 2 (and start from release/1.0 itself, so 2 commits in total)
                Assert.Equal(2, scannedCommits.Count);
                Assert.Contains(scannedCommits, c => c.MessageShort == "Commit 2");
                Assert.Contains(scannedCommits, c => c.MessageShort == "Commit 1");
            }
        }

        [Fact]
        public void Test_6_MultipleFilesChangedInOneCommit()
        {
            using (var repo = new Repository(_tempRepoPath))
            {
                CommitFile(repo, "test1.txt", "v1", "Initial commit");
                var startingBranch = repo.CreateBranch("release/1.0");

                // Commit changing multiple files
                var fullPathA = Path.Combine(_tempRepoPath, "fileA.txt");
                var fullPathB = Path.Combine(_tempRepoPath, "fileB.txt");
                File.WriteAllText(fullPathA, "content A");
                File.WriteAllText(fullPathB, "content B");
                Commands.Stage(repo, "fileA.txt");
                Commands.Stage(repo, "fileB.txt");
                var signature = new Signature("John Doe", "john@example.com", DateTimeOffset.Now);
                var commit2 = repo.Commit("Commit 2 changing multiple files", signature, signature);

                var scannedCommits = _scanningService.GetCommitsForScan(repo, startingBranch.Tip, repo.Head.Tip);
                var commitBranchMap = _scanningService.GetCommitBranchMap(repo);
                var fileChanges = _scanningService.ExtractFileChanges(repo, scannedCommits, commitBranchMap);

                var commit2Changes = fileChanges.Where(c => c.CommitHash == commit2.Sha).ToList();
                Assert.Equal(2, commit2Changes.Count);
                Assert.Contains(commit2Changes, c => c.FilePath == "fileA.txt");
                Assert.Contains(commit2Changes, c => c.FilePath == "fileB.txt");
            }
        }

        [Fact]
        public void Test_7_AddedFile()
        {
            using (var repo = new Repository(_tempRepoPath))
            {
                CommitFile(repo, "test1.txt", "v1", "Initial commit");
                var startingBranch = repo.CreateBranch("release/1.0");

                CommitFile(repo, "added_file.txt", "new file content", "Commit 2 added file");

                var scannedCommits = _scanningService.GetCommitsForScan(repo, startingBranch.Tip, repo.Head.Tip);
                var commitBranchMap = _scanningService.GetCommitBranchMap(repo);
                var fileChanges = _scanningService.ExtractFileChanges(repo, scannedCommits, commitBranchMap);

                var addedChange = fileChanges.FirstOrDefault(c => c.FilePath == "added_file.txt");
                Assert.NotNull(addedChange);
                Assert.Equal("Added", addedChange.ChangeType);
            }
        }

        [Fact]
        public void Test_8_ModifiedFile()
        {
            using (var repo = new Repository(_tempRepoPath))
            {
                CommitFile(repo, "modify_me.txt", "original content", "Initial commit");
                var startingBranch = repo.CreateBranch("release/1.0");

                CommitFile(repo, "modify_me.txt", "modified content here", "Commit 2 modified file");

                var scannedCommits = _scanningService.GetCommitsForScan(repo, startingBranch.Tip, repo.Head.Tip);
                var commitBranchMap = _scanningService.GetCommitBranchMap(repo);
                var fileChanges = _scanningService.ExtractFileChanges(repo, scannedCommits, commitBranchMap);

                var modifiedChange = fileChanges.FirstOrDefault(c => c.CommitMessage == "Commit 2 modified file" && c.FilePath == "modify_me.txt");
                Assert.NotNull(modifiedChange);
                Assert.Equal("Modified", modifiedChange.ChangeType);
            }
        }

        [Fact]
        public void Test_9_DeletedFile()
        {
            using (var repo = new Repository(_tempRepoPath))
            {
                CommitFile(repo, "delete_me.txt", "some content", "Initial commit");
                var startingBranch = repo.CreateBranch("release/1.0");

                DeleteFile(repo, "delete_me.txt", "Commit 2 deleted file");

                var scannedCommits = _scanningService.GetCommitsForScan(repo, startingBranch.Tip, repo.Head.Tip);
                var commitBranchMap = _scanningService.GetCommitBranchMap(repo);
                var fileChanges = _scanningService.ExtractFileChanges(repo, scannedCommits, commitBranchMap);

                var deletedChange = fileChanges.FirstOrDefault(c => c.CommitMessage == "Commit 2 deleted file" && c.FilePath == "delete_me.txt");
                Assert.NotNull(deletedChange);
                Assert.Equal("Deleted", deletedChange.ChangeType);
            }
        }

        [Fact]
        public void Test_10_RenamedFile()
        {
            using (var repo = new Repository(_tempRepoPath))
            {
                CommitFile(repo, "old_name.txt", "this is some file content that needs to be substantial enough to detect rename", "Initial commit");
                var startingBranch = repo.CreateBranch("release/1.0");

                RenameFile(repo, "old_name.txt", "new_name.txt", "Commit 2 renamed file");

                var scannedCommits = _scanningService.GetCommitsForScan(repo, startingBranch.Tip, repo.Head.Tip);
                var commitBranchMap = _scanningService.GetCommitBranchMap(repo);
                var fileChanges = _scanningService.ExtractFileChanges(repo, scannedCommits, commitBranchMap);

                var renameChange = fileChanges.FirstOrDefault(c => c.CommitMessage == "Commit 2 renamed file" && c.FilePath == "new_name.txt");
                Assert.NotNull(renameChange);
                Assert.Equal("Renamed", renameChange.ChangeType);
            }
        }

        [Fact]
        public void Test_11_MergeCommit()
        {
            using (var repo = new Repository(_tempRepoPath))
            {
                CommitFile(repo, "base.txt", "base", "Initial commit");
                var startingBranch = repo.CreateBranch("release/1.0");

                // branch feature
                var featureBranch = repo.CreateBranch("feature/test");
                Commands.Checkout(repo, featureBranch);
                CommitFile(repo, "feature_file.txt", "feature content", "Feature commit");

                // checkout master/main again
                var mainBranch = repo.Branches["master"] ?? repo.Branches["main"];
                Commands.Checkout(repo, mainBranch);
                CommitFile(repo, "main_file.txt", "main content", "Main commit");

                // merge feature branch
                var signature = new Signature("John Doe", "john@example.com", DateTimeOffset.Now);
                repo.Merge(featureBranch, signature, new MergeOptions());

                var scannedCommits = _scanningService.GetCommitsForScan(repo, startingBranch.Tip, repo.Head.Tip);
                var commitBranchMap = _scanningService.GetCommitBranchMap(repo);
                var fileChanges = _scanningService.ExtractFileChanges(repo, scannedCommits, commitBranchMap);

                Assert.NotEmpty(scannedCommits);
                Assert.NotEmpty(fileChanges);
            }
        }

        [Fact]
        public void Test_12_MultipleBranchesContainingSameCommit()
        {
            using (var repo = new Repository(_tempRepoPath))
            {
                CommitFile(repo, "test.txt", "v1", "Commit 1");

                var branch1 = repo.CreateBranch("branch/one");
                var branch2 = repo.CreateBranch("branch/two");

                var commitBranchMap = _scanningService.GetCommitBranchMap(repo);
                var commit1Branches = commitBranchMap[repo.Head.Tip.Sha];

                Assert.Contains("branch/one", commit1Branches);
                Assert.Contains("branch/two", commit1Branches);
            }
        }

        [Fact]
        public void Test_13_ResultsSortedByChangeDateDescending()
        {
            var list = new List<FileChangeInfo>
            {
                new FileChangeInfo { FilePath = "old.txt", ChangeDate = DateTimeOffset.Now.AddDays(-5) },
                new FileChangeInfo { FilePath = "new.txt", ChangeDate = DateTimeOffset.Now.AddDays(-1) },
                new FileChangeInfo { FilePath = "mid.txt", ChangeDate = DateTimeOffset.Now.AddDays(-3) }
            };

            var sorted = _scanningService.SortFileChanges(list);

            Assert.Equal("new.txt", sorted[0].FilePath);
            Assert.Equal("mid.txt", sorted[1].FilePath);
            Assert.Equal("old.txt", sorted[2].FilePath);
        }

        [Fact]
        public void Test_14_NoHistoryBeforeStartingPointIncluded()
        {
            using (var repo = new Repository(_tempRepoPath))
            {
                CommitFile(repo, "before1.txt", "content", "Commit Before 1");
                CommitFile(repo, "before2.txt", "content", "Commit Before 2");

                var startingBranch = repo.CreateBranch("release/1.0");

                CommitFile(repo, "after1.txt", "content", "Commit After 1");

                var scannedCommits = _scanningService.GetCommitsForScan(repo, startingBranch.Tip, repo.Head.Tip);

                // Commits Before 1 and Before 2 are history before starting branch release/1.0, and they should NOT be included!
                // Only starting branch tip (Commit Before 2) and Commit After 1 should be included.
                Assert.Contains(scannedCommits, c => c.MessageShort == "Commit After 1");
                Assert.Contains(scannedCommits, c => c.MessageShort == "Commit Before 2"); // Starting branch tip is included as the start point of the scan
                Assert.DoesNotContain(scannedCommits, c => c.MessageShort == "Commit Before 1"); // Unrelated/ancestral history before starting branch's parent
            }
        }

        [Fact]
        public void Test_15_FeatureBranchForkedBeforeStartIsScannedOnceMerged()
        {
            using (var repo = new Repository(_tempRepoPath))
            {
                CommitFile(repo, "base.txt", "base", "Initial commit");
                var mainName = repo.Head.FriendlyName;

                // Feature branch forks before the starting point...
                var featureBranch = repo.CreateBranch("feature/early");
                CommitFile(repo, "release.txt", "release", "Release prep");
                repo.CreateBranch("release/1.0");

                Commands.Checkout(repo, featureBranch);
                CommitFile(repo, "feature_file.txt", "feature content", "Feature commit");

                // ...and is merged after it
                Commands.Checkout(repo, repo.Branches[mainName]);
                var signature = new Signature("John Doe", "john@example.com", DateTimeOffset.Now);
                repo.Merge(repo.Branches["feature/early"], signature, new MergeOptions());

                var scan = _scanningService.Scan(repo, _scanningService.ResolveReference(repo, "release/1.0")!, _scanningService.ResolveHead(repo)!);

                Assert.Contains(scan.Commits, c => c.MessageShort == "Feature commit");
                Assert.DoesNotContain(scan.Commits, c => c.MessageShort == "Initial commit");
                Assert.Contains(scan.CommitInfos, c => c.IsMerge);

                // Reported once by the feature commit, not again by the merge commit
                var featureChange = Assert.Single(scan.FileChanges, c => c.FilePath == "feature_file.txt");
                Assert.Contains("feature/early", featureChange.Branch);
            }
        }

        [Fact]
        public void Test_16_ScanToExplicitTargetBranch()
        {
            using (var repo = new Repository(_tempRepoPath))
            {
                CommitFile(repo, "base.txt", "base", "Initial commit");
                var mainName = repo.Head.FriendlyName;
                repo.CreateBranch("release/1.0");

                var featureBranch = repo.CreateBranch("feature/target");
                Commands.Checkout(repo, featureBranch);
                CommitFile(repo, "target_only.txt", "content", "Target commit");
                Commands.Checkout(repo, repo.Branches[mainName]);

                var start = _scanningService.ResolveReference(repo, "release/1.0")!;
                var headScan = _scanningService.Scan(repo, start, _scanningService.ResolveHead(repo)!);
                var targetScan = _scanningService.Scan(repo, start, _scanningService.ResolveReference(repo, "feature/target")!);

                Assert.Equal(0, headScan.NewCommitCount);
                Assert.Equal(1, targetScan.NewCommitCount);
                Assert.Contains(targetScan.FileChanges, c => c.FilePath == "target_only.txt");
                Assert.True(targetScan.StartIsAncestorOfTarget);
            }
        }

        [Fact]
        public void Test_17_ResolveReferenceAcceptsTagsShasAndRemoteBranches()
        {
            using (var repo = new Repository(_tempRepoPath))
            {
                CommitFile(repo, "base.txt", "base", "Initial commit");
                var commit = repo.Head.Tip;
                var signature = new Signature("John Doe", "john@example.com", DateTimeOffset.Now);
                repo.ApplyTag("v1.0", signature, "Release 1.0");
                repo.Refs.Add("refs/remotes/origin/feature/remote-only", commit.Id);

                Assert.Equal(commit.Sha, _scanningService.ResolveReference(repo, "v1.0")?.Commit.Sha);
                Assert.Equal(commit.Sha, _scanningService.ResolveReference(repo, commit.Sha[..8])?.Commit.Sha);

                var remote = _scanningService.ResolveReference(repo, "feature/remote-only");
                Assert.NotNull(remote);
                Assert.Equal("origin/feature/remote-only", remote!.Name);

                Assert.Null(_scanningService.ResolveReference(repo, "does-not-exist"));
            }
        }

        [Fact]
        public void Test_18_ScanRecordsLineCountsAndScopedBranchMap()
        {
            using (var repo = new Repository(_tempRepoPath))
            {
                CommitFile(repo, "lines.txt", "one\ntwo\n", "Initial commit");
                repo.CreateBranch("release/1.0");
                CommitFile(repo, "lines.txt", "one\nTWO\nthree\n", "Edit lines");

                var scan = _scanningService.Scan(repo, _scanningService.ResolveReference(repo, "release/1.0")!, _scanningService.ResolveHead(repo)!);

                var change = scan.FileChanges.Single(c => c.CommitMessage == "Edit lines");
                Assert.Equal(2, change.LinesAdded);
                Assert.Equal(1, change.LinesDeleted);

                var commitInfo = scan.CommitInfos.Single(c => c.Message == "Edit lines");
                Assert.Equal(1, commitInfo.FilesChanged);
                Assert.Equal(2, commitInfo.Insertions);

                // Branch map only covers scanned commits
                Assert.All(scan.CommitBranchMap.Keys, sha => Assert.Contains(scan.Commits, c => c.Sha == sha));
            }
        }

        [Fact]
        public void Test_19_ReportsAreGeneratedForScan()
        {
            var outputDir = _tempRepoPath + "_reports";
            try
            {
                using (var repo = new Repository(_tempRepoPath))
                {
                    CommitFile(repo, "base.txt", "base", "Initial commit");
                    repo.CreateBranch("release/1.0");
                    CommitFile(repo, "src/Tom&Jerry.cs", "class Feature { }", "Add <Feature>");
                    RenameFile(repo, "base.txt", "renamed.txt", "Rename base");

                    var scan = _scanningService.Scan(repo, _scanningService.ResolveReference(repo, "release/1.0")!, _scanningService.ResolveHead(repo)!);

                    var reportData = CreateOrchestrator().AnalyzeScan(repo, scan, 30);

                    Assert.True(reportData.Summary.IsScopedScan);
                    Assert.Equal(scan.Commits.Count, reportData.Summary.TotalCommits);
                    Assert.Equal(scan.FileChanges.Count, reportData.Summary.TotalFileChanges);
                    Assert.Equal(1, reportData.Summary.FilesRenamed);
                    Assert.Contains(reportData.FileHotspots, f => f.Path == "src/Tom&Jerry.cs");

                    new HtmlReportGenerator().Generate(reportData, outputDir, "Test Report");
                    new ExcelReportGenerator().Generate(reportData, Path.Combine(outputDir, "report.xlsx"));

                    var html = File.ReadAllText(Path.Combine(outputDir, "index.html"));
                    Assert.Contains("Scan range:", html);
                    Assert.Contains("id=\"fileChangesTable\"", html);
                    Assert.Contains("src/Tom&amp;Jerry.cs", html); // File and commit text is HTML-encoded
                    Assert.Contains("Add &lt;Feature&gt;", html);
                    Assert.DoesNotContain("<td>Add <Feature></td>", html);

                    using var workbook = new ClosedXML.Excel.XLWorkbook(Path.Combine(outputDir, "report.xlsx"));
                    Assert.True(workbook.Worksheets.Contains("File Changes"));
                    Assert.Equal(scan.FileChanges.Count + 1, workbook.Worksheet("File Changes").RangeUsed()!.RowCount());
                    Assert.True(workbook.Worksheets.Contains("Modules"));
                    Assert.True(workbook.Worksheets.Contains("Release Notes"));

                    // Diffs are written to a side file instead of being inlined in the page
                    Assert.DoesNotContain("data-diff=", html);
                    Assert.Contains("window.GIT_DIFFS", File.ReadAllText(Path.Combine(outputDir, "assets", "js", "diffs.js")));

                    var jsonPath = Path.Combine(outputDir, "report.json");
                    new JsonReportGenerator().Generate(reportData, jsonPath);
                    using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(jsonPath));
                    Assert.Equal(scan.FileChanges.Count, json.RootElement.GetProperty("fileChanges").GetArrayLength());
                    Assert.True(json.RootElement.GetProperty("modules").GetArrayLength() > 0);
                    Assert.False(json.RootElement.GetProperty("comparisons")[0].GetProperty("files")[0].TryGetProperty("beforeContent", out _));
                }
            }
            finally
            {
                if (Directory.Exists(outputDir))
                {
                    Directory.Delete(outputDir, true);
                }
            }
        }
    }
}
