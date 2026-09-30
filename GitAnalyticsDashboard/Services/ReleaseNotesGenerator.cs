using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GitAnalyticsDashboard.Models;
using LibGit2Sharp;

namespace GitAnalyticsDashboard.Services
{
    public class ReleaseNotesOptions
    {
        /// <summary>Regex for ticket references in commit messages. "#123" matches are stored without the "#".</summary>
        public string TicketPattern { get; set; } = DefaultTicketPattern;

        /// <summary>Link template with an {id} placeholder, e.g. "https://jira.example.com/browse/{id}". Empty for no links.</summary>
        public string TicketUrlTemplate { get; set; } = string.Empty;

        public const string DefaultTicketPattern = @"\b[A-Z][A-Z0-9]+-\d+\b|(?<![\w/&])#\d+\b";
    }

    /// <summary>
    /// Builds release notes from the scanned commits, grouping Conventional Commits
    /// ("feat(api)!: add endpoint") by type. Other messages land under "Other Changes".
    /// </summary>
    public class ReleaseNotesGenerator
    {
        private static readonly Regex ConventionalRegex = new Regex(
            @"^(?<type>[A-Za-z]+)(?:\((?<scope>[^)]*)\))?(?<bang>!)?:\s*(?<desc>.+)$",
            RegexOptions.Compiled);

        private static readonly Regex BreakingRegex = new Regex(
            @"^BREAKING[ -]CHANGE:\s*(?<note>.+)$",
            RegexOptions.Compiled | RegexOptions.Multiline);

        // Section key, title and the commit types that belong to it, in display order
        private static readonly (string Key, string Title, string[] Types)[] SectionDefinitions =
        {
            ("feat", "Features", new[] { "feat", "feature" }),
            ("fix", "Bug Fixes", new[] { "fix", "bugfix", "hotfix" }),
            ("perf", "Performance", new[] { "perf" }),
            ("refactor", "Refactoring", new[] { "refactor" }),
            ("docs", "Documentation", new[] { "docs", "doc" }),
            ("test", "Tests", new[] { "test", "tests" }),
            ("build", "Build & CI", new[] { "build", "ci" }),
            ("chore", "Chores", new[] { "chore", "style" }),
            ("revert", "Reverts", new[] { "revert" }),
            ("other", "Other Changes", Array.Empty<string>())
        };

        public ReleaseNotes Generate(ScanResult scan, ReleaseNotesOptions? options = null)
        {
            options ??= new ReleaseNotesOptions();
            var ticketRegex = new Regex(string.IsNullOrWhiteSpace(options.TicketPattern) ? ReleaseNotesOptions.DefaultTicketPattern : options.TicketPattern);

            var notes = new ReleaseNotes
            {
                StartRef = scan.Start.Name,
                StartSha = scan.Start.Commit.Sha,
                TargetRef = scan.Target.Name,
                TargetSha = scan.Target.Commit.Sha,
                GeneratedAt = DateTimeOffset.Now
            };

            var entries = new List<ReleaseNoteEntry>();
            foreach (var commit in scan.Commits)
            {
                // Merge commits only restate the branch name; the merged commits are listed individually
                if (commit.Parents.Count() > 1)
                    continue;

                entries.Add(Parse(commit, scan.Options.Mailmap.Map(commit.Author).Name, ticketRegex, options.TicketUrlTemplate));
            }

            var breaking = entries.Where(e => e.IsBreaking).ToList();
            if (breaking.Any())
            {
                notes.Sections.Add(new ReleaseNoteSection { Key = ReleaseNoteSection.BreakingKey, Title = "Breaking Changes", Entries = breaking });
            }

            foreach (var (key, title, types) in SectionDefinitions)
            {
                var sectionEntries = entries.Where(e => SectionKeyFor(e.Type) == key).ToList();
                if (sectionEntries.Any())
                {
                    notes.Sections.Add(new ReleaseNoteSection { Key = key, Title = title, Entries = sectionEntries });
                }
            }

            notes.Contributors = entries
                .GroupBy(e => e.Author)
                .Select(g => new ReleaseNoteContributor { Name = g.Key, Commits = g.Count() })
                .OrderByDescending(c => c.Commits)
                .ThenBy(c => c.Name)
                .ToList();

            return notes;
        }

        public ReleaseNoteEntry Parse(Commit commit, string author, Regex ticketRegex, string ticketUrlTemplate)
        {
            var entry = new ReleaseNoteEntry
            {
                Sha = commit.Sha,
                Author = author,
                Date = commit.Author.When,
                Description = commit.MessageShort.Trim()
            };

            var match = ConventionalRegex.Match(commit.MessageShort.Trim());
            if (match.Success)
            {
                entry.Type = match.Groups["type"].Value.ToLowerInvariant();
                entry.Scope = match.Groups["scope"].Value.Trim();
                entry.Description = match.Groups["desc"].Value.Trim();
                entry.IsBreaking = match.Groups["bang"].Success;
            }

            var breakingMatch = BreakingRegex.Match(commit.Message);
            if (breakingMatch.Success)
            {
                entry.IsBreaking = true;
                entry.BreakingNote = breakingMatch.Groups["note"].Value.Trim();
            }

            entry.Tickets = ticketRegex.Matches(commit.Message)
                .Select(m => m.Value.TrimStart('#'))
                .Distinct()
                .Select(id => new ReleaseNoteTicket
                {
                    Id = id,
                    Url = string.IsNullOrWhiteSpace(ticketUrlTemplate) ? string.Empty : ticketUrlTemplate.Replace("{id}", Uri.EscapeDataString(id))
                })
                .ToList();

            return entry;
        }

        private static string SectionKeyFor(string type)
        {
            foreach (var (key, _, types) in SectionDefinitions)
            {
                if (types.Contains(type))
                    return key;
            }
            return "other";
        }

        public string ToMarkdown(ReleaseNotes notes)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Release Notes: {notes.StartRef} → {notes.TargetRef}");
            sb.AppendLine();
            sb.AppendLine($"_Range `{Short(notes.StartSha)}..{Short(notes.TargetSha)}` · {notes.EntryCount} commits · generated {notes.GeneratedAt:yyyy-MM-dd HH:mm}_");

            foreach (var section in notes.Sections)
            {
                sb.AppendLine();
                sb.AppendLine($"## {section.Title}");
                sb.AppendLine();
                foreach (var entry in section.Entries)
                {
                    var line = new StringBuilder("- ");
                    if (!string.IsNullOrEmpty(entry.Scope))
                        line.Append($"**{EscapeMarkdown(entry.Scope)}:** ");
                    line.Append(EscapeMarkdown(entry.Description));
                    line.Append($" (`{Short(entry.Sha)}`, {EscapeMarkdown(entry.Author)})");
                    if (entry.Tickets.Any())
                    {
                        line.Append(" · ");
                        line.Append(string.Join(", ", entry.Tickets.Select(t => string.IsNullOrEmpty(t.Url) ? t.Id : $"[{t.Id}]({t.Url})")));
                    }
                    sb.AppendLine(line.ToString());

                    if (section.Key == ReleaseNoteSection.BreakingKey && !string.IsNullOrEmpty(entry.BreakingNote))
                    {
                        sb.AppendLine($"  > {EscapeMarkdown(entry.BreakingNote)}");
                    }
                }
            }

            if (notes.Contributors.Any())
            {
                sb.AppendLine();
                sb.AppendLine("## Contributors");
                sb.AppendLine();
                foreach (var contributor in notes.Contributors)
                {
                    sb.AppendLine($"- {EscapeMarkdown(contributor.Name)} ({contributor.Commits} {(contributor.Commits == 1 ? "commit" : "commits")})");
                }
            }

            return sb.ToString();
        }

        public void WriteMarkdown(ReleaseNotes notes, string filePath)
        {
            File.WriteAllText(filePath, ToMarkdown(notes));
        }

        private static string Short(string sha) => sha.Length > 7 ? sha[..7] : sha;

        private static string EscapeMarkdown(string text)
        {
            return Regex.Replace(text, @"([\\`*_\[\]<>])", @"\$1");
        }
    }
}
