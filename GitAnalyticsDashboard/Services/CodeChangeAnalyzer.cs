using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GitAnalyticsDashboard.Services
{
    public class CodeChangeAnalyzer
    {
        public List<string> Analyze(string path, string before, string after)
        {
            var changes = new List<string>();
            var extension = System.IO.Path.GetExtension(path).ToLower();

            if (extension == ".cs")
            {
                AnalyzeCSharp(before, after, changes);
            }
            else if (extension == ".sql")
            {
                AnalyzeSql(before, after, changes);
            }
            else if (extension == ".json" || extension == ".config" || extension == ".xml")
            {
                changes.Add("Configuration changes");
            }
            else if (extension == ".html" || extension == ".cshtml" || extension == ".css")
            {
                changes.Add("HTML/UI changes");
            }

            if (after.Contains("INSERT INTO") || after.Contains("UPDATE ") || after.Contains("DELETE FROM") || after.Contains("SELECT "))
            {
                if (!changes.Contains("SQL changes"))
                    changes.Add("SQL changes");
            }

            return changes.Distinct().ToList();
        }

        private void AnalyzeCSharp(string before, string after, List<string> changes)
        {
            var beforeMethods = ExtractMethods(before);
            var afterMethods = ExtractMethods(after);

            var addedMethods = afterMethods.Except(beforeMethods).ToList();
            var removedMethods = beforeMethods.Except(afterMethods).ToList();

            foreach (var m in addedMethods) changes.Add($"Added method: {m}");
            foreach (var m in removedMethods) changes.Add($"Removed method: {m}");

            if (after.Contains("[HttpGet") || after.Contains("[HttpPost") || after.Contains("[HttpPut") || after.Contains("[HttpDelete]"))
            {
                changes.Add("API endpoint changes");
            }

            if (Regex.IsMatch(after, @"\bclass\s+\w+"))
            {
                var beforeClasses = ExtractClasses(before);
                var afterClasses = ExtractClasses(after);
                foreach (var c in afterClasses.Except(beforeClasses)) changes.Add($"New class: {c}");
                foreach (var c in beforeClasses.Except(afterClasses)) changes.Add($"Removed class: {c}");
            }

            if (after.Contains("interface "))
            {
                 changes.Add("Changed interfaces");
            }
        }

        private void AnalyzeSql(string before, string after, List<string> changes)
        {
            changes.Add("SQL changes");
            if (after.Contains("CREATE TABLE")) changes.Add("Schema change: New table");
            if (after.Contains("ALTER TABLE")) changes.Add("Schema change: Modified table");
        }

        private List<string> ExtractMethods(string content)
        {
            // Simple regex for C# methods
            var matches = Regex.Matches(content, @"(public|private|protected|internal|static|\s)+\s+\w+\s+(\w+)\s*\(.*\)\s*{?");
            return matches.Select(m => m.Groups[2].Value).Where(v => !string.IsNullOrEmpty(v) && v != "if" && v != "for" && v != "foreach" && v != "while" && v != "switch").ToList();
        }

        private List<string> ExtractClasses(string content)
        {
            var matches = Regex.Matches(content, @"class\s+(\w+)");
            return matches.Select(m => m.Groups[1].Value).ToList();
        }
    }
}
