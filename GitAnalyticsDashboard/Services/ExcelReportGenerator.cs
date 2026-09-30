using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using GitAnalyticsDashboard.Models;

namespace GitAnalyticsDashboard.Services
{
    public class ExcelReportGenerator
    {
        public void Generate(ReportData data, string filePath)
        {
            using (var workbook = new XLWorkbook())
            {
                AddSummarySheet(workbook, data.Summary);
                if (data.FileChanges.Any())
                {
                    AddFileChangesSheet(workbook, data.FileChanges);
                }
                if (data.Modules.Any())
                {
                    AddModulesSheet(workbook, data.Modules);
                }
                if (data.ReleaseNotes != null && data.ReleaseNotes.Sections.Any())
                {
                    AddReleaseNotesSheet(workbook, data.ReleaseNotes);
                }
                AddBranchesSheet(workbook, data.Branches);
                AddDevelopersSheet(workbook, data.Developers);
                AddCommitsSheet(workbook, data.RecentCommits);
                AddFilesSheet(workbook, data.FileHotspots);
                AddComparisonSheet(workbook, data.Comparisons);

                workbook.SaveAs(filePath);
            }
        }

        private void AddSummarySheet(XLWorkbook workbook, RepositorySummary summary)
        {
            var ws = workbook.Worksheets.Add("Summary");

            var rows = new List<(string Label, XLCellValue Value)>
            {
                ("Repository Name", summary.RepositoryName)
            };
            if (summary.IsScopedScan)
            {
                rows.Add(("Starting Branch", $"{summary.StartingRef} ({ShortSha(summary.StartingCommitSha)})"));
                rows.Add(("Target Branch", $"{summary.TargetRef} ({ShortSha(summary.TargetCommitSha)})"));
                rows.Add(("Start Is Ancestor Of Target", summary.StartIsAncestorOfTarget ? "Yes" : "No"));
            }
            rows.Add((summary.IsScopedScan ? "Commits Scanned" : "Total Commits", summary.TotalCommits));
            if (summary.IsScopedScan)
            {
                rows.Add(("Merge Commits", summary.MergeCommits));
                rows.Add(("File Changes", summary.TotalFileChanges));
                rows.Add(("Files Touched", summary.UniqueFilesChanged));
                rows.Add(("Files Added", summary.FilesAdded));
                rows.Add(("Files Modified", summary.FilesModified));
                rows.Add(("Files Deleted", summary.FilesDeleted));
                rows.Add(("Files Renamed", summary.FilesRenamed));
                rows.Add(("Lines Added", summary.TotalLinesAdded));
                rows.Add(("Lines Deleted", summary.TotalLinesDeleted));
            }
            rows.Add(("Total Branches", summary.TotalBranches));
            rows.Add(("Active Developers", summary.ActiveDevelopers));
            rows.Add(("Commits Today", summary.CommitsToday));
            rows.Add(("Commits (Month)", summary.CommitsThisMonth));
            var staleRow = rows.Count + 1;
            rows.Add(("Stale Branches", summary.StaleBranches));
            rows.Add(("Average Branch Age (Days)", Math.Round(summary.AverageBranchAgeDays, 2)));
            rows.Add(("Most Active Developer", summary.MostActiveDeveloper));

            for (var i = 0; i < rows.Count; i++)
            {
                ws.Cell(i + 1, 1).Value = rows[i].Label;
                ws.Cell(i + 1, 2).Value = rows[i].Value;
            }

            var rngTable = ws.Range(1, 1, rows.Count, 2);
            rngTable.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            rngTable.FirstColumn().Style.Font.Bold = true;
            rngTable.FirstColumn().Style.Fill.BackgroundColor = XLColor.LightGray;

            // Conditional formatting for stale branches
            ws.Cell(staleRow, 2).AddConditionalFormat().WhenGreaterThan(5).Fill.SetBackgroundColor(XLColor.Red);

            ws.Columns().AdjustToContents();
        }

        private void AddFileChangesSheet(XLWorkbook workbook, List<FileChangeInfo> changes)
        {
            var ws = workbook.Worksheets.Add("File Changes");
            var exportData = changes.Select(c => new
            {
                Date = c.ChangeDate.DateTime,
                c.FilePath,
                c.ChangeType,
                c.OldPath,
                Author = c.AuthorName,
                c.AuthorEmail,
                Commit = ShortSha(c.CommitHash),
                c.Branch,
                Message = c.CommitMessage,
                c.LinesAdded,
                c.LinesDeleted
            });
            var table = ws.Cell(1, 1).InsertTable(exportData);

            ws.SheetView.FreezeRows(1);
            table.Theme = XLTableTheme.TableStyleMedium2;
            ws.Column(1).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
            ws.Columns().AdjustToContents();
        }

        private void AddModulesSheet(XLWorkbook workbook, List<ModuleStatistic> modules)
        {
            var ws = workbook.Worksheets.Add("Modules");
            var exportData = modules.Select(m => new
            {
                m.Module,
                m.ProjectFile,
                m.Commits,
                m.FileChanges,
                m.FilesTouched,
                m.Contributors,
                m.Added,
                m.Modified,
                m.Deleted,
                m.Renamed,
                m.LinesAdded,
                m.LinesDeleted,
                m.Churn
            });
            var table = ws.Cell(1, 1).InsertTable(exportData);

            ws.SheetView.FreezeRows(1);
            table.Theme = XLTableTheme.TableStyleMedium2;
            ws.Columns().AdjustToContents();
        }

        private void AddReleaseNotesSheet(XLWorkbook workbook, ReleaseNotes notes)
        {
            var ws = workbook.Worksheets.Add("Release Notes");
            var exportData = notes.Sections.SelectMany(s => s.Entries.Select(e => new
            {
                Section = s.Title,
                e.Type,
                e.Scope,
                e.Description,
                Breaking = e.IsBreaking ? "Yes" : string.Empty,
                e.BreakingNote,
                Commit = ShortSha(e.Sha),
                e.Author,
                Date = e.Date.DateTime,
                Tickets = string.Join(", ", e.Tickets.Select(t => t.Id))
            }));
            var table = ws.Cell(1, 1).InsertTable(exportData);

            ws.SheetView.FreezeRows(1);
            table.Theme = XLTableTheme.TableStyleMedium2;
            ws.Columns().AdjustToContents();
        }

        private static string ShortSha(string sha) => sha.Length > 7 ? sha[..7] : sha;

        private void AddBranchesSheet(XLWorkbook workbook, List<BranchInfo> branches)
        {
            var ws = workbook.Worksheets.Add("Branches");
            var table = ws.Cell(1, 1).InsertTable(branches);

            // Freeze Header
            ws.SheetView.FreezeRows(1);

            // Styling
            table.Theme = XLTableTheme.TableStyleMedium2;
            ws.Columns().AdjustToContents();
        }

        private void AddDevelopersSheet(XLWorkbook workbook, List<DeveloperInfo> developers)
        {
            var ws = workbook.Worksheets.Add("Developers");
            var exportData = developers.Select(d => new {
                d.Name, d.Email, d.TotalCommits, d.LinesAdded, d.LinesRemoved, d.FilesModified, d.FirstCommit, d.LatestCommit
            });
            var table = ws.Cell(1, 1).InsertTable(exportData);

            ws.SheetView.FreezeRows(1);
            table.Theme = XLTableTheme.TableStyleMedium2;
            ws.Columns().AdjustToContents();
        }

        private void AddCommitsSheet(XLWorkbook workbook, List<CommitInfo> commits)
        {
            var ws = workbook.Worksheets.Add("Commits");
            var table = ws.Cell(1, 1).InsertTable(commits);

            ws.SheetView.FreezeRows(1);
            table.Theme = XLTableTheme.TableStyleMedium2;
            ws.Columns().AdjustToContents();
        }

        private void AddFilesSheet(XLWorkbook workbook, List<FileStatistic> files)
        {
            var ws = workbook.Worksheets.Add("Files");
            var table = ws.Cell(1, 1).InsertTable(files);

            ws.SheetView.FreezeRows(1);
            table.Theme = XLTableTheme.TableStyleMedium2;

            // Conditional formatting for high commit count (hotspots)
            if (files.Any())
            {
                var commitColumn = ws.Range(2, 2, 1 + files.Count, 2);
                commitColumn.AddConditionalFormat().WhenGreaterThan(files.Average(f => f.CommitCount)).Fill.SetBackgroundColor(XLColor.LightSalmon);
            }

            ws.Columns().AdjustToContents();
        }

        private void AddComparisonSheet(XLWorkbook workbook, List<CommitComparison> comparisons)
        {
            var ws = workbook.Worksheets.Add("Before After Comparison");

            // Header
            ws.Cell(1, 1).Value = "Commit";
            ws.Cell(1, 2).Value = "Date";
            ws.Cell(1, 3).Value = "Author";
            ws.Cell(1, 4).Value = "File";
            ws.Cell(1, 5).Value = "Change Type";
            ws.Cell(1, 6).Value = "Summary";
            ws.Cell(1, 7).Value = "Before";
            ws.Cell(1, 8).Value = "After";

            var row = 2;
            foreach (var commit in comparisons)
            {
                foreach (var file in commit.FileChanges)
                {
                    ws.Cell(row, 1).Value = commit.Sha.Substring(0, 7);
                    ws.Cell(row, 2).Value = commit.Date.DateTime;
                    ws.Cell(row, 3).Value = commit.Author;
                    ws.Cell(row, 4).Value = file.Path;
                    ws.Cell(row, 5).Value = file.ChangeType;
                    ws.Cell(row, 6).Value = file.Summary;

                    if (file.BeforeContent.Length > 30000)
                    {
                        ws.Cell(row, 7).Value = "Content too large. See HTML report.";
                    }
                    else
                    {
                        ws.Cell(row, 7).Value = file.BeforeContent;
                    }

                    if (file.AfterContent.Length > 30000)
                    {
                        ws.Cell(row, 8).Value = "Content too large. See HTML report.";
                    }
                    else
                    {
                        ws.Cell(row, 8).Value = file.AfterContent;
                    }

                    // Hyperlink to HTML report
                    ws.Cell(row, 1).SetHyperlink(new XLHyperlink("index.html#comparison"));

                    row++;
                }
            }

            var header = ws.Range(1, 1, 1, 8);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.LightGray;
            ws.SheetView.FreezeRows(1);
            ws.Columns().AdjustToContents();
            ws.Columns(7, 8).Width = 50; // Limit width of content columns
        }
    }
}
