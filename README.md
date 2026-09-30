# Git Analytics Dashboard Generator

## Overview
A cross-platform .NET 8 application that analyzes Git repositories and generates HTML and Excel reports.

## Project Structure
- **/Models**: Data structures for branches, commits, developers, file statistics, and repository summaries.
- **/Services**: Core logic for Git analysis and report generation.
  - `ReportOrchestrator`: Coordinates the analysis process.
  - `BranchAnalyzer`, `CommitAnalyzer`, `DeveloperAnalyzer`, `FileAnalyzer`: Specialized analyzers.
  - `HtmlReportGenerator`: Generates a responsive HTML dashboard.
  - `ExcelReportGenerator`: Generates a detailed Excel workbook.
- **/Utilities**: Helper classes for dates and CSV exporting.

## Build Instructions
1. Ensure .NET 8 SDK is installed.
2. Navigate to the project directory: `cd GitAnalyticsDashboard`
3. Restore packages: `dotnet restore`
4. Build: `dotnet build`

## Configuration
Edit `appsettings.json` to configure:
- `OutputDirectory`: Where reports will be generated (relative to the current directory).
- `StaleBranchThresholdDays`: Days of inactivity before a branch is considered stale.
- `ReportTitle`: The title displayed on the reports.
- `ComparisonLimit`: Maximum number of commits shown in the Before vs After comparison.
- `ConsoleRowLimit`: Maximum number of file changes printed to the console (the reports always contain all of them).
- `MaxComparisonFileKB` / `MaxDiffChars`: Size caps for the before/after comparison. Larger files keep their summary but no contents; longer diffs are cut. Binary files are never loaded.
- `IncludePaths` / `ExcludePaths`: Glob filters applied to every report (see below). `ExcludePaths` ships with defaults for build output, IDE folders, `node_modules/`, lock files and minified assets.
- `Mailmap`: Extra `.mailmap`-format lines, e.g. `"Jane Doe <jane@work.com> <jane@home.com>"`, merged with the repository's own `.mailmap`.
- `TicketPattern` / `TicketUrlTemplate`: Regex for ticket IDs in commit messages (default matches `ABC-123` and `#42`) and an optional link template such as `https://jira.example.com/browse/{id}`.

## Usage
Run interactively; you will be prompted for the repository path, the starting branch and the target branch (press Enter for the current HEAD):
```bash
dotnet run --project GitAnalyticsDashboard
```
Or pass the values on the command line:
```bash
dotnet run --project GitAnalyticsDashboard -- --repo C:\path\to\repo --start release/1.0 [--target main] [--output ./Reports]
dotnet run --project GitAnalyticsDashboard -- C:\path\to\repo release/1.0 [main]
```

### Filters
```bash
# Only source files, skip designer files, one author, September only
dotnet run --project GitAnalyticsDashboard -- --repo . --start release/1.0 \
  --include "src/**" --exclude "*.Designer.cs" --author jane --since 2026-09-01 --until 2026-09-30
```
- `--include` / `--exclude`: comma-separated globs. `bin/` matches a folder at any depth, `*.min.js` (no slash) matches a name at any depth, `src/**/*.cs` (with a slash) matches from the repository root. Matching is case-insensitive. `--exclude` adds to the configured defaults; `--no-default-excludes` drops them.
- `--author`: matches part of the author name or email, after `.mailmap` is applied.
- `--since` / `--until`: author date bounds; a date-only `--until` includes the whole day.

Commits whose changes are all excluded (for example, commits that only touched `bin/`) are dropped from the scan. Author identities are merged using the repository's `.mailmap` plus the `Mailmap` setting, so one person with two emails counts once.

### What is scanned
Every commit reachable from the target that was not already history before the starting branch's tip:
the starting commit itself, everything after it, and commits merged in from branches that forked earlier.
- Branches can be local or remote-tracking (`feature/x` resolves to `origin/feature/x` when there is no local branch). Tags, commit SHAs and expressions such as `HEAD~5` also work.
- Renames are detected. Merge commits are listed but contribute no file changes of their own, because the commits they bring in are scanned individually (as in `git log`).
- If the starting branch is not an ancestor of the target, a warning is shown and the scan covers the target's commits that are not in the history before the start.

### Reports
Written to `<OutputDirectory>/<repository name>/`:
- `index.html`: dashboard with the scan range and filters, KPIs, branches, developers, scanned commits, every file change, modules, release notes, and a before/after diff view. Diffs are stored in `assets/js/diffs.js` and rendered when a file is expanded, so keep the `assets` folder next to the page.
- `report.xlsx`: Summary, File Changes, Modules, Release Notes, Branches, Developers, Commits, Files (hotspots) and Before After Comparison sheets.
- `report.json`: the full report data (without file contents or raw diffs) for Power BI, scripts or comparing runs.
- `CHANGELOG.md`: release notes grouped by Conventional Commit type (`feat`, `fix`, `perf`, `refactor`, `docs`, `test`, `build`/`ci`, `chore`, `revert`), with a Breaking Changes section (from `!` or a `BREAKING CHANGE:` footer), linked ticket IDs and a contributor list. Other messages are listed under "Other Changes".
- `file-changes.csv`, `commits.csv` and `modules.csv`: flat exports.

### Modules
File changes are rolled up to the nearest folder holding a project file (`.csproj`, `.fsproj`, `.vbproj`, `package.json`, `pom.xml`, `build.gradle`, `go.mod`, `Cargo.toml`, `pyproject.toml`, ...) in the target commit. Files outside any project are grouped by top-level folder, or `(root)`.

## Extension Points
- **Power BI**: Load `report.json` or the CSV exports.
- **REST API**: The `ReportOrchestrator` can be easily called from an ASP.NET Core Controller.
- **New Metrics**: Add properties to the Models and update the corresponding Analyzers to track additional Git metadata.
