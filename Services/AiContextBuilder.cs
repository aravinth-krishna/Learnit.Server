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

            sb.AppendLine("User learning context:");
            sb.AppendLine($"Courses: {courses.Count}");

            foreach (var course in courses)
            {
                var totalModules = course.Modules.Count;
                var completedModules = course.Modules.Count(m => m.IsCompleted);
                sb.AppendLine($"- {course.Title} | {completedModules}/{totalModules} modules | Priority: {course.Priority} | Difficulty: {course.Difficulty}");
            }

            var weekStart = DateTime.UtcNow.Date.AddDays(-6);
            var weekEnd = DateTime.UtcNow.Date.AddDays(1);

            var weeklyEvents = await _db.ScheduleEvents
                .Where(e => e.UserId == userId && e.StartUtc >= weekStart && e.StartUtc < weekEnd && e.EndUtc.HasValue)
                .ToListAsync(cancellationToken);

            var scheduledHours = weeklyEvents.Sum(e => (decimal)(e.EndUtc!.Value - e.StartUtc).TotalHours);
            var completedHours = await _db.StudySessions
                .Where(s => s.IsCompleted && s.StartTime.Date >= weekStart && s.StartTime.Date < weekEnd)
                .Join(_db.Courses.Where(c => c.UserId == userId), s => s.CourseId, c => c.Id, (s, _) => s)
                .SumAsync(s => s.DurationHours, cancellationToken);

            sb.AppendLine($"This week: scheduled {scheduledHours:0.0}h, completed {completedHours:0.0}h.");

            return sb.ToString();
        }
    }
}
