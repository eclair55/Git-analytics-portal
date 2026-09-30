using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GitAnalyticsDashboard.Models;
using GitAnalyticsDashboard.Services;
using GitAnalyticsDashboard.Utilities;
using LibGit2Sharp;
using Xunit;

namespace GitAnalyticsDashboard.Tests
{
    public partial class GitScanningServiceTests
    {
        [Theory]
        [InlineData("bin/", "bin/Debug/app.dll", true)]
        [InlineData("bin/", "src/Api/bin/Debug/app.dll", true)]
        [InlineData("bin/", "src/binary.cs", false)]
        [InlineData("*.min.js", "wwwroot/js/site.min.js", true)]
        [InlineData("*.min.js", "wwwroot/js/site.js", false)]
        [InlineData("src/**/*.cs", "src/Api/Controllers/Home.cs", true)]
        [InlineData("src/**/*.cs", "src/Program.cs", true)]
        [InlineData("src/**/*.cs", "tests/Program.cs", false)]
        [InlineData("docs", "docs/readme.md", true)]
        [InlineData("*.Designer.cs", "Forms/Main.designer.cs", true)]
        [InlineData("src/*.cs", "src/Api/Home.cs", false)]
        public void Test_20_PathFilterGlobs(string pattern, string path, bool expected)
        {
            Assert.Equal(expected, PathFilter.IsMatch(pattern, path));
        }

        [Fact]
        public void Test_21_PathFilterIncludeAndExclude()
        {
            var filter = new PathFilter(new[] { "src/**" }, new[] { "*.Designer.cs" });

            Assert.True(filter.IsIncluded("src/App.cs"));
            Assert.False(filter.IsIncluded("src/Form.Designer.cs"));
            Assert.False(filter.IsIncluded("README.md"));
        }

        [Fact]
        public void Test_22_ExcludedPathsAreFilteredAndNoiseOnlyCommitsDropped()
        {
            using (var repo = new Repository(_tempRepoPath))
            {
                CommitFile(repo, "base.txt", "base", "Initial commit");
                repo.CreateBranch("release/1.0");
                CommitFiles(repo, "Feature with build output", ("src/App.cs", "class App { }"), ("bin/app.dll", "binary-ish"));
                CommitFiles(repo, "Only build output", ("bin/other.dll", "more"));

                var options = new ScanOptions { PathFilter = new PathFilter(null, new[] { "bin/" }) };
                var scan = ScanFromRelease(repo, options);

                Assert.Contains(scan.FileChanges, c => c.FilePath == "src/App.cs");
                Assert.DoesNotContain(scan.FileChanges, c => c.FilePath.StartsWith("bin/"));
                Assert.DoesNotContain(scan.Commits, c => c.MessageShort == "Only build output");
                Assert.Equal(2, scan.ExcludedFileChanges);
                Assert.Equal(1, scan.FilteredOutCommits);

                // Commit totals only count included files
                Assert.Equal(1, scan.CommitInfos.Single(c => c.Message == "Feature with build output").FilesChanged);
            }
        }

        [Fact]
        public void Test_23_AuthorAndDateFilters()
        {
            using (var repo = new Repository(_tempRepoPath))
            {
                var now = DateTimeOffset.Now;
                CommitFile(repo, "base.txt", "base", "Initial commit", date: now.AddDays(-30));
                repo.CreateBranch("release/1.0");
                CommitFile(repo, "old.txt", "a", "Old change", "Alice", "alice@example.com", now.AddDays(-20));
                CommitFile(repo, "bob.txt", "b", "Bob change", "Bob", "bob@example.com", now.AddDays(-2));
                CommitFile(repo, "alice.txt", "c", "Recent Alice change", "Alice", "alice@example.com", now.AddDays(-1));

                var byAuthor = ScanFromRelease(repo, new ScanOptions { Author = "alice" });
                Assert.All(byAuthor.CommitInfos, c => Assert.Equal("Alice", c.Author));
                Assert.Equal(2, byAuthor.Commits.Count);

                var byDate = ScanFromRelease(repo, new ScanOptions { Since = now.AddDays(-5) });
                Assert.Equal(new[] { "Recent Alice change", "Bob change" }, byDate.Commits.Select(c => c.MessageShort).ToArray());

                var both = ScanFromRelease(repo, new ScanOptions { Author = "ALICE", Since = now.AddDays(-5) });
                Assert.Equal("Recent Alice change", Assert.Single(both.Commits).MessageShort);
            }
        }

        [Fact]
        public void Test_24_MailmapMergesIdentities()
        {
            var mailmap = Mailmap.Parse(new[]
            {
                "# comment",
                "Jane Doe <jane@work.com> <jane@home.com>",
                "Jane Doe <jane@work.com> J. Doe <jdoe@old.com>",
                "Proper Bob <bob@example.com>",
                "<new@example.com> <old@example.com>"
            });

            Assert.Equal(("Jane Doe", "jane@work.com"), mailmap.Map("jane", "JANE@home.com"));
            Assert.Equal(("Jane Doe", "jane@work.com"), mailmap.Map("J. Doe", "jdoe@old.com"));
            Assert.Equal(("Someone", "jdoe@old.com"), mailmap.Map("Someone", "jdoe@old.com")); // name must match
            Assert.Equal(("Proper Bob", "bob@example.com"), mailmap.Map("bobby", "bob@example.com"));
            Assert.Equal(("Carl", "new@example.com"), mailmap.Map("Carl", "old@example.com"));
            Assert.Equal(("Unknown", "x@example.com"), mailmap.Map("Unknown", "x@example.com"));
        }

        [Fact]
        public void Test_25_MailmapAppliedToScanAndDevelopers()
        {
            using (var repo = new Repository(_tempRepoPath))
            {
                CommitFile(repo, "base.txt", "base", "Initial commit");
                repo.CreateBranch("release/1.0");
                CommitFile(repo, "a.txt", "a", "From work", "Jane Doe", "jane@work.com");
                CommitFile(repo, "b.txt", "b", "From home", "jane", "jane@home.com");

                File.WriteAllText(Path.Combine(_tempRepoPath, ".mailmap"), "Jane Doe <jane@work.com> <jane@home.com>\n");
                var options = new ScanOptions { Mailmap = Mailmap.Load(repo, repo.Head.Tip) };
                var scan = ScanFromRelease(repo, options);

                Assert.All(scan.FileChanges.Where(c => c.CommitMessage != "Initial commit"), c => Assert.Equal("jane@work.com", c.AuthorEmail));

                var developers = new DeveloperAnalyzer().Analyze(scan.CommitInfos.Where(c => c.Message != "Initial commit").ToList());
                var jane = Assert.Single(developers);
                Assert.Equal(2, jane.TotalCommits);
            }
        }

        [Fact]
        public void Test_26_ComparisonSkipsBinaryAndCapsLargeFiles()
        {
            using (var repo = new Repository(_tempRepoPath))
            {
                CommitFile(repo, "base.txt", "base", "Initial commit");
                repo.CreateBranch("release/1.0");

                var binaryPath = Path.Combine(_tempRepoPath, "image.bin");
                File.WriteAllBytes(binaryPath, new byte[] { 0, 1, 2, 0, 255, 0, 3 });
                Commands.Stage(repo, "image.bin");
                CommitFiles(repo, "Add files", ("large.txt", string.Join("\n", Enumerable.Range(0, 5000).Select(i => $"line {i}"))));

                var scan = ScanFromRelease(repo);
                var options = new ComparisonOptions { MaxFileBytes = 1024, MaxDiffChars = 500 };
                var comparisons = new ComparisonService(new CodeChangeAnalyzer()).Analyze(repo, scan.Commits, scan.CommitBranchMap, options);
                var files = comparisons.Single(c => c.Message == "Add files").FileChanges;

                var binary = files.Single(f => f.Path == "image.bin");
                Assert.True(binary.IsBinary);
                Assert.Equal(string.Empty, binary.Diff);
                Assert.Equal(string.Empty, binary.AfterContent);

                var large = files.Single(f => f.Path == "large.txt");
                Assert.True(large.IsTruncated);
                Assert.Equal(string.Empty, large.AfterContent);
                Assert.True(large.Diff.Length < 600);
            }
        }

        [Fact]
        public void Test_27_ModulesUseNearestProjectFile()
        {
            using (var repo = new Repository(_tempRepoPath))
            {
                CommitFile(repo, "base.txt", "base", "Initial commit");
                repo.CreateBranch("release/1.0");
                CommitFiles(repo, "Add projects",
                    ("src/Api/Api.csproj", "<Project />"),
                    ("src/Api/Controllers/Home.cs", "class Home { }"),
                    ("src/Web/package.json", "{}"),
                    ("src/Web/app.js", "console.log(1);"),
                    ("docs/guide.md", "# Guide"),
                    ("README.md", "readme"));

                var scan = ScanFromRelease(repo);
                var modules = new ModuleAnalyzer().Analyze(repo.Head.Tip, scan.FileChanges);

                var api = modules.Single(m => m.Module == "src/Api");
                Assert.Equal("Api.csproj", api.ProjectFile);
                Assert.Equal(2, api.FileChanges);
                Assert.Equal(2, modules.Single(m => m.Module == "src/Web").FileChanges);
                Assert.Equal(1, modules.Single(m => m.Module == "docs").FileChanges);
                Assert.Contains(modules, m => m.Module == ModuleAnalyzer.RootModule);
            }
        }

        [Fact]
        public void Test_28_ReleaseNotesGroupConventionalCommits()
        {
            using (var repo = new Repository(_tempRepoPath))
            {
                CommitFile(repo, "base.txt", "base", "Initial commit");
                repo.CreateBranch("release/1.0");
                CommitFile(repo, "a.txt", "a", "feat(api): add orders endpoint ABC-123");
                CommitFile(repo, "b.txt", "b", "fix: handle null totals (#42)");
                CommitFile(repo, "c.txt", "c", "refactor(core)!: rename OrderService");
                CommitFile(repo, "d.txt", "d", "feat: new export\n\nBREAKING CHANGE: CSV columns reordered");
                CommitFile(repo, "e.txt", "e", "Update readme");

                var scan = ScanFromRelease(repo);
                var notes = new ReleaseNotesGenerator().Generate(scan, new ReleaseNotesOptions { TicketUrlTemplate = "https://jira.example.com/browse/{id}" });

                var sectionTitles = notes.Sections.Select(s => s.Title).ToList();
                Assert.Equal("Breaking Changes", sectionTitles.First());
                Assert.Contains("Features", sectionTitles);
                Assert.Contains("Bug Fixes", sectionTitles);
                Assert.Contains("Refactoring", sectionTitles);
                Assert.Contains("Other Changes", sectionTitles);

                var breaking = notes.Sections.Single(s => s.Key == ReleaseNoteSection.BreakingKey).Entries;
                Assert.Equal(2, breaking.Count);
                Assert.Contains(breaking, e => e.BreakingNote == "CSV columns reordered");

                var feature = notes.Sections.Single(s => s.Key == "feat").Entries.Single(e => e.Scope == "api");
                Assert.Equal("add orders endpoint ABC-123", feature.Description);
                var ticket = Assert.Single(feature.Tickets);
                Assert.Equal("https://jira.example.com/browse/ABC-123", ticket.Url);

                var fix = notes.Sections.Single(s => s.Key == "fix").Entries.Single();
                Assert.Equal("42", Assert.Single(fix.Tickets).Id);

                // "Initial commit" is the starting commit and lands in Other Changes alongside "Update readme"
                Assert.Contains(notes.Sections.Single(s => s.Key == "other").Entries, e => e.Description == "Update readme");

                var markdown = new ReleaseNotesGenerator().ToMarkdown(notes);
                Assert.Contains("## Breaking Changes", markdown);
                Assert.Contains("**api:** add orders endpoint ABC-123", markdown);
                Assert.Contains("[ABC-123](https://jira.example.com/browse/ABC-123)", markdown);
                Assert.Contains("> CSV columns reordered", markdown);
            }
        }
    }
}
