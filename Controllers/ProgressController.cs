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

            // Get current week data (last 7 days)
            var weekStart = DateTime.UtcNow.Date.AddDays(-6); // 7 days ago
            var weekEnd = DateTime.UtcNow.Date.AddDays(1); // Tomorrow

            // Get scheduled vs completed for the week
            var weeklyEvents = await _db.ScheduleEvents
                .Where(e => e.UserId == userId &&
                           e.StartUtc >= weekStart &&
                           e.StartUtc < weekEnd)
                .ToListAsync();

            // Calculate weekly data by day
            var weeklyData = new List<WeeklyDataPoint>();
            for (int i = 6; i >= 0; i--)
            {
                var date = DateTime.UtcNow.Date.AddDays(-i);
                var dayEvents = weeklyEvents.Where(e => e.StartUtc.Date == date).ToList();

                var scheduled = dayEvents.Sum(e => e.EndUtc.HasValue
                    ? (decimal)(e.EndUtc.Value - e.StartUtc).TotalHours
                    : 0);

                // For now, assume completed = scheduled (in a real app, you'd track completion)
                var completed = scheduled;

                weeklyData.Add(new WeeklyDataPoint
                {
                    Day = date.ToString("ddd"),
                    Scheduled = (decimal)Math.Round((double)scheduled, 1),
                    Completed = (decimal)Math.Round((double)completed, 1)
                });
            }

            // Calculate streaks (simplified - based on having any activity each day)
            var currentStreak = await CalculateCurrentStreak(userId);
            var longestStreak = await CalculateLongestStreak(userId);

            // Calculate totals
            var totalScheduled = weeklyData.Sum(d => d.Scheduled);
            var totalCompleted = weeklyData.Sum(d => d.Completed);
            var completionRate = totalScheduled > 0 ? (totalCompleted / totalScheduled) * 100 : 0;
            var efficiency = Math.Min(100, completionRate * 1.1m); // Simple efficiency calculation

            // Get course progress
            var courses = await _db.Courses
                .Include(c => c.Modules)
                .Where(c => c.UserId == userId)
                .ToListAsync();

            var courseProgress = courses.Select(c => new CourseProgressDto
            {
                Id = c.Id,
                Title = c.Title,
                TotalHours = c.TotalEstimatedHours,
                CompletedHours = c.TotalEstimatedHours - c.HoursRemaining, // Simplified
                ProgressPercentage = c.TotalEstimatedHours > 0
                    ? (decimal)Math.Round((double)(((c.TotalEstimatedHours - c.HoursRemaining) / c.TotalEstimatedHours) * 100), 1)
                    : 0
            }).ToList();

            // Calculate overall progress
            var overallProgress = courseProgress.Any()
                ? courseProgress.Average(c => c.ProgressPercentage)
                : 0;

            // Generate heatmap data (last 60 days)
            var heatmapData = await GenerateHeatmapData(userId);

            var dashboard = new ProgressDashboardDto
            {
                Stats = new ProgressStatsDto
                {
                    CurrentStreak = currentStreak,
                    LongestStreak = longestStreak,
                    TotalScheduledHours = (decimal)Math.Round((double)totalScheduled, 1),
                    TotalCompletedHours = (decimal)Math.Round((double)totalCompleted, 1),
                    CompletionRate = (decimal)Math.Round((double)completionRate, 1),
                    Efficiency = (decimal)Math.Round((double)efficiency, 1),
                    OverallProgress = (decimal)Math.Round((double)overallProgress, 1),
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
                var hasActivity = await _db.ScheduleEvents
                    .AnyAsync(e => e.UserId == userId &&
                                  e.StartUtc.Date == date);

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

                // Calculate activity level based on hours scheduled
                var dailyHours = await _db.ScheduleEvents
                    .Where(e => e.UserId == userId && e.StartUtc.Date == date)
                    .SumAsync(e => e.EndUtc.HasValue
                        ? (decimal)(e.EndUtc.Value - e.StartUtc).TotalHours
                        : 0);

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
