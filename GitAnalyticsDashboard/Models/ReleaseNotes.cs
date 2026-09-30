using System;
using System.Collections.Generic;
using System.Linq;

namespace GitAnalyticsDashboard.Models
{
    public class ReleaseNotes
    {
        public string StartRef { get; set; } = string.Empty;
        public string StartSha { get; set; } = string.Empty;
        public string TargetRef { get; set; } = string.Empty;
        public string TargetSha { get; set; } = string.Empty;
        public DateTimeOffset GeneratedAt { get; set; }

        /// <summary>Sections in display order; empty sections are omitted.</summary>
        public List<ReleaseNoteSection> Sections { get; set; } = new();
        public List<ReleaseNoteContributor> Contributors { get; set; } = new();

        public int EntryCount => Sections.Where(s => s.Key != ReleaseNoteSection.BreakingKey).Sum(s => s.Entries.Count);
    }

    public class ReleaseNoteSection
    {
        public const string BreakingKey = "breaking";

        public string Key { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public List<ReleaseNoteEntry> Entries { get; set; } = new();
    }

    public class ReleaseNoteEntry
    {
        public string Type { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsBreaking { get; set; }
        public string BreakingNote { get; set; } = string.Empty;
        public string Sha { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public DateTimeOffset Date { get; set; }
        public List<ReleaseNoteTicket> Tickets { get; set; } = new();
    }

    public class ReleaseNoteTicket
    {
        public string Id { get; set; } = string.Empty;

        /// <summary>Link built from the configured TicketUrlTemplate; empty when no template is set.</summary>
        public string Url { get; set; } = string.Empty;
    }

    public class ReleaseNoteContributor
    {
        public string Name { get; set; } = string.Empty;
        public int Commits { get; set; }
    }
}
