using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LibGit2Sharp;

namespace GitAnalyticsDashboard.Utilities
{
    /// <summary>
    /// Maps commit identities to canonical ones using git's .mailmap format:
    /// <code>
    /// Proper Name &lt;commit@email&gt;
    /// &lt;proper@email&gt; &lt;commit@email&gt;
    /// Proper Name &lt;proper@email&gt; &lt;commit@email&gt;
    /// Proper Name &lt;proper@email&gt; Commit Name &lt;commit@email&gt;
    /// </code>
    /// Emails are compared case-insensitively; a commit name, when given, must match exactly.
    /// </summary>
    public class Mailmap
    {
        private static readonly Regex EntryRegex = new Regex(
            @"^\s*(?<name1>[^<]*?)\s*<(?<email1>[^>]*)>\s*(?:(?<name2>[^<]*?)\s*<(?<email2>[^>]*)>)?\s*$",
            RegexOptions.Compiled);

        private readonly Dictionary<string, (string? Name, string? Email)> _byEmail = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<(string Name, string Email), (string? Name, string? Email)> _byNameAndEmail = new(new NameEmailComparer());

        public static Mailmap Empty { get; } = new Mailmap();

        public int Count => _byEmail.Count + _byNameAndEmail.Count;

        /// <summary>Loads the repository's .mailmap (working copy first, then the given commit) plus any extra entries.</summary>
        public static Mailmap Load(Repository repo, Commit? commit, IEnumerable<string>? extraLines = null)
        {
            var mailmap = new Mailmap();

            string? content = null;
            if (repo.Info.WorkingDirectory != null)
            {
                var path = Path.Combine(repo.Info.WorkingDirectory, ".mailmap");
                if (File.Exists(path))
                {
                    content = File.ReadAllText(path);
                }
            }
            if (content == null && commit?[".mailmap"]?.Target is Blob blob)
            {
                content = blob.GetContentText();
            }

            if (content != null)
            {
                mailmap.AddLines(content.Split('\n'));
            }
            if (extraLines != null)
            {
                mailmap.AddLines(extraLines);
            }
            return mailmap;
        }

        public static Mailmap Parse(IEnumerable<string> lines)
        {
            var mailmap = new Mailmap();
            mailmap.AddLines(lines);
            return mailmap;
        }

        public void AddLines(IEnumerable<string> lines)
        {
            foreach (var rawLine in lines)
            {
                var line = rawLine;
                var comment = line.IndexOf('#');
                if (comment >= 0)
                {
                    line = line[..comment];
                }
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var match = EntryRegex.Match(line);
                if (!match.Success)
                    continue;

                var name1 = NullIfEmpty(match.Groups["name1"].Value);
                var email1 = NullIfEmpty(match.Groups["email1"].Value);

                if (!match.Groups["email2"].Success)
                {
                    // "Proper Name <commit@email>": only the name is replaced
                    if (email1 != null && name1 != null)
                    {
                        _byEmail[email1] = (name1, null);
                    }
                    continue;
                }

                var commitName = NullIfEmpty(match.Groups["name2"].Value);
                var commitEmail = NullIfEmpty(match.Groups["email2"].Value);
                if (commitEmail == null)
                    continue;

                if (commitName != null)
                {
                    _byNameAndEmail[(commitName, commitEmail)] = (name1, email1);
                }
                else
                {
                    _byEmail[commitEmail] = (name1, email1);
                }
            }
        }

        public (string Name, string Email) Map(string name, string email)
        {
            if (_byNameAndEmail.TryGetValue((name, email), out var exact) || _byEmail.TryGetValue(email, out exact))
            {
                return (exact.Name ?? name, exact.Email ?? email);
            }
            return (name, email);
        }

        public (string Name, string Email) Map(Signature signature)
        {
            return Map(signature.Name, signature.Email ?? string.Empty);
        }

        private static string? NullIfEmpty(string value)
        {
            var trimmed = value.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }

        private class NameEmailComparer : IEqualityComparer<(string Name, string Email)>
        {
            public bool Equals((string Name, string Email) x, (string Name, string Email) y)
            {
                return string.Equals(x.Name, y.Name, StringComparison.Ordinal)
                    && string.Equals(x.Email, y.Email, StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode((string Name, string Email) obj)
            {
                return HashCode.Combine(obj.Name, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Email));
            }
        }
    }
}
