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
                AddComparisonSheet(workbook, data.Comparisons);

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
