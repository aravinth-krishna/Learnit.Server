using Learnit.Server.Data;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace Learnit.Server.Services
{
    public class AiContextBuilder
    {
        private readonly AppDbContext _db;

        public AiContextBuilder(AppDbContext db)
        {
            _db = db;
        }

        public async Task<string> BuildContextAsync(int userId, CancellationToken cancellationToken = default)
        {
            var sb = new StringBuilder();

            var courses = await _db.Courses
                .Include(c => c.Modules)
                .Where(c => c.UserId == userId)
                .ToListAsync(cancellationToken);

            var courseIds = courses.Select(c => c.Id).ToList();

            var userEvents = await _db.ScheduleEvents
                .Include(e => e.CourseModule)!
                    .ThenInclude(cm => cm.Course)
                .Where(e => e.UserId == userId)
                .ToListAsync(cancellationToken);

            var userSessions = await _db.StudySessions
                .Where(s => courseIds.Contains(s.CourseId))
                .OrderByDescending(s => s.StartTime)
                .ToListAsync(cancellationToken);

            sb.AppendLine("User learning context:");
            sb.AppendLine($"Courses: {courses.Count}");

            AppendCourseSummaries(sb, courses, userSessions, userEvents);
            AppendScheduleSummaries(sb, userEvents);
            AppendProgressSummaries(sb, userSessions);
            AppendRecentSessions(sb, userSessions, courses);

            return sb.ToString();
        }

        private static void AppendCourseSummaries(StringBuilder sb, List<Models.Course> courses, List<Models.StudySession> sessions, List<Models.ScheduleEvent> events)
        {
            // Precompute scheduled module ids to highlight unscheduled work
            var scheduledModuleIds = new HashSet<int>(events.Where(e => e.CourseModuleId.HasValue).Select(e => e.CourseModuleId!.Value));

            var completedHoursByCourse = sessions
                .Where(s => s.IsCompleted)
                .GroupBy(s => s.CourseId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.DurationHours));

            foreach (var course in courses)
            {
                var totalModules = course.Modules.Count;
                var completedModules = course.Modules.Count(m => m.IsCompleted);
                var totalEstimated = course.Modules.Sum(m => m.EstimatedHours);
                var completedEstimated = course.Modules.Where(m => m.IsCompleted).Sum(m => m.EstimatedHours);
                var progressPct = totalModules > 0
                    ? Math.Round((decimal)completedModules * 100 / totalModules, 1)
                    : (decimal)0;

                var completedHours = completedHoursByCourse.TryGetValue(course.Id, out var hours) ? hours : 0;
                var hoursRemaining = totalEstimated > 0
                    ? Math.Max(0, totalEstimated - Math.Max(completedEstimated, (int)completedHours))
                    : Math.Max(0, course.TotalEstimatedHours - (int)completedHours);

                var unscheduledModules = course.Modules.Count(m => !m.IsCompleted && !scheduledModuleIds.Contains(m.Id));

                sb.AppendLine($"- {course.Title} | {completedModules}/{totalModules} modules | {progressPct}% | remaining ~{hoursRemaining}h | priority {course.Priority} | difficulty {course.Difficulty} | target {FormatDate(course.TargetCompletionDate)} | unscheduled modules {unscheduledModules}");
            }
        }

        private static void AppendScheduleSummaries(StringBuilder sb, List<Models.ScheduleEvent> events)
        {
            var today = DateTime.UtcNow.Date;
            var pastWeekStart = today.AddDays(-6);
            var nextWeekEnd = today.AddDays(7);

            var pastWeekEvents = events
                .Where(e => e.StartUtc.Date >= pastWeekStart && e.StartUtc.Date < today.AddDays(1))
                .ToList();

            var pastWeekScheduledHours = pastWeekEvents.Sum(e => (decimal)((e.EndUtc ?? e.StartUtc.AddHours(1)) - e.StartUtc).TotalHours);

            var upcomingEvents = events
                .Where(e => e.StartUtc.Date >= today && e.StartUtc.Date < nextWeekEnd)
                .ToList();

            var upcomingHours = upcomingEvents.Sum(e => (decimal)((e.EndUtc ?? e.StartUtc.AddHours(1)) - e.StartUtc).TotalHours);

            sb.AppendLine($"Past 7d schedule: {pastWeekEvents.Count} events, {pastWeekScheduledHours:0.0}h planned.");
            sb.AppendLine($"Next 7d schedule: {upcomingEvents.Count} events, {upcomingHours:0.0}h planned.");
        }

        private void AppendProgressSummaries(StringBuilder sb, List<Models.StudySession> sessions)
        {
            var today = DateTime.UtcNow.Date;
            var weekStart = today.AddDays(-6);
            var weekEnd = today.AddDays(1);

            var weeklyCompleted = sessions
                .Where(s => s.IsCompleted && s.StartTime.Date >= weekStart && s.StartTime.Date < weekEnd)
                .Sum(s => s.DurationHours);

            var currentStreak = CalculateCurrentStreak(sessions, today);
            var longestStreak = CalculateLongestStreak(sessions);

            sb.AppendLine($"Progress: completed {weeklyCompleted:0.0}h past 7d | streak {currentStreak} days | longest streak {longestStreak} days.");
        }

        private static void AppendRecentSessions(StringBuilder sb, List<Models.StudySession> sessions, List<Models.Course> courses)
        {
            var courseLookup = courses.ToDictionary(c => c.Id, c => c.Title);

            var recent = sessions
                .Where(s => s.IsCompleted)
                .OrderByDescending(s => s.StartTime)
                .Take(3)
                .ToList();

            if (!recent.Any())
            {
                sb.AppendLine("Recent sessions: none completed yet.");
                return;
            }

            sb.AppendLine("Recent sessions:");
            foreach (var session in recent)
            {
                var courseTitle = courseLookup.TryGetValue(session.CourseId, out var title) ? title : $"Course {session.CourseId}";
                sb.AppendLine($"- {courseTitle} | {session.DurationHours:0.0}h | {FormatDate(session.StartTime)}" + (session.CourseModuleId.HasValue ? $" | module {session.CourseModuleId}" : string.Empty));
            }
        }

        private static string FormatDate(DateTime? date)
        {
            return date.HasValue ? date.Value.ToString("yyyy-MM-dd") : "none";
        }

        private static int CalculateCurrentStreak(List<Models.StudySession> sessions, DateTime today)
        {
            var dates = new HashSet<DateTime>(sessions
                .Where(s => s.IsCompleted)
                .Select(s => s.StartTime.Date));

            var streak = 0;
            var cursor = today;
            while (dates.Contains(cursor))
            {
                streak++;
                cursor = cursor.AddDays(-1);
            }

            return streak;
        }

        private static int CalculateLongestStreak(List<Models.StudySession> sessions)
        {
            var ordered = sessions
                .Where(s => s.IsCompleted)
                .Select(s => s.StartTime.Date)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            if (!ordered.Any()) return 0;

            var longest = 1;
            var current = 1;

            for (int i = 1; i < ordered.Count; i++)
            {
                if ((ordered[i] - ordered[i - 1]).TotalDays == 1)
                {
                    current++;
                }
                else
                {
                    longest = Math.Max(longest, current);
                    current = 1;
                }
            }

            longest = Math.Max(longest, current);
            return longest;
        }
    }
}
