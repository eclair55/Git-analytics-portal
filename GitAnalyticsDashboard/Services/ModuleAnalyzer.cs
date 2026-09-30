using System;
using System.Collections.Generic;
using System.Linq;
using GitAnalyticsDashboard.Models;
using GitAnalyticsDashboard.Utilities;
using LibGit2Sharp;

namespace GitAnalyticsDashboard.Services
{
    /// <summary>
    /// Rolls file changes up to modules: the nearest directory holding a project file
    /// (.csproj, package.json, pom.xml, ...) in the target commit, otherwise the top-level folder.
    /// </summary>
    public class ModuleAnalyzer
    {
        public const string RootModule = "(root)";

        private static readonly string[] ProjectFileExtensions = { ".csproj", ".fsproj", ".vbproj", ".vcxproj", ".sqlproj" };
        private static readonly HashSet<string> ProjectFileNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "package.json", "pom.xml", "build.gradle", "build.gradle.kts", "go.mod", "Cargo.toml", "pyproject.toml", "setup.py", "composer.json"
        };

        public List<ModuleStatistic> Analyze(Commit targetCommit, IEnumerable<FileChangeInfo> changes, PathFilter? pathFilter = null)
        {
            var projectDirs = FindProjectDirectories(targetCommit.Tree, pathFilter ?? PathFilter.None);

            // Longest directory first so nested projects win over their parents. A project file at the
            // repository root (e.g. a workspace package.json) would swallow everything, so top-level
            // folders are used below it instead.
            var orderedDirs = projectDirs.Keys.Where(d => d.Length > 0).OrderByDescending(d => d.Length).ToList();

            return changes
                .GroupBy(c => ResolveModule(c.FilePath, orderedDirs))
                .Select(g => new ModuleStatistic
                {
                    Module = g.Key,
                    ProjectFile = projectDirs.TryGetValue(g.Key, out var projectFile) ? projectFile : string.Empty,
                    FileChanges = g.Count(),
                    FilesTouched = g.Select(c => c.FilePath).Distinct().Count(),
                    Commits = g.Select(c => c.CommitHash).Distinct().Count(),
                    Contributors = g.Select(c => c.AuthorEmail).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    LinesAdded = g.Sum(c => c.LinesAdded),
                    LinesDeleted = g.Sum(c => c.LinesDeleted),
                    Added = g.Count(c => c.ChangeType == "Added"),
                    Modified = g.Count(c => c.ChangeType == "Modified"),
                    Deleted = g.Count(c => c.ChangeType == "Deleted"),
                    Renamed = g.Count(c => c.ChangeType == "Renamed")
                })
                .OrderByDescending(m => m.Churn)
                .ThenByDescending(m => m.FileChanges)
                .ToList();
        }

        public static string ResolveModule(string filePath, IReadOnlyList<string> projectDirsLongestFirst)
        {
            foreach (var dir in projectDirsLongestFirst)
            {
                if (filePath.StartsWith(dir + "/", StringComparison.Ordinal))
                    return dir;
            }

            var slash = filePath.IndexOf('/');
            return slash > 0 ? filePath[..slash] : RootModule;
        }

        /// <summary>Directory path (relative, "/"-separated, "" for root) mapped to the project file found there.</summary>
        public Dictionary<string, string> FindProjectDirectories(Tree tree, PathFilter pathFilter)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            Walk(tree, string.Empty, pathFilter, result);
            return result;
        }

        private static void Walk(Tree tree, string dir, PathFilter pathFilter, Dictionary<string, string> result)
        {
            foreach (var entry in tree)
            {
                var path = dir.Length == 0 ? entry.Name : $"{dir}/{entry.Name}";
                if (entry.TargetType == TreeEntryTargetType.Tree)
                {
                    // Skip excluded folders such as node_modules/ or bin/ entirely
                    if (!pathFilter.IsExcluded(path + "/"))
                    {
                        Walk((Tree)entry.Target, path, pathFilter, result);
                    }
                }
                else if (entry.TargetType == TreeEntryTargetType.Blob && IsProjectFile(entry.Name) && !result.ContainsKey(dir))
                {
                    result[dir] = entry.Name;
                }
            }
        }

        private static bool IsProjectFile(string name)
        {
            return ProjectFileNames.Contains(name)
                || ProjectFileExtensions.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        }
    }
}
