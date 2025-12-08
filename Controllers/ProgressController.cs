using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Learnit.Server.Data;
using Learnit.Server.Models;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace Learnit.Server.Controllers
{
    [ApiController]
    [Route("api/progress")]
    [Authorize]
    public class ProgressController : ControllerBase
    {
        private readonly AppDbContext _db;

        public ProgressController(AppDbContext db)
        {
            _db = db;
        }

        private int GetUserId()
        {
            var userIdClaim = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
            {
                throw new UnauthorizedAccessException("Invalid user token");
            }

            return userId;
        }

        [HttpGet("dashboard")]
        public async Task<IActionResult> GetProgressDashboard()
        {
            var userId = GetUserId();

            var weekStart = DateTime.UtcNow.Date.AddDays(-6);
            var weekEnd = DateTime.UtcNow.Date.AddDays(1);

            var userCourseIds = await _db.Courses
                .Where(c => c.UserId == userId)
                .Select(c => c.Id)
                .ToListAsync();

            var weeklyEvents = await _db.ScheduleEvents
                .Where(e => e.UserId == userId &&
                           e.StartUtc >= weekStart &&
                           e.StartUtc < weekEnd &&
                           e.EndUtc.HasValue)
                .ToListAsync();

            var weeklySessions = await _db.StudySessions
                .Where(s => s.IsCompleted && userCourseIds.Contains(s.CourseId) &&
                            s.StartTime.Date >= weekStart && s.StartTime.Date < weekEnd)
                .ToListAsync();

            var weeklyData = new List<WeeklyDataPoint>();
            for (int i = 6; i >= 0; i--)
            {
                var date = DateTime.UtcNow.Date.AddDays(-i);

                var scheduled = weeklyEvents
                    .Where(e => e.StartUtc.Date == date)
                    .Sum(e => (decimal)(e.EndUtc!.Value - e.StartUtc).TotalHours);

                var completed = weeklySessions
                    .Where(s => s.StartTime.Date == date)
                    .Sum(s => s.DurationHours);

                weeklyData.Add(new WeeklyDataPoint
                {
                    Day = date.ToString("ddd"),
                    Scheduled = Math.Round(scheduled, 1),
                    Completed = Math.Round(completed, 1)
                });
            }

            var currentStreak = await CalculateCurrentStreak(userId);
            var longestStreak = await CalculateLongestStreak(userId);

            var totalScheduled = weeklyData.Sum(d => d.Scheduled);
            var totalCompleted = weeklyData.Sum(d => d.Completed);
            var completionRate = totalScheduled > 0 ? (totalCompleted / totalScheduled) * 100 : 0;
            var efficiency = Math.Min(100, completionRate);

            var courses = await _db.Courses
                .Include(c => c.Modules)
                .Where(c => c.UserId == userId)
                .ToListAsync();

            var scheduledLookup = await _db.ScheduleEvents
                .Where(e => e.UserId == userId && e.CourseModuleId.HasValue && e.EndUtc.HasValue)
                .Include(e => e.CourseModule)
                .Where(e => e.CourseModule != null)
                .GroupBy(e => e.CourseModule!.CourseId)
                .Select(g => new { CourseId = g.Key, Hours = g.Sum(e => (decimal)(e.EndUtc!.Value - e.StartUtc).TotalHours) })
                .ToDictionaryAsync(k => k.CourseId, v => v.Hours);

            var completedLookup = await _db.StudySessions
                .Where(s => s.IsCompleted && userCourseIds.Contains(s.CourseId))
                .GroupBy(s => s.CourseId)
                .Select(g => new { CourseId = g.Key, Hours = g.Sum(s => s.DurationHours) })
                .ToDictionaryAsync(k => k.CourseId, v => v.Hours);

            var courseProgress = courses.Select(c =>
            {
                var totalModules = c.Modules.Count;
                var completedModules = c.Modules.Count(m => m.IsCompleted);
                var completedEstimated = c.Modules.Where(m => m.IsCompleted).Sum(m => m.EstimatedHours);
                var totalHours = c.Modules.Sum(m => m.EstimatedHours);
                var progressPct = totalModules > 0
                    ? (decimal)Math.Round((double)completedModules * 100 / totalModules, 1)
                    : 0;

                var scheduledHours = scheduledLookup.TryGetValue(c.Id, out var sh) ? sh : 0;
                var completedHours = completedLookup.TryGetValue(c.Id, out var ch) ? ch : 0;

                return new CourseProgressDto
                {
                    Id = c.Id,
                    Title = c.Title,
                    TotalHours = totalHours,
                    CompletedHours = completedHours > 0 ? completedHours : completedEstimated,
                    ProgressPercentage = progressPct
                };
            }).ToList();

            var overallProgress = courseProgress.Any()
                ? courseProgress.Average(c => c.ProgressPercentage)
                : 0;

            var heatmapData = await GenerateHeatmapData(userId);

            var dashboard = new ProgressDashboardDto
            {
                Stats = new ProgressStatsDto
                {
                    CurrentStreak = currentStreak,
                    LongestStreak = longestStreak,
                    TotalScheduledHours = Math.Round(totalScheduled, 1),
                    TotalCompletedHours = Math.Round(totalCompleted, 1),
                    CompletionRate = Math.Round((decimal)completionRate, 1),
                    Efficiency = Math.Round((decimal)efficiency, 1),
                    OverallProgress = Math.Round((decimal)overallProgress, 1),
                    LastUpdated = DateTime.UtcNow
                },
                WeeklyData = weeklyData,
                CourseProgress = courseProgress,
                ActivityHeatmap = heatmapData
            };

            return Ok(dashboard);
        }

        private async Task<int> CalculateCurrentStreak(int userId)
        {
            var today = DateTime.UtcNow.Date;
            var streak = 0;

            // Check last 30 days for activity
            for (int i = 0; i < 30; i++)
            {
                var date = today.AddDays(-i);
                var hasActivity = await _db.StudySessions
                    .Join(_db.Courses.Where(c => c.UserId == userId), s => s.CourseId, c => c.Id,
                        (s, _) => s)
                    .AnyAsync(s => s.IsCompleted && s.StartTime.Date == date);

                if (hasActivity)
                {
                    streak++;
                }
                else if (i == 0)
                {
                    // No activity today, continue checking
                    continue;
                }
                else
                {
                    // Gap in streak
                    break;
                }
            }

            return streak;
        }

        private async Task<int> CalculateLongestStreak(int userId)
        {
            // Simplified: just return current streak for now
            // In a real app, you'd track historical streaks
            return await CalculateCurrentStreak(userId);
        }

        private async Task<List<int>> GenerateHeatmapData(int userId)
        {
            var heatmap = new List<int>();
            var today = DateTime.UtcNow.Date;

            for (int i = 59; i >= 0; i--)
            {
                var date = today.AddDays(-i);

                // Calculate activity level based on completed study session hours
                var dailyHours = await _db.StudySessions
                    .Join(_db.Courses.Where(c => c.UserId == userId), s => s.CourseId, c => c.Id,
                        (s, _) => s)
                    .Where(s => s.IsCompleted && s.StartTime.Date == date)
                    .SumAsync(s => s.DurationHours);

                int activityLevel;
                if (dailyHours == 0) activityLevel = 0;
                else if (dailyHours < 2) activityLevel = 1;
                else if (dailyHours < 4) activityLevel = 2;
                else activityLevel = 3;

                heatmap.Add(activityLevel);
            }

            return heatmap;
        }
    }
}
