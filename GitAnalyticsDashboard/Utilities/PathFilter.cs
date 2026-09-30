using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GitAnalyticsDashboard.Utilities
{
    /// <summary>
    /// Include/exclude filter for repository paths using gitignore-style globs:
    /// <list type="bullet">
    /// <item><c>bin/</c> - a directory with that name at any depth</item>
    /// <item><c>*.min.js</c> - no slash, so it matches a file or directory name at any depth</item>
    /// <item><c>src/**/*.cs</c> - contains a slash, so it is matched from the repository root</item>
    /// </list>
    /// <c>*</c> and <c>?</c> stop at <c>/</c>; <c>**</c> spans directories. Matching is case-insensitive.
    /// </summary>
    public class PathFilter
    {
        private readonly List<(string Pattern, Regex Regex)> _includes;
        private readonly List<(string Pattern, Regex Regex)> _excludes;

        public PathFilter(IEnumerable<string>? includes, IEnumerable<string>? excludes)
        {
            _includes = Compile(includes);
            _excludes = Compile(excludes);
        }

        public static PathFilter None { get; } = new PathFilter(null, null);

        public IReadOnlyList<string> IncludePatterns => _includes.Select(p => p.Pattern).ToList();
        public IReadOnlyList<string> ExcludePatterns => _excludes.Select(p => p.Pattern).ToList();
        public bool IsActive => _includes.Any() || _excludes.Any();

        public bool IsIncluded(string path)
        {
            path = path.Replace('\\', '/');
            if (_includes.Any() && !_includes.Any(p => p.Regex.IsMatch(path)))
                return false;
            return !_excludes.Any(p => p.Regex.IsMatch(path));
        }

        /// <summary>True when an exclude pattern matches; include patterns are ignored.</summary>
        public bool IsExcluded(string path)
        {
            path = path.Replace('\\', '/');
            return _excludes.Any(p => p.Regex.IsMatch(path));
        }

        public static bool IsMatch(string pattern, string path)
        {
            return ToRegex(pattern)?.IsMatch(path.Replace('\\', '/')) ?? false;
        }

        private static List<(string, Regex)> Compile(IEnumerable<string>? patterns)
        {
            var compiled = new List<(string, Regex)>();
            foreach (var pattern in patterns ?? Enumerable.Empty<string>())
            {
                var regex = ToRegex(pattern);
                if (regex != null)
                {
                    compiled.Add((pattern.Trim(), regex));
                }
            }
            return compiled;
        }

        private static Regex? ToRegex(string pattern)
        {
            var p = pattern.Trim().Replace('\\', '/');
            if (p.Length == 0)
                return null;

            string regex;
            if (p.EndsWith("/"))
            {
                // Directory at any depth (or anchored when it contains another slash)
                var dir = p.TrimEnd('/');
                var anchored = dir.Contains('/');
                regex = (anchored ? "^" : "^(.*/)?") + GlobToRegex(dir.TrimStart('/')) + "/.*$";
            }
            else if (p.Contains('/'))
            {
                regex = "^" + GlobToRegex(p.TrimStart('/')) + "(/.*)?$";
            }
            else
            {
                regex = "^(.*/)?" + GlobToRegex(p) + "(/.*)?$";
            }

            return new Regex(regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        }

        private static string GlobToRegex(string glob)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < glob.Length; i++)
            {
                var c = glob[i];
                if (c == '*')
                {
                    if (i + 1 < glob.Length && glob[i + 1] == '*')
                    {
                        i++;
                        if (i + 1 < glob.Length && glob[i + 1] == '/')
                        {
                            i++;
                            sb.Append("(.*/)?"); // "**/" = zero or more directories
                        }
                        else
                        {
                            sb.Append(".*");
                        }
                    }
                    else
                    {
                        sb.Append("[^/]*");
                    }
                }
                else if (c == '?')
                {
                    sb.Append("[^/]");
                }
                else
                {
                    sb.Append(Regex.Escape(c.ToString()));
                }
            }
            return sb.ToString();
        }

        public static List<string> SplitPatterns(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return new List<string>();
            return value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
    }
}
