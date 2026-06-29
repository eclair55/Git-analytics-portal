using System;

namespace GitAnalyticsDashboard.Utilities
{
    public static class DateUtilities
    {
        public static bool IsStale(DateTimeOffset lastActivity, int thresholdDays)
        {
            return (DateTimeOffset.Now - lastActivity).TotalDays >= thresholdDays;
        }

        public static int GetCommitsInPeriod(DateTimeOffset commitDate, DateTimeOffset now, int days)
        {
            return (now - commitDate).TotalDays <= days ? 1 : 0;
        }
    }
}
