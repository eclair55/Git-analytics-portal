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
                AddBranchesSheet(workbook, data.Branches);
                AddDevelopersSheet(workbook, data.Developers);
                AddCommitsSheet(workbook, data.RecentCommits);
                AddFilesSheet(workbook, data.FileHotspots);

                workbook.SaveAs(filePath);
            }
        }

        private void AddSummarySheet(XLWorkbook workbook, RepositorySummary summary)
        {
            var ws = workbook.Worksheets.Add("Summary");
            ws.Cell(1, 1).Value = "Repository Name"; ws.Cell(1, 2).Value = summary.RepositoryName;
            ws.Cell(2, 1).Value = "Total Commits"; ws.Cell(2, 2).Value = summary.TotalCommits;
            ws.Cell(3, 1).Value = "Total Branches"; ws.Cell(3, 2).Value = summary.TotalBranches;
            ws.Cell(4, 1).Value = "Active Developers"; ws.Cell(4, 2).Value = summary.ActiveDevelopers;
            ws.Cell(5, 1).Value = "Commits Today"; ws.Cell(5, 2).Value = summary.CommitsToday;
            ws.Cell(6, 1).Value = "Commits (Month)"; ws.Cell(6, 2).Value = summary.CommitsThisMonth;
            ws.Cell(7, 1).Value = "Stale Branches"; ws.Cell(7, 2).Value = summary.StaleBranches;
            ws.Cell(8, 1).Value = "Average Branch Age (Days)"; ws.Cell(8, 2).Value = Math.Round(summary.AverageBranchAgeDays, 2);
            ws.Cell(9, 1).Value = "Most Active Developer"; ws.Cell(9, 2).Value = summary.MostActiveDeveloper;

            var rngTable = ws.Range(1, 1, 9, 2);
            rngTable.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            rngTable.FirstColumn().Style.Font.Bold = true;
            rngTable.FirstColumn().Style.Fill.BackgroundColor = XLColor.LightGray;

            // Conditional formatting for stale branches
            ws.Cell(7, 2).AddConditionalFormat().WhenGreaterThan(5).Fill.SetBackgroundColor(XLColor.Red);

            ws.Columns().AdjustToContents();
        }

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
    }
}
