using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using GitAnalyticsDashboard.Models;

namespace GitAnalyticsDashboard.Services
{
    public class HtmlReportGenerator
    {
        public void Generate(ReportData data, string outputDirectory, string reportTitle)
        {
            Directory.CreateDirectory(outputDirectory);
            var assetsDir = Path.Combine(outputDirectory, "assets");
            Directory.CreateDirectory(Path.Combine(assetsDir, "css"));
            Directory.CreateDirectory(Path.Combine(assetsDir, "js"));

            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html lang=\"en\">");
            html.AppendLine("<head>");
            html.AppendLine("    <meta charset=\"UTF-8\">");
            html.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            html.AppendLine($"    <title>{E(reportTitle)}</title>");
            html.AppendLine("    <link href=\"https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/css/bootstrap.min.css\" rel=\"stylesheet\">");
            html.AppendLine("    <link href=\"https://cdn.datatables.net/1.13.4/css/dataTables.bootstrap5.min.css\" rel=\"stylesheet\">");
            html.AppendLine("    <link href=\"https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.4.0/css/all.min.css\" rel=\"stylesheet\">");
            html.AppendLine("    <link rel=\"stylesheet\" href=\"https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.7.0/styles/github.min.css\">");
            html.AppendLine("    <link rel=\"stylesheet\" type=\"text/css\" href=\"https://cdn.jsdelivr.net/npm/diff2html/bundles/css/diff2html.min.css\" />");
            html.AppendLine("    <style>");
            html.AppendLine("        body { background-color: #f8f9fa; }");
            html.AppendLine("        .card { margin-bottom: 1.5rem; border: none; box-shadow: 0 0.125rem 0.25rem rgba(0, 0, 0, 0.075); }");
            html.AppendLine("        .kpi-card { border-left: 4px solid #0d6efd; }");
            html.AppendLine("        .nav-link { color: #495057; }");
            html.AppendLine("        .nav-link.active { font-weight: bold; border-bottom: 2px solid #0d6efd; }");
            html.AppendLine("        .diff-container { margin-top: 20px; }");
            html.AppendLine("        .file-header { position: sticky; top: 0; z-index: 100; background: #fff; border-bottom: 1px solid #ddd; padding: 10px; }");
            html.AppendLine("    </style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");

            html.AppendLine("    <nav class=\"navbar navbar-expand-lg navbar-dark bg-dark mb-4\">");
            html.AppendLine("        <div class=\"container-fluid\">");
            html.AppendLine($"            <a class=\"navbar-brand\" href=\"#\"><i class=\"fas fa-chart-line me-2\"></i>{E(reportTitle)} - {E(data.Summary.RepositoryName)}</a>");
            html.AppendLine("        </div>");
            html.AppendLine("    </nav>");

            html.AppendLine("    <div class=\"container-fluid\">");

            var summary = data.Summary;
            if (summary.IsScopedScan)
            {
                html.AppendLine(GenerateScanBanner(summary));
            }

            // KPI Cards
            html.AppendLine("        <div class=\"row\">");
            html.AppendLine(GenerateKpiCard("Total Branches", data.Summary.TotalBranches.ToString(), "fa-code-branch", "text-primary"));
            html.AppendLine(GenerateKpiCard("Active Developers", data.Summary.ActiveDevelopers.ToString(), "fa-users", "text-success"));
            html.AppendLine(GenerateKpiCard(summary.IsScopedScan ? "Commits Scanned" : "Total Commits", data.Summary.TotalCommits.ToString(), "fa-history", "text-info"));
            html.AppendLine(GenerateKpiCard("Commits (Month)", data.Summary.CommitsThisMonth.ToString(), "fa-calendar-alt", "text-warning"));
            html.AppendLine(GenerateKpiCard("Stale Branches", data.Summary.StaleBranches.ToString(), "fa-clock", "text-danger"));
            html.AppendLine(GenerateKpiCard("Merged Branches", data.Summary.MergedBranches.ToString(), "fa-check-circle", "text-secondary"));
            html.AppendLine("        </div>");

            if (summary.IsScopedScan)
            {
                html.AppendLine("        <div class=\"row\">");
                html.AppendLine(GenerateKpiCard("File Changes", summary.TotalFileChanges.ToString(), "fa-file-alt", "text-primary"));
                html.AppendLine(GenerateKpiCard("Files Touched", summary.UniqueFilesChanged.ToString(), "fa-folder-open", "text-info"));
                html.AppendLine(GenerateKpiCard("Added", summary.FilesAdded.ToString(), "fa-plus-circle", "text-success"));
                html.AppendLine(GenerateKpiCard("Modified", summary.FilesModified.ToString(), "fa-edit", "text-primary"));
                html.AppendLine(GenerateKpiCard("Deleted", summary.FilesDeleted.ToString(), "fa-minus-circle", "text-danger"));
                html.AppendLine(GenerateKpiCard("Renamed", summary.FilesRenamed.ToString(), "fa-exchange-alt", "text-warning"));
                html.AppendLine("        </div>");
            }

            // Charts Row
            html.AppendLine("        <div class=\"row\">");
            html.AppendLine("            <div class=\"col-md-6\">");
            html.AppendLine("                <div class=\"card\"><div class=\"card-body\"><h5 class=\"card-title\">Commits per Developer</h5><canvas id=\"devChart\"></canvas></div></div>");
            html.AppendLine("            </div>");
            html.AppendLine("            <div class=\"col-md-6\">");
            html.AppendLine("                <div class=\"card\"><div class=\"card-body\"><h5 class=\"card-title\">File Hotspots (Commits)</h5><canvas id=\"fileChart\"></canvas></div></div>");
            html.AppendLine("            </div>");
            html.AppendLine("        </div>");

            // Tables
            html.AppendLine("        <div class=\"card\">");
            html.AppendLine("            <div class=\"card-body\">");
            html.AppendLine("                <ul class=\"nav nav-tabs mb-3\" id=\"reportTabs\" role=\"tablist\">");
            html.AppendLine("                    <li class=\"nav-item\"><button class=\"nav-link active\" id=\"branches-tab\" data-bs-toggle=\"tab\" data-bs-target=\"#branches\" type=\"button\">Branches</button></li>");
            html.AppendLine("                    <li class=\"nav-item\"><button class=\"nav-link\" id=\"devs-tab\" data-bs-toggle=\"tab\" data-bs-target=\"#devs\" type=\"button\">Developers</button></li>");
            html.AppendLine($"                    <li class=\"nav-item\"><button class=\"nav-link\" id=\"commits-tab\" data-bs-toggle=\"tab\" data-bs-target=\"#commits\" type=\"button\">{(summary.IsScopedScan ? "Scanned Commits" : "Recent Commits")}</button></li>");
            if (data.FileChanges.Any())
            {
                html.AppendLine("                    <li class=\"nav-item\"><button class=\"nav-link\" id=\"filechanges-tab\" data-bs-toggle=\"tab\" data-bs-target=\"#filechanges\" type=\"button\">File Changes</button></li>");
            }
            if (data.Modules.Any())
            {
                html.AppendLine("                    <li class=\"nav-item\"><button class=\"nav-link\" id=\"modules-tab\" data-bs-toggle=\"tab\" data-bs-target=\"#modules\" type=\"button\">Modules</button></li>");
            }
            if (data.ReleaseNotes != null && data.ReleaseNotes.Sections.Any())
            {
                html.AppendLine("                    <li class=\"nav-item\"><button class=\"nav-link\" id=\"release-tab\" data-bs-toggle=\"tab\" data-bs-target=\"#release\" type=\"button\">Release Notes</button></li>");
            }
            html.AppendLine("                    <li class=\"nav-item\"><button class=\"nav-link\" id=\"comparison-tab\" data-bs-toggle=\"tab\" data-bs-target=\"#comparison\" type=\"button\">Before vs After Comparison</button></li>");
            html.AppendLine("                </ul>");

            html.AppendLine("                <div class=\"tab-content\" id=\"reportTabsContent\">");

            // Branches Table
            html.AppendLine("                    <div class=\"tab-pane fade show active\" id=\"branches\" role=\"tabpanel\">");
            html.AppendLine("                        <table id=\"branchesTable\" class=\"table table-striped w-100\">");
            html.AppendLine("                            <thead><tr><th>Name</th><th>Latest Commit</th><th>Author</th><th>Date</th><th>Commits</th><th>Status</th></tr></thead>");
            html.AppendLine("                            <tbody>");
            foreach (var b in data.Branches)
            {
                var status = b.IsStale ? "<span class=\"badge bg-danger\">Stale</span>" : "<span class=\"badge bg-success\">Active</span>";
                if (b.IsMerged) status += " <span class=\"badge bg-info\">Merged</span>";
                html.AppendLine($"<tr><td>{E(b.Name)}</td><td><code>{b.LatestCommitSha[..7]}</code> {E(b.LatestCommitMessage)}</td><td>{E(b.LatestCommitAuthor)}</td><td>{b.LatestCommitDate:yyyy-MM-dd}</td><td>{b.CommitCount}</td><td>{status}</td></tr>");
            }
            html.AppendLine("                            </tbody>");
            html.AppendLine("                        </table>");
            html.AppendLine("                    </div>");

            // Developers Table
            html.AppendLine("                    <div class=\"tab-pane fade\" id=\"devs\" role=\"tabpanel\">");
            html.AppendLine("                        <table id=\"devsTable\" class=\"table table-striped w-100\">");
            html.AppendLine("                            <thead><tr><th>Developer</th><th>Commits</th><th>Lines Added</th><th>Lines Removed</th><th>Files Modified</th><th>Last Commit</th></tr></thead>");
            html.AppendLine("                            <tbody>");
            foreach (var d in data.Developers)
            {
                html.AppendLine($"<tr><td>{E(d.Name)}</td><td>{d.TotalCommits}</td><td>{d.LinesAdded}</td><td>{d.LinesRemoved}</td><td>{d.FilesModified}</td><td>{d.LatestCommit:yyyy-MM-dd}</td></tr>");
            }
            html.AppendLine("                            </tbody>");
            html.AppendLine("                        </table>");
            html.AppendLine("                    </div>");

            // Commits Table
            html.AppendLine("                    <div class=\"tab-pane fade\" id=\"commits\" role=\"tabpanel\">");
            html.AppendLine("                        <table id=\"commitsTable\" class=\"table table-striped w-100\">");
            html.AppendLine("                            <thead><tr><th>SHA</th><th>Author</th><th>Date</th><th>Message</th><th>Branch</th><th>Files</th><th>+</th><th>-</th></tr></thead>");
            html.AppendLine("                            <tbody>");
            foreach (var c in data.RecentCommits)
            {
                var mergeBadge = c.IsMerge ? " <span class=\"badge bg-secondary\">Merge</span>" : string.Empty;
                html.AppendLine($"<tr><td><code>{c.Sha[..7]}</code></td><td>{E(c.Author)}</td><td>{c.Date:yyyy-MM-dd HH:mm}</td><td>{E(c.Message)}{mergeBadge}</td><td>{E(c.Branch)}</td><td>{c.FilesChanged}</td><td class=\"text-success\">{c.Insertions}</td><td class=\"text-danger\">{c.Deletions}</td></tr>");
            }
            html.AppendLine("                            </tbody>");
            html.AppendLine("                        </table>");
            html.AppendLine("                    </div>");

            // File Changes Table
            if (data.FileChanges.Any())
            {
                html.AppendLine("                    <div class=\"tab-pane fade\" id=\"filechanges\" role=\"tabpanel\">");
                html.AppendLine("                        <table id=\"fileChangesTable\" class=\"table table-striped table-sm w-100\">");
                html.AppendLine("                            <thead><tr><th>Date</th><th>File</th><th>Change</th><th>Author</th><th>Commit</th><th>Branch</th><th>Message</th><th>+</th><th>-</th></tr></thead>");
                html.AppendLine("                            <tbody>");
                foreach (var f in data.FileChanges)
                {
                    var fileCell = string.IsNullOrEmpty(f.OldPath) ? E(f.FilePath) : $"{E(f.OldPath)} &rarr; {E(f.FilePath)}";
                    html.AppendLine($"<tr><td data-order=\"{f.ChangeDate.UtcDateTime:o}\">{f.ChangeDate:yyyy-MM-dd HH:mm}</td><td>{fileCell}</td><td>{GenerateChangeBadge(f.ChangeType)}</td><td>{E(f.AuthorName)}</td><td><code>{ShortSha(f.CommitHash)}</code></td><td>{E(f.Branch)}</td><td>{E(f.CommitMessage)}</td><td class=\"text-success\">{f.LinesAdded}</td><td class=\"text-danger\">{f.LinesDeleted}</td></tr>");
                }
                html.AppendLine("                            </tbody>");
                html.AppendLine("                        </table>");
                html.AppendLine("                    </div>");
            }

            // Modules Table
            if (data.Modules.Any())
            {
                html.AppendLine("                    <div class=\"tab-pane fade\" id=\"modules\" role=\"tabpanel\">");
                html.AppendLine("                        <div style=\"height: 320px\" class=\"mb-3\"><canvas id=\"moduleChart\"></canvas></div>");
                html.AppendLine("                        <table id=\"modulesTable\" class=\"table table-striped table-sm w-100\">");
                html.AppendLine("                            <thead><tr><th>Module</th><th>Project</th><th>Commits</th><th>File Changes</th><th>Files</th><th>Contributors</th><th>Added</th><th>Modified</th><th>Deleted</th><th>Renamed</th><th>+</th><th>-</th></tr></thead>");
                html.AppendLine("                            <tbody>");
                foreach (var m in data.Modules)
                {
                    html.AppendLine($"<tr><td>{E(m.Module)}</td><td>{E(m.ProjectFile)}</td><td>{m.Commits}</td><td>{m.FileChanges}</td><td>{m.FilesTouched}</td><td>{m.Contributors}</td><td>{m.Added}</td><td>{m.Modified}</td><td>{m.Deleted}</td><td>{m.Renamed}</td><td class=\"text-success\">{m.LinesAdded}</td><td class=\"text-danger\">{m.LinesDeleted}</td></tr>");
                }
                html.AppendLine("                            </tbody>");
                html.AppendLine("                        </table>");
                html.AppendLine("                    </div>");
            }

            // Release Notes
            if (data.ReleaseNotes != null && data.ReleaseNotes.Sections.Any())
            {
                html.AppendLine("                    <div class=\"tab-pane fade\" id=\"release\" role=\"tabpanel\">");
                html.AppendLine(GenerateReleaseNotes(data.ReleaseNotes));
                html.AppendLine("                    </div>");
            }

            // Comparison Tab
            html.AppendLine("                    <div class=\"tab-pane fade\" id=\"comparison\" role=\"tabpanel\">");
            html.AppendLine("                        <div class=\"row mb-3\">");
            html.AppendLine("                            <div class=\"col-md-6\">");
            html.AppendLine("                                <input type=\"text\" id=\"comparisonSearch\" class=\"form-control\" placeholder=\"Search by Commit, File, Method, API...\">");
            html.AppendLine("                            </div>");
            html.AppendLine("                            <div class=\"col-md-6 d-flex align-items-center justify-content-end\">");
            html.AppendLine("                                <div class=\"btn-group\" role=\"group\">");
            html.AppendLine("                                    <input type=\"checkbox\" class=\"btn-check filter-btn\" id=\"filter-Added\" checked>");
            html.AppendLine("                                    <label class=\"btn btn-outline-success\" for=\"filter-Added\">Added</label>");
            html.AppendLine("                                    <input type=\"checkbox\" class=\"btn-check filter-btn\" id=\"filter-Modified\" checked>");
            html.AppendLine("                                    <label class=\"btn btn-outline-primary\" for=\"filter-Modified\">Modified</label>");
            html.AppendLine("                                    <input type=\"checkbox\" class=\"btn-check filter-btn\" id=\"filter-Deleted\" checked>");
            html.AppendLine("                                    <label class=\"btn btn-outline-danger\" for=\"filter-Deleted\">Deleted</label>");
            html.AppendLine("                                    <input type=\"checkbox\" class=\"btn-check filter-btn\" id=\"filter-Renamed\" checked>");
            html.AppendLine("                                    <label class=\"btn btn-outline-warning\" for=\"filter-Renamed\">Renamed</label>");
            html.AppendLine("                                </div>");
            html.AppendLine("                            </div>");
            html.AppendLine("                        </div>");
            html.AppendLine("                        <div id=\"comparison-list\">");
            // Diffs live in assets/js/diffs.js and are rendered when a file is expanded, so the page stays light
            var diffs = new Dictionary<string, string>();
            for (var commitIndex = 0; commitIndex < data.Comparisons.Count; commitIndex++)
            {
                var commit = data.Comparisons[commitIndex];
                html.AppendLine($"<div class=\"commit-item\" data-sha=\"{commit.Sha}\" data-message=\"{WebUtility.HtmlEncode(commit.Message)}\">");
                html.AppendLine($"  <div class=\"card mb-3\">");
                html.AppendLine($"    <div class=\"card-header bg-light d-flex justify-content-between align-items-center\">");
                html.AppendLine($"      <span><strong>{WebUtility.HtmlEncode(commit.Message)}</strong> <code class=\"ms-2\">{commit.Sha[..7]}</code></span>");
                html.AppendLine($"      <span class=\"text-muted small\">{WebUtility.HtmlEncode(commit.Author)} on {commit.Date:yyyy-MM-dd HH:mm}</span>");
                html.AppendLine($"    </div>");
                html.AppendLine($"    <div class=\"card-body\">");
                html.AppendLine($"      <pre class=\"small\">{WebUtility.HtmlEncode(commit.Summary)}</pre>");

                for (var fileIndex = 0; fileIndex < commit.FileChanges.Count; fileIndex++)
                {
                    var file = commit.FileChanges[fileIndex];
                    var fileId = $"file-{commitIndex}-{fileIndex}";
                    var hasDiff = !string.IsNullOrEmpty(file.Diff);
                    if (hasDiff)
                    {
                        diffs[fileId] = file.Diff;
                    }

                    var flags = file.IsBinary ? " <span class=\"badge bg-dark\">Binary</span>" : file.IsTruncated ? " <span class=\"badge bg-warning text-dark\">Truncated</span>" : string.Empty;
                    html.AppendLine($"      <div class=\"file-comparison mb-3\" data-path=\"{WebUtility.HtmlEncode(file.Path)}\" data-summary=\"{WebUtility.HtmlEncode(file.Summary)}\" data-type=\"{file.ChangeType}\">");
                    html.AppendLine($"        <div class=\"file-header d-flex justify-content-between align-items-center\">");
                    html.AppendLine($"          <span><i class=\"far fa-file-code me-2\"></i>{WebUtility.HtmlEncode(file.Path)} <span class=\"badge bg-secondary\">{file.ChangeType}</span>{flags}</span>");
                    html.AppendLine($"          <div>");
                    html.AppendLine($"            <button class=\"btn btn-sm btn-outline-primary\" type=\"button\" data-bs-toggle=\"collapse\" data-bs-target=\"#{fileId}\">Expand/Collapse</button>");
                    if (hasDiff)
                    {
                        html.AppendLine($"            <button class=\"btn btn-sm btn-outline-secondary btn-copy-diff\" data-diff-key=\"{fileId}\">Copy Diff</button>");
                    }
                    html.AppendLine($"          </div>");
                    html.AppendLine($"        </div>");
                    html.AppendLine($"        <div class=\"collapse\" id=\"{fileId}\">");
                    html.AppendLine($"          <div class=\"mt-2 p-2 bg-light border-start border-4 border-info\"><strong>Summary:</strong> {WebUtility.HtmlEncode(file.Summary)}</div>");
                    html.AppendLine($"          <div class=\"diff-viewer\" data-diff-key=\"{fileId}\"></div>");
                    html.AppendLine($"        </div>");
                    html.AppendLine($"      </div>");
                }

                html.AppendLine($"    </div>");
                html.AppendLine($"  </div>");
                html.AppendLine($"</div>");
            }
            html.AppendLine("                        </div>");
            html.AppendLine("                    </div>");

            html.AppendLine("                </div>"); // tab-content
            html.AppendLine("            </div>"); // card-body
            html.AppendLine("        </div>"); // card

            html.AppendLine("    </div>"); // container

            html.AppendLine("    <script src=\"https://code.jquery.com/jquery-3.6.0.min.js\"></script>");
            html.AppendLine("    <script src=\"https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/js/bootstrap.bundle.min.js\"></script>");
            html.AppendLine("    <script src=\"https://cdn.datatables.net/1.13.4/js/jquery.dataTables.min.js\"></script>");
            html.AppendLine("    <script src=\"https://cdn.datatables.net/1.13.4/js/dataTables.bootstrap5.min.js\"></script>");
            html.AppendLine("    <script src=\"https://cdn.jsdelivr.net/npm/chart.js\"></script>");
            html.AppendLine("    <script src=\"https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.7.0/highlight.min.js\"></script>");
            html.AppendLine("    <script type=\"text/javascript\" src=\"https://cdn.jsdelivr.net/npm/diff2html/bundles/js/diff2html-ui.min.js\"></script>");
            html.AppendLine("    <script src=\"assets/js/diffs.js\"></script>");

            html.AppendLine("    <script>");
            html.AppendLine("        $(document).ready(function() {");
            html.AppendLine("            $('table').DataTable({ pageLength: 10, responsive: true, order: [] });");

            // Diff Rendering (lazy, on first expand)
            html.AppendLine("            document.addEventListener('shown.bs.collapse', function(e) {");
            html.AppendLine("                var viewer = e.target.querySelector('.diff-viewer');");
            html.AppendLine("                if (!viewer || viewer.dataset.rendered) return;");
            html.AppendLine("                viewer.dataset.rendered = '1';");
            html.AppendLine("                var diffString = (window.GIT_DIFFS || {})[viewer.dataset.diffKey];");
            html.AppendLine("                if (!diffString) { viewer.innerHTML = '<p class=\"text-muted fst-italic mt-2\">No text diff available.</p>'; return; }");
            html.AppendLine("                var diff2htmlUi = new Diff2HtmlUI(viewer, diffString, {");
            html.AppendLine("                    drawFileList: false,");
            html.AppendLine("                    matching: 'lines',");
            html.AppendLine("                    outputFormat: 'side-by-side',");
            html.AppendLine("                    highlight: true,");
            html.AppendLine("                    renderNothingWhenEmpty: false");
            html.AppendLine("                });");
            html.AppendLine("                diff2htmlUi.draw();");
            html.AppendLine("                diff2htmlUi.highlightCode();");
            html.AppendLine("            });");

            // Search and Filter functionality
            html.AppendLine("            function applyFilters() {");
            html.AppendLine("                var searchValue = $('#comparisonSearch').val().toLowerCase();");
            html.AppendLine("                var addedEnabled = $('#filter-Added').is(':checked');");
            html.AppendLine("                var modifiedEnabled = $('#filter-Modified').is(':checked');");
            html.AppendLine("                var deletedEnabled = $('#filter-Deleted').is(':checked');");
            html.AppendLine("                var renamedEnabled = $('#filter-Renamed').is(':checked');");
            html.AppendLine("                $('.commit-item').each(function() {");
            html.AppendLine("                    var showCommit = $(this).data('sha').toLowerCase().indexOf(searchValue) > -1 || $(this).data('message').toLowerCase().indexOf(searchValue) > -1;");
            html.AppendLine("                    var anyFileMatch = false;");
            html.AppendLine("                    $(this).find('.file-comparison').each(function() {");
            html.AppendLine("                        var type = $(this).data('type');");
            html.AppendLine("                        var typeEnabled = (type === 'Added' && addedEnabled) || (type === 'Modified' && modifiedEnabled) || (type === 'Deleted' && deletedEnabled) || (type === 'Renamed' && renamedEnabled) || (type !== 'Added' && type !== 'Modified' && type !== 'Deleted' && type !== 'Renamed');");
            html.AppendLine("                        var searchMatch = $(this).data('path').toLowerCase().indexOf(searchValue) > -1 || $(this).data('summary').toLowerCase().indexOf(searchValue) > -1;");
            html.AppendLine("                        if (typeEnabled && (showCommit || searchMatch)) { $(this).show(); anyFileMatch = true; } else { $(this).hide(); }");
            html.AppendLine("                    });");
            html.AppendLine("                    if (anyFileMatch) { $(this).show(); } else { $(this).hide(); }");
            html.AppendLine("                });");
            html.AppendLine("            }");
            html.AppendLine("            $('#comparisonSearch').on('keyup', applyFilters);");
            html.AppendLine("            $('.filter-btn').on('change', applyFilters);");

            // Copy Diff
            html.AppendLine("            $('.btn-copy-diff').on('click', function() {");
            html.AppendLine("                var diffText = (window.GIT_DIFFS || {})[$(this).data('diff-key')] || '';");
            html.AppendLine("                navigator.clipboard.writeText(diffText).then(function() { alert('Diff copied to clipboard!'); });");
            html.AppendLine("            });");

            // Dev Chart
            var devLabels = JsonSerializer.Serialize(data.Developers.Take(10).Select(d => d.Name));
            var devData = JsonSerializer.Serialize(data.Developers.Take(10).Select(d => d.TotalCommits));
            html.AppendLine($"            new Chart(document.getElementById('devChart'), {{ type: 'bar', data: {{ labels: {devLabels}, datasets: [{{ label: 'Commits', data: {devData}, backgroundColor: 'rgba(13, 110, 253, 0.5)' }}] }} }});");

            // File Chart
            var fileLabels = JsonSerializer.Serialize(data.FileHotspots.Take(10).Select(f => Path.GetFileName(f.Path)));
            var fileData = JsonSerializer.Serialize(data.FileHotspots.Take(10).Select(f => f.CommitCount));
            html.AppendLine($"            new Chart(document.getElementById('fileChart'), {{ type: 'pie', data: {{ labels: {fileLabels}, datasets: [{{ label: 'Commits', data: {fileData}, backgroundColor: ['#FF6384', '#36A2EB', '#FFCE56', '#4BC0C0', '#9966FF', '#FF9F40', '#C9CBCF', '#46BFBD', '#FDB45C', '#949FB1'] }}] }} }});");

            // Module Chart (churn per module)
            if (data.Modules.Any())
            {
                var topModules = data.Modules.Take(15).ToList();
                var moduleLabels = JsonSerializer.Serialize(topModules.Select(m => m.Module));
                var moduleAdded = JsonSerializer.Serialize(topModules.Select(m => m.LinesAdded));
                var moduleDeleted = JsonSerializer.Serialize(topModules.Select(m => m.LinesDeleted));
                html.AppendLine($"            new Chart(document.getElementById('moduleChart'), {{ type: 'bar', data: {{ labels: {moduleLabels}, datasets: [{{ label: 'Lines added', data: {moduleAdded}, backgroundColor: 'rgba(25, 135, 84, 0.6)' }}, {{ label: 'Lines deleted', data: {moduleDeleted}, backgroundColor: 'rgba(220, 53, 69, 0.6)' }}] }}, options: {{ indexAxis: 'y', maintainAspectRatio: false, scales: {{ x: {{ stacked: true }}, y: {{ stacked: true }} }} }} }});");
            }

            html.AppendLine("        });");
            html.AppendLine("    </script>");

            html.AppendLine("</body>");
            html.AppendLine("</html>");

            File.WriteAllText(Path.Combine(outputDirectory, "index.html"), html.ToString());
            File.WriteAllText(Path.Combine(assetsDir, "js", "diffs.js"), $"window.GIT_DIFFS = {JsonSerializer.Serialize(diffs)};\n");
        }

        private string GenerateReleaseNotes(ReleaseNotes notes)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<p class=\"text-muted\">{notes.EntryCount} commits from <code>{E(notes.StartRef)}</code> to <code>{E(notes.TargetRef)}</code>. Also written to <code>CHANGELOG.md</code>.</p>");

            foreach (var section in notes.Sections)
            {
                var headerClass = section.Key == ReleaseNoteSection.BreakingKey ? "text-danger" : string.Empty;
                sb.AppendLine($"<h5 class=\"mt-3 {headerClass}\">{E(section.Title)} <span class=\"badge bg-light text-dark\">{section.Entries.Count}</span></h5>");
                sb.AppendLine("<ul class=\"list-unstyled ms-2\">");
                foreach (var entry in section.Entries)
                {
                    var scope = string.IsNullOrEmpty(entry.Scope) ? string.Empty : $"<strong>{E(entry.Scope)}:</strong> ";
                    var tickets = string.Join(" ", entry.Tickets.Select(t => string.IsNullOrEmpty(t.Url)
                        ? $"<span class=\"badge bg-info text-dark\">{E(t.Id)}</span>"
                        : $"<a class=\"badge bg-info text-dark text-decoration-none\" href=\"{E(t.Url)}\" target=\"_blank\" rel=\"noopener\">{E(t.Id)}</a>"));
                    sb.AppendLine($"<li class=\"mb-1\">{scope}{E(entry.Description)} <code class=\"small\">{ShortSha(entry.Sha)}</code> <span class=\"text-muted small\">{E(entry.Author)}</span> {tickets}");
                    if (section.Key == ReleaseNoteSection.BreakingKey && !string.IsNullOrEmpty(entry.BreakingNote))
                    {
                        sb.AppendLine($"<div class=\"small text-danger ms-3\">{E(entry.BreakingNote)}</div>");
                    }
                    sb.AppendLine("</li>");
                }
                sb.AppendLine("</ul>");
            }

            if (notes.Contributors.Any())
            {
                sb.AppendLine("<h5 class=\"mt-3\">Contributors</h5>");
                sb.AppendLine($"<p>{string.Join(", ", notes.Contributors.Select(c => $"{E(c.Name)} ({c.Commits})"))}</p>");
            }

            return sb.ToString();
        }

        private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

        private static string ShortSha(string sha) => sha.Length > 7 ? sha[..7] : sha;

        private string GenerateScanBanner(RepositorySummary summary)
        {
            var sb = new StringBuilder();
            sb.AppendLine("        <div class=\"alert alert-secondary\">");
            sb.AppendLine($"            <strong><i class=\"fas fa-code-branch me-2\"></i>Scan range:</strong> <code>{E(summary.StartingRef)}</code> ({ShortSha(summary.StartingCommitSha)}) &rarr; <code>{E(summary.TargetRef)}</code> ({ShortSha(summary.TargetCommitSha)})");
            sb.AppendLine($"            &middot; {summary.TotalCommits} commits ({summary.MergeCommits} merges) &middot; {summary.TotalFileChanges} file changes across {summary.UniqueFilesChanged} files");
            sb.AppendLine($"            &middot; <span class=\"text-success\">+{summary.TotalLinesAdded}</span> / <span class=\"text-danger\">-{summary.TotalLinesDeleted}</span> lines");
            if (summary.ActiveFilters.Any())
            {
                sb.AppendLine($"            <div class=\"mt-1 small\"><i class=\"fas fa-filter me-1\"></i>Filters: {E(string.Join("; ", summary.ActiveFilters))} &middot; filtered out {summary.FilteredOutCommits} commits and {summary.ExcludedFileChanges} file changes</div>");
            }
            if (!summary.StartIsAncestorOfTarget)
            {
                sb.AppendLine($"            <div class=\"mt-2 text-danger\"><i class=\"fas fa-exclamation-triangle me-1\"></i>{E(summary.StartingRef)} is not an ancestor of {E(summary.TargetRef)}; the scan covers commits on {E(summary.TargetRef)} that are not in the history before {E(summary.StartingRef)}.</div>");
            }
            sb.Append("        </div>");
            return sb.ToString();
        }

        private static string GenerateChangeBadge(string changeType)
        {
            var color = changeType switch
            {
                "Added" => "bg-success",
                "Deleted" => "bg-danger",
                "Renamed" => "bg-warning text-dark",
                "Modified" => "bg-primary",
                _ => "bg-secondary"
            };
            return $"<span class=\"badge {color}\">{E(changeType)}</span>";
        }

        private string GenerateKpiCard(string title, string value, string icon, string colorClass)
        {
            return $@"
                <div class=""col-md-2"">
                    <div class=""card kpi-card"">
                        <div class=""card-body"">
                            <div class=""d-flex align-items-center"">
                                <div class=""flex-grow-1"">
                                    <div class=""text-muted small"">{title}</div>
                                    <div class=""h4 mb-0"">{value}</div>
                                </div>
                                <div class=""flex-shrink-0"">
                                    <i class=""fas {icon} fa-2x {colorClass} opacity-25""></i>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>";
        }
    }
}
