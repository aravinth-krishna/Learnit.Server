using Learnit.Server.Data;
using Learnit.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
                        CourseTitle = e.CourseModule.Course?.Title ?? string.Empty
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

            // Get available modules
            var scheduledModuleIds = await _db.ScheduleEvents
                .Where(e => e.UserId == userId && e.CourseModuleId.HasValue)
                .Select(e => e.CourseModuleId!.Value)
                .ToListAsync();

            var modulesToSchedule = await _db.CourseModules
                .Include(cm => cm.Course)
                .Where(cm => cm.Course!.UserId == userId && !scheduledModuleIds.Contains(cm.Id))
                .OrderBy(cm => cm.Course!.Priority == "High" ? 1 :
                             cm.Course!.Priority == "Medium" ? 2 : 3)
                .ThenBy(cm => cm.Order)
                .ToListAsync();

            var events = new List<ScheduleEvent>();
            var currentTime = request.StartDateTime ?? DateTime.UtcNow;

            // Parameters for more realistic scheduling
            const int workdayStartHour = 8;
            const int lunchStartHour = 12;
            const int lunchEndHour = 13;
            const int workdayEndHour = 18; // hard stop
            const int maxDailyHours = 5; // avoid cramming entire day
            const double maxBlockHours = 1.5; // 90m focus blocks
            const int bufferMinutes = 20; // short reset buffer

            // Track hours booked per day to avoid full-day stacking
            DateTime currentDay = currentTime.Date;
            double currentDayHours = 0;

            DateTime AlignToWorkWindow(DateTime dt)
            {
                var aligned = dt;

                // Skip to next weekday if weekend
                while (aligned.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                {
                    aligned = aligned.AddDays(1).Date.AddHours(workdayStartHour);
                }

                // Before workday -> jump to start
                if (aligned.Hour < workdayStartHour)
                {
                    aligned = aligned.Date.AddHours(workdayStartHour);
                }
                // Inside lunch gap -> jump to end of lunch
                else if (aligned.Hour == lunchStartHour || (aligned.Hour == lunchStartHour - 1 && aligned.Minute > 0))
                {
                    aligned = aligned.Date.AddHours(lunchEndHour);
                }
                // After workday -> move to next day start
                else if (aligned.Hour >= workdayEndHour)
                {
                    aligned = aligned.AddDays(1).Date.AddHours(workdayStartHour);
                }

                return aligned;
            }

            currentTime = AlignToWorkWindow(currentTime);
            currentDay = currentTime.Date;

            foreach (var module in modulesToSchedule)
            {
                double remainingHours = Math.Max(1, module.EstimatedHours);

                while (remainingHours > 0)
                {
                    // Reset daily counters if we moved to a new day
                    if (currentTime.Date != currentDay)
                    {
                        currentDay = currentTime.Date;
                        currentDayHours = 0;
                        currentTime = AlignToWorkWindow(currentTime);
                    }

                    // If we've hit the daily cap, move to next day start
                    if (currentDayHours >= maxDailyHours)
                    {
                        currentTime = AlignToWorkWindow(currentTime.AddDays(1));
                        continue;
                    }

                    // Ensure we are inside work hours
                    currentTime = AlignToWorkWindow(currentTime);

                    // Available time today before hard stop and daily cap
                    // Avoid scheduling across lunch
                    var dayStopHour = currentTime.Hour < lunchStartHour ? lunchStartHour : workdayEndHour;
                    var hoursUntilDayEnd = (dayStopHour - currentTime.Hour) - (currentTime.Minute > 0 ? 1 : 0);
                    var availableToday = Math.Min(hoursUntilDayEnd, maxDailyHours - currentDayHours);

                    if (availableToday <= 0)
                    {
                        currentTime = AlignToWorkWindow(currentTime.AddDays(1));
                        continue;
                    }

                    var blockHours = Math.Min(Math.Min(maxBlockHours, availableToday), remainingHours);

                    var endTime = currentTime.AddHours(blockHours);

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

                    remainingHours -= blockHours;
                    currentDayHours += blockHours;

                    // Add buffer before next block
                    currentTime = endTime.AddMinutes(bufferMinutes);
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
                events = events.Select(e => new ScheduleEventDto
                {
                    Id = e.Id,
                    Title = e.Title,
                    StartUtc = e.StartUtc,
                    EndUtc = e.EndUtc,
                    AllDay = e.AllDay,
                    CourseModuleId = e.CourseModuleId
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
    }
    
}


