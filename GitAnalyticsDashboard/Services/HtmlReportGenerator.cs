using System;
using System.IO;
using System.Linq;
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
            html.AppendLine($"    <title>{reportTitle}</title>");
            html.AppendLine("    <link href=\"https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/css/bootstrap.min.css\" rel=\"stylesheet\">");
            html.AppendLine("    <link href=\"https://cdn.datatables.net/1.13.4/css/dataTables.bootstrap5.min.css\" rel=\"stylesheet\">");
            html.AppendLine("    <link href=\"https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.4.0/css/all.min.css\" rel=\"stylesheet\">");
            html.AppendLine("    <style>");
            html.AppendLine("        body { background-color: #f8f9fa; }");
            html.AppendLine("        .card { margin-bottom: 1.5rem; border: none; box-shadow: 0 0.125rem 0.25rem rgba(0, 0, 0, 0.075); }");
            html.AppendLine("        .kpi-card { border-left: 4px solid #0d6efd; }");
            html.AppendLine("        .nav-link { color: #495057; }");
            html.AppendLine("        .nav-link.active { font-weight: bold; border-bottom: 2px solid #0d6efd; }");
            html.AppendLine("    </style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");

            html.AppendLine("    <nav class=\"navbar navbar-expand-lg navbar-dark bg-dark mb-4\">");
            html.AppendLine("        <div class=\"container-fluid\">");
            html.AppendLine($"            <a class=\"navbar-brand\" href=\"#\"><i class=\"fas fa-chart-line me-2\"></i>{reportTitle} - {data.Summary.RepositoryName}</a>");
            html.AppendLine("        </div>");
            html.AppendLine("    </nav>");

            html.AppendLine("    <div class=\"container-fluid\">");

            // KPI Cards
            html.AppendLine("        <div class=\"row\">");
            html.AppendLine(GenerateKpiCard("Total Branches", data.Summary.TotalBranches.ToString(), "fa-code-branch", "text-primary"));
            html.AppendLine(GenerateKpiCard("Active Developers", data.Summary.ActiveDevelopers.ToString(), "fa-users", "text-success"));
            html.AppendLine(GenerateKpiCard("Total Commits", data.Summary.TotalCommits.ToString(), "fa-history", "text-info"));
            html.AppendLine(GenerateKpiCard("Commits (Month)", data.Summary.CommitsThisMonth.ToString(), "fa-calendar-alt", "text-warning"));
            html.AppendLine(GenerateKpiCard("Stale Branches", data.Summary.StaleBranches.ToString(), "fa-clock", "text-danger"));
            html.AppendLine(GenerateKpiCard("Merged Branches", data.Summary.MergedBranches.ToString(), "fa-check-circle", "text-secondary"));
            html.AppendLine("        </div>");

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
            html.AppendLine("                    <li class=\"nav-item\"><button class=\"nav-link\" id=\"commits-tab\" data-bs-toggle=\"tab\" data-bs-target=\"#commits\" type=\"button\">Recent Commits</button></li>");
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
                html.AppendLine($"<tr><td>{b.Name}</td><td><code>{b.LatestCommitSha[..7]}</code> {b.LatestCommitMessage}</td><td>{b.LatestCommitAuthor}</td><td>{b.LatestCommitDate:yyyy-MM-dd}</td><td>{b.CommitCount}</td><td>{status}</td></tr>");
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
                html.AppendLine($"<tr><td>{d.Name}</td><td>{d.TotalCommits}</td><td>{d.LinesAdded}</td><td>{d.LinesRemoved}</td><td>{d.FilesModified}</td><td>{d.LatestCommit:yyyy-MM-dd}</td></tr>");
            }
            html.AppendLine("                            </tbody>");
            html.AppendLine("                        </table>");
            html.AppendLine("                    </div>");

            // Commits Table
            html.AppendLine("                    <div class=\"tab-pane fade\" id=\"commits\" role=\"tabpanel\">");
            html.AppendLine("                        <table id=\"commitsTable\" class=\"table table-striped w-100\">");
            html.AppendLine("                            <thead><tr><th>SHA</th><th>Author</th><th>Date</th><th>Message</th><th>+</th><th>-</th></tr></thead>");
            html.AppendLine("                            <tbody>");
            foreach (var c in data.RecentCommits)
            {
                html.AppendLine($"<tr><td><code>{c.Sha[..7]}</code></td><td>{c.Author}</td><td>{c.Date:yyyy-MM-dd HH:mm}</td><td>{c.Message}</td><td class=\"text-success\">{c.Insertions}</td><td class=\"text-danger\">{c.Deletions}</td></tr>");
            }
            html.AppendLine("                            </tbody>");
            html.AppendLine("                        </table>");
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

            html.AppendLine("    <script>");
            html.AppendLine("        $(document).ready(function() {");
            html.AppendLine("            $('table').DataTable({ pageLength: 10, responsive: true });");

            // Dev Chart
            var devLabels = JsonSerializer.Serialize(data.Developers.Take(10).Select(d => d.Name));
            var devData = JsonSerializer.Serialize(data.Developers.Take(10).Select(d => d.TotalCommits));
            html.AppendLine($"            new Chart(document.getElementById('devChart'), {{ type: 'bar', data: {{ labels: {devLabels}, datasets: [{{ label: 'Commits', data: {devData}, backgroundColor: 'rgba(13, 110, 253, 0.5)' }}] }} }});");

            // File Chart
            var fileLabels = JsonSerializer.Serialize(data.FileHotspots.Take(10).Select(f => Path.GetFileName(f.Path)));
            var fileData = JsonSerializer.Serialize(data.FileHotspots.Take(10).Select(f => f.CommitCount));
            html.AppendLine($"            new Chart(document.getElementById('fileChart'), {{ type: 'pie', data: {{ labels: {fileLabels}, datasets: [{{ label: 'Commits', data: {fileData}, backgroundColor: ['#FF6384', '#36A2EB', '#FFCE56', '#4BC0C0', '#9966FF', '#FF9F40', '#C9CBCF', '#46BFBD', '#FDB45C', '#949FB1'] }}] }} }});");

            html.AppendLine("        });");
            html.AppendLine("    </script>");

            html.AppendLine("</body>");
            html.AppendLine("</html>");

            File.WriteAllText(Path.Combine(outputDirectory, "index.html"), html.ToString());
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
