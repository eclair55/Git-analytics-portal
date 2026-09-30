using System;
using System.Collections.Generic;
using System.Linq;
using GitAnalyticsDashboard.Utilities;

namespace GitAnalyticsDashboard.Models
{
    public class ScanOptions
    {
        public PathFilter PathFilter { get; set; } = PathFilter.None;

        /// <summary>Case-insensitive substring matched against the (mailmapped) author name or email.</summary>
        public string? Author { get; set; }

        /// <summary>Inclusive bounds on the author date.</summary>
        public DateTimeOffset? Since { get; set; }
        public DateTimeOffset? Until { get; set; }

        public Mailmap Mailmap { get; set; } = Mailmap.Empty;

        public bool HasCommitFilters => !string.IsNullOrWhiteSpace(Author) || Since.HasValue || Until.HasValue;

        public bool IsAuthorIncluded(string name, string email, DateTimeOffset date)
        {
            if (Since.HasValue && date < Since.Value)
                return false;
            if (Until.HasValue && date > Until.Value)
                return false;
            if (!string.IsNullOrWhiteSpace(Author)
                && !name.Contains(Author, StringComparison.OrdinalIgnoreCase)
                && !email.Contains(Author, StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }

        /// <summary>Human-readable list of the active filters, for console and report headers.</summary>
        public List<string> Describe()
        {
            var parts = new List<string>();
            if (PathFilter.IncludePatterns.Any())
                parts.Add($"include: {string.Join(", ", PathFilter.IncludePatterns)}");
            if (PathFilter.ExcludePatterns.Any())
                parts.Add($"exclude: {string.Join(", ", PathFilter.ExcludePatterns)}");
            if (!string.IsNullOrWhiteSpace(Author))
                parts.Add($"author: {Author}");
            if (Since.HasValue)
                parts.Add($"since: {Since.Value:yyyy-MM-dd HH:mm}");
            if (Until.HasValue)
                parts.Add($"until: {Until.Value:yyyy-MM-dd HH:mm}");
            return parts;
        }
    }
}
