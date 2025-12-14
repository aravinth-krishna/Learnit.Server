using Learnit.Server.Data;
using Learnit.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Security.Claims;

namespace Learnit.Server.Controllers
{
    [ApiController]
    [Route("api/schedule")]
    [Authorize]
    public class ScheduleController : ControllerBase
    {
        private readonly AppDbContext _db;

        public ScheduleController(AppDbContext db)
        {
            _db = db;
        }

        private int GetUserId()
        {
            var userIdClaim = User.FindFirst("sub")?.Value
                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
            {
                throw new UnauthorizedAccessException("Invalid user token");
            }

            return userId;
        }

        private static DateTime EnsureUtc(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };
        }

        private static DateTime? EnsureUtc(DateTime? value)
        {
            if (!value.HasValue) return null;
            return EnsureUtc(value.Value);
        }

        [HttpGet]
        public async Task<IActionResult> GetEvents(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to)
        {
            var userId = GetUserId();

            var query = _db.ScheduleEvents
                .Where(e => e.UserId == userId)
                .Include(e => e.CourseModule)
                    .ThenInclude(cm => cm!.Course)
                .AsQueryable();

            if (from.HasValue)
            {
                var fromUtc = EnsureUtc(from.Value);
                query = query.Where(e => e.EndUtc == null ? e.StartUtc >= fromUtc : e.EndUtc >= fromUtc);
            }

            if (to.HasValue)
            {
                var toUtc = EnsureUtc(to.Value);
                query = query.Where(e => e.StartUtc <= toUtc);
            }

            var events = await query
                .OrderBy(e => e.StartUtc)
                .ToListAsync();

            var result = events.Select(e => new ScheduleEventDto
            {
                Id = e.Id,
                Title = e.Title,
                StartUtc = e.StartUtc,
                EndUtc = e.EndUtc,
                AllDay = e.AllDay,
                CourseModuleId = e.CourseModuleId,
                CourseModule = e.CourseModule == null
                    ? null
                    : new CourseModuleInfo
                    {
                        Id = e.CourseModule.Id,
                        Title = e.CourseModule.Title,
                        CourseId = e.CourseModule.CourseId,
                        CourseTitle = e.CourseModule.Course?.Title ?? string.Empty,
                        IsCompleted = e.CourseModule.IsCompleted
                    }
            }).ToList();

            return Ok(result);
        }

        [HttpPost]
        public async Task<IActionResult> CreateEvent(CreateScheduleEventDto dto)
        {
            var userId = GetUserId();

            var entity = new ScheduleEvent
            {
                UserId = userId,
                Title = dto.Title,
                StartUtc = dto.StartUtc,
                EndUtc = dto.EndUtc,
                AllDay = dto.AllDay,
                CourseModuleId = dto.CourseModuleId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.ScheduleEvents.Add(entity);
            await _db.SaveChangesAsync();

            var result = new ScheduleEventDto
            {
                Id = entity.Id,
                Title = entity.Title,
                StartUtc = entity.StartUtc,
                EndUtc = entity.EndUtc,
                AllDay = entity.AllDay,
                CourseModuleId = entity.CourseModuleId
            };

            return CreatedAtAction(nameof(GetEvents), new { id = entity.Id }, result);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateEvent(int id, CreateScheduleEventDto dto)
        {
            var userId = GetUserId();

            var entity = await _db.ScheduleEvents
                .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);

            if (entity == null)
                return NotFound();

            entity.Title = dto.Title;
            entity.StartUtc = dto.StartUtc;
            entity.EndUtc = dto.EndUtc;
            entity.AllDay = dto.AllDay;
            entity.CourseModuleId = dto.CourseModuleId;
            entity.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(new { message = "Event updated" });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteEvent(int id)
        {
            var userId = GetUserId();

            var entity = await _db.ScheduleEvents
                .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);

            if (entity == null)
                return NotFound();

            _db.ScheduleEvents.Remove(entity);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Event deleted" });
        }

        [HttpDelete("reset")]
        public async Task<IActionResult> ResetAll()
        {
            var userId = GetUserId();

            var events = await _db.ScheduleEvents
                .Where(e => e.UserId == userId)
                .ToListAsync();

            if (events.Count == 0)
                return Ok(new { message = "No events to remove", removed = 0 });

            _db.ScheduleEvents.RemoveRange(events);
            await _db.SaveChangesAsync();

            return Ok(new { message = "All schedule events cleared", removed = events.Count });
        }

        [HttpGet("available-modules")]
        public async Task<IActionResult> GetAvailableModules()
        {
            var userId = GetUserId();

            // Get course modules that haven't been scheduled yet
            var scheduledModuleIds = await _db.ScheduleEvents
                .Where(e => e.UserId == userId && e.CourseModuleId.HasValue)
                .Select(e => e.CourseModuleId!.Value)
                .ToListAsync();

            var availableModules = await _db.CourseModules
                .Include(cm => cm.Course)
                .Where(cm => cm.Course!.UserId == userId && !scheduledModuleIds.Contains(cm.Id))
                .Select(cm => new
                {
                    id = cm.Id,
                    title = cm.Title,
                    courseTitle = cm.Course!.Title,
                    estimatedHours = cm.EstimatedHours,
                    courseId = cm.CourseId
                })
                .ToListAsync();

            return Ok(availableModules);
        }

        [HttpPost("auto-schedule")]
        public async Task<IActionResult> AutoScheduleModules([FromBody] AutoScheduleRequest request)
        {
            var userId = GetUserId();
            var includeWeekends = request.IncludeWeekends ?? false;
            var preferredStartHour = Math.Clamp(request.PreferredStartHour ?? 9, 5, 12);
            var preferredEndHour = Math.Clamp(request.PreferredEndHour ?? 18, preferredStartHour + 2, 22);

            var maxSessionMinutes = Math.Clamp(request.MaxSessionMinutes ?? 90, 30, 180);
            var maxBlockHours = maxSessionMinutes / 60d;
            var bufferMinutes = Math.Clamp(request.BufferMinutes ?? 15, 5, 45);

            var windowHours = Math.Max(2, preferredEndHour - preferredStartHour);
            var maxDailyHours = Math.Clamp(request.MaxDailyHours ?? Math.Min(windowHours, 6), 2, windowHours);
            var weeklyLimitHours = request.WeeklyLimitHours ?? 20;

            // Get available modules ordered by urgency and priority
            var scheduledModuleIds = await _db.ScheduleEvents
                .Where(e => e.UserId == userId && e.CourseModuleId.HasValue)
                .Select(e => e.CourseModuleId!.Value)
                .ToListAsync();

            var modulesToSchedule = await _db.CourseModules
                .Include(cm => cm.Course)
                .Where(cm => cm.Course!.UserId == userId && !scheduledModuleIds.Contains(cm.Id) && cm.Course!.IsActive && !cm.IsCompleted)
                .OrderBy(cm => cm.Course!.TargetCompletionDate ?? DateTime.MaxValue)
                .ThenBy(cm => cm.Course!.Priority == "High" ? 1 : cm.Course!.Priority == "Medium" ? 2 : 3)
                .ThenBy(cm => cm.Order)
                .ToListAsync();

            var occupiedIntervals = (await _db.ScheduleEvents
                .Where(e => e.UserId == userId)
                .Select(e => new
                {
                    Start = e.StartUtc,
                    End = e.EndUtc ?? e.StartUtc.AddHours(1)
                })
                .ToListAsync())
                .Select(e => (Start: EnsureUtc(e.Start), End: EnsureUtc(e.End)))
                .OrderBy(e => e.Start)
                .ToList();

            var events = new List<ScheduleEvent>();
            var currentTime = EnsureUtc(request.StartDateTime ?? DateTime.UtcNow);

            const int lunchStartHour = 12;
            const int lunchEndHour = 13;

            DateTime GetWeekStart(DateTime dt)
            {
                var normalized = dt.Date;
                var offset = normalized.DayOfWeek == DayOfWeek.Sunday ? 6 : ((int)normalized.DayOfWeek - 1);
                return normalized.AddDays(-offset);
            }

            DateTime AlignToWorkWindow(DateTime dt)
            {
                var aligned = EnsureUtc(dt);

                while (!includeWeekends && (aligned.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday))
                {
                    aligned = aligned.AddDays(1).Date.AddHours(preferredStartHour);
                }

                if (aligned.Hour < preferredStartHour)
                {
                    aligned = aligned.Date.AddHours(preferredStartHour);
                }
                else if (aligned.Hour >= preferredEndHour)
                {
                    aligned = aligned.AddDays(1).Date.AddHours(preferredStartHour);
                }

                // Avoid lunch window when it sits inside the work window
                if (preferredStartHour < lunchStartHour && preferredEndHour > lunchEndHour)
                {
                    if (aligned.Hour == lunchStartHour || (aligned.Hour == lunchStartHour - 1 && aligned.Minute > 0))
                    {
                        aligned = aligned.Date.AddHours(lunchEndHour);
                    }
                    else if (aligned.Hour >= lunchStartHour && aligned.Hour < lunchEndHour)
                    {
                        aligned = aligned.Date.AddHours(lunchEndHour);
                    }
                }

                return aligned;
            }

            currentTime = AlignToWorkWindow(currentTime);
            var currentDay = currentTime.Date;
            double currentDayHours = 0;
            var weeklyHours = new Dictionary<DateTime, double>();

            bool HasOverlap(DateTime start, DateTime end, out DateTime nextStart)
            {
                foreach (var interval in occupiedIntervals)
                {
                    if (start < interval.End && end > interval.Start)
                    {
                        nextStart = EnsureUtc(interval.End.AddMinutes(bufferMinutes));
                        return true;
                    }
                }

                nextStart = DateTime.MinValue;
                return false;
            }

            foreach (var module in modulesToSchedule)
            {
                double remainingHours = Math.Max(1, module.EstimatedHours);

                // Slightly smaller blocks for advanced material
                var difficultyCap = module.Course?.Difficulty == "Advanced" ? Math.Min(maxBlockHours, 1.0) : maxBlockHours;

                while (remainingHours > 0)
                {
                    if (currentTime.Date != currentDay)
                    {
                        currentDay = currentTime.Date;
                        currentDayHours = 0;
                        currentTime = AlignToWorkWindow(currentTime);
                    }

                    var weekKey = GetWeekStart(currentTime);
                    weeklyHours.TryGetValue(weekKey, out var usedThisWeek);

                    if (weeklyLimitHours > 0 && usedThisWeek >= weeklyLimitHours)
                    {
                        currentTime = AlignToWorkWindow(weekKey.AddDays(7).AddHours(preferredStartHour));
                        continue;
                    }

                    if (currentDayHours >= maxDailyHours)
                    {
                        currentTime = AlignToWorkWindow(currentTime.AddDays(1));
                        continue;
                    }

                    currentTime = AlignToWorkWindow(currentTime);

                    DateTime dayBoundary;
                    if (preferredStartHour < lunchStartHour && preferredEndHour > lunchEndHour && currentTime.Hour < lunchStartHour)
                    {
                        dayBoundary = currentTime.Date.AddHours(lunchStartHour);
                    }
                    else
                    {
                        dayBoundary = currentTime.Date.AddHours(preferredEndHour);
                    }

                    var availableHoursToday = Math.Max(0, (dayBoundary - currentTime).TotalHours);
                    var remainingDailyHours = maxDailyHours - currentDayHours;
                    var remainingWeeklyHours = weeklyLimitHours > 0 ? weeklyLimitHours - usedThisWeek : double.MaxValue;

                    var blockHours = new[]
                    {
                        difficultyCap,
                        remainingHours,
                        availableHoursToday,
                        remainingDailyHours,
                        remainingWeeklyHours
                    }.Min();

                    if (blockHours <= 0)
                    {
                        currentTime = AlignToWorkWindow(currentTime.AddDays(1));
                        continue;
                    }

                    var endTime = currentTime.AddHours(blockHours);

                    if (HasOverlap(currentTime, endTime, out var nextStart))
                    {
                        currentTime = AlignToWorkWindow(nextStart);
                        continue;
                    }

                    // Do not cross the lunch gap
                    if (preferredStartHour < lunchStartHour && preferredEndHour > lunchEndHour && currentTime < currentTime.Date.AddHours(lunchStartHour) && endTime > currentTime.Date.AddHours(lunchStartHour))
                    {
                        endTime = currentTime.Date.AddHours(lunchStartHour);
                        blockHours = (endTime - currentTime).TotalHours;
                    }

                    var scheduleEvent = new ScheduleEvent
                    {
                        UserId = userId,
                        Title = $"{module.Course!.Title} - {module.Title}",
                        StartUtc = currentTime,
                        EndUtc = endTime,
                        AllDay = false,
                        CourseModuleId = module.Id,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    events.Add(scheduleEvent);
                    occupiedIntervals.Add((scheduleEvent.StartUtc, scheduleEvent.EndUtc ?? scheduleEvent.StartUtc.AddMinutes(maxSessionMinutes)));

                    remainingHours -= blockHours;
                    currentDayHours += blockHours;
                    weeklyHours[weekKey] = usedThisWeek + blockHours;

                    currentTime = AlignToWorkWindow(endTime.AddMinutes(bufferMinutes));
                }
            }

            if (events.Any())
            {
                _db.ScheduleEvents.AddRange(events);
                await _db.SaveChangesAsync();
            }

            return Ok(new
            {
                scheduledEvents = events.Count,
                weeklyLimitHours,
                maxDailyHours,
                maxSessionMinutes,
                events = events.Select(e => new ScheduleEventDto
                {
                    Id = e.Id,
                    Title = e.Title,
                    StartUtc = e.StartUtc,
                    EndUtc = e.EndUtc,
                    AllDay = e.AllDay,
                    CourseModuleId = e.CourseModuleId,
                    CourseModule = e.CourseModuleId.HasValue
                        ? new CourseModuleInfo
                        {
                            Id = e.CourseModuleId.Value,
                            Title = e.Title,
                            CourseId = modulesToSchedule.First(m => m.Id == e.CourseModuleId.Value).CourseId,
                            CourseTitle = modulesToSchedule.First(m => m.Id == e.CourseModuleId.Value).Course!.Title,
                            IsCompleted = modulesToSchedule.First(m => m.Id == e.CourseModuleId.Value).IsCompleted
                        }
                        : null
                })
            });
        }

        [HttpPost("{eventId}/link-module/{moduleId}")]
        public async Task<IActionResult> LinkEventToModule(int eventId, int moduleId)
        {
            var userId = GetUserId();

            var scheduleEvent = await _db.ScheduleEvents
                .FirstOrDefaultAsync(e => e.Id == eventId && e.UserId == userId);

            if (scheduleEvent == null)
                return NotFound("Event not found");

            // Verify the module belongs to user's course
            var courseModule = await _db.CourseModules
                .Include(cm => cm.Course)
                .FirstOrDefaultAsync(cm => cm.Id == moduleId && cm.Course!.UserId == userId);

            if (courseModule == null)
                return BadRequest("Invalid module");

            scheduleEvent.CourseModuleId = moduleId;
            scheduleEvent.Title = $"{courseModule.Course!.Title} - {courseModule.Title}";
            scheduleEvent.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(new { message = "Event linked to module" });
        }

        [HttpDelete("{eventId}/unlink-module")]
        public async Task<IActionResult> UnlinkEventFromModule(int eventId)
        {
            var userId = GetUserId();

            var scheduleEvent = await _db.ScheduleEvents
                .FirstOrDefaultAsync(e => e.Id == eventId && e.UserId == userId);

            if (scheduleEvent == null)
                return NotFound("Event not found");

            scheduleEvent.CourseModuleId = null;
            // Reset title to generic
            scheduleEvent.Title = "Study Session";
            scheduleEvent.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(new { message = "Event unlinked from module" });
        }
    }

    public class AutoScheduleRequest
    {
        public DateTime? StartDateTime { get; set; }
        public int? PreferredStartHour { get; set; }
        public int? PreferredEndHour { get; set; }
        public bool? IncludeWeekends { get; set; }
        public int? MaxDailyHours { get; set; }
        public int? MaxSessionMinutes { get; set; }
        public int? BufferMinutes { get; set; }
        public int? WeeklyLimitHours { get; set; }
    }
    
}


