using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GitAnalyticsDashboard.Utilities
{
    public static class CsvExporter
    {
        public static void ExportToCsv<T>(IEnumerable<T> data, string filePath)
        {
            var properties = typeof(T).GetProperties();
            var sb = new StringBuilder();

            // Header
            var headers = new List<string>();
            foreach (var prop in properties)
            {
                headers.Add(prop.Name);
            }
            sb.AppendLine(string.Join(",", headers));

            // Data
            foreach (var item in data)
            {
                var values = new List<string>();
                foreach (var prop in properties)
                {
                    var val = prop.GetValue(item)?.ToString() ?? "";
                    if (val.Contains(",") || val.Contains("\"") || val.Contains("\n") || val.Contains("\r"))
                    {
                        val = $"\"{val.Replace("\"", "\"\"")}\"";
                    }
                    values.Add(val);
                }
                sb.AppendLine(string.Join(",", values));
            }

            File.WriteAllText(filePath, sb.ToString());
        }
    }
}
