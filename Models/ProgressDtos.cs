using System;

namespace Learnit.Server.Models
{
    public class ProgressStatsDto
    {
        public int CurrentStreak { get; set; }
        public int LongestStreak { get; set; }
        public decimal TotalScheduledHours { get; set; }
        public decimal TotalCompletedHours { get; set; }
        public decimal CompletionRate { get; set; }
        public decimal Efficiency { get; set; }
        public decimal OverallProgress { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public class WeeklyDataPoint
    {
        public string Day { get; set; } = "";
        public decimal Scheduled { get; set; }
        public decimal Completed { get; set; }
    }

    public class CourseProgressDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public decimal ProgressPercentage { get; set; }
        public decimal TotalHours { get; set; }
        public decimal CompletedHours { get; set; }
    }

    public class ProgressDashboardDto
    {
        public ProgressStatsDto Stats { get; set; } = new();
        public List<WeeklyDataPoint> WeeklyData { get; set; } = new();
        public List<CourseProgressDto> CourseProgress { get; set; } = new();
        public List<int> ActivityHeatmap { get; set; } = new(); // 60 days of activity levels (0-3)
    }

    public class ActivityLogDto
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public decimal HoursCompleted { get; set; }
        public int ActivityLevel { get; set; } // 0-3 scale
    }
}
