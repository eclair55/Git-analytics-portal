using System.IO;
using LibGit2Sharp;

namespace GitAnalyticsDashboard.Utilities
{
    public static class BlobExtensions
    {
        public static string GetContentText(this Blob blob)
        {
            if (blob == null) return string.Empty;
            using (var reader = new StreamReader(blob.GetContentStream()))
            {
                return reader.ReadToEnd();
            }
        }
    }
}
