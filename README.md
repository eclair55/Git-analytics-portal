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
- `RepositoryPaths`: List of paths to local Git repositories.
- `OutputDirectory`: Where reports will be generated.
- `StaleBranchThresholdDays`: Days of inactivity before a branch is considered stale.
- `ReportTitle`: The title displayed on the reports.

## Usage
Run the application using:
```bash
dotnet run --project GitAnalyticsDashboard
```
The reports will be generated in the configured `OutputDirectory`.

## Extension Points
- **Power BI**: Use the `CsvExporter` or add a new `JsonReportGenerator` to provide data for Power BI.
- **REST API**: The `ReportOrchestrator` can be easily called from an ASP.NET Core Controller.
- **New Metrics**: Add properties to the Models and update the corresponding Analyzers to track additional Git metadata.
