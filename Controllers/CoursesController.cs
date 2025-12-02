using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Learnit.Server.Data;
using Learnit.Server.Models;
using System.Security.Claims;

namespace Learnit.Server.Controllers
{
    [ApiController]
    [Route("api/courses")]
    [Authorize]
    public class CoursesController : ControllerBase
    {
        private readonly AppDbContext _db;

        public CoursesController(AppDbContext db)
        {
            _db = db;
        }

        private int GetUserId()
        {
            // JWT uses "sub" claim for user ID (from JwtRegisteredClaimNames.Sub)
            var userIdClaim = User.FindFirst("sub")?.Value 
                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
            {
                throw new UnauthorizedAccessException("Invalid user token");
            }
            
            return userId;
        }

        [HttpGet]
        public async Task<IActionResult> GetCourses(
            [FromQuery] string? search,
            [FromQuery] string? priority,
            [FromQuery] string? difficulty,
            [FromQuery] string? duration,
            [FromQuery] string? sortBy = "createdAt",
            [FromQuery] string? sortOrder = "desc")
        {
            var userId = GetUserId();
            sortBy = string.IsNullOrWhiteSpace(sortBy) ? "createdAt" : sortBy;
            sortOrder = string.IsNullOrWhiteSpace(sortOrder) ? "desc" : sortOrder;
            var query = _db.Courses
                .Include(c => c.Modules)
                .Where(c => c.UserId == userId)
                .AsQueryable();

            // Search filter
            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(c => 
                    c.Title.ToLower().Contains(search.ToLower()) ||
                    c.Description.ToLower().Contains(search.ToLower()));
            }

            // Priority filter
            if (!string.IsNullOrEmpty(priority))
            {
                var priorities = priority.Split(',');
                query = query.Where(c => priorities.Contains(c.Priority));
            }

            // Difficulty filter
            if (!string.IsNullOrEmpty(difficulty))
            {
                var difficulties = difficulty.Split(',');
                query = query.Where(c => difficulties.Contains(c.Difficulty));
            }

            // Duration filter
            if (!string.IsNullOrEmpty(duration))
            {
                if (duration == "< 1 hour")
                    query = query.Where(c => c.TotalEstimatedHours < 1);
                else if (duration == "1-3 hours")
                    query = query.Where(c => c.TotalEstimatedHours >= 1 && c.TotalEstimatedHours <= 3);
                else if (duration == "> 3 hours")
                    query = query.Where(c => c.TotalEstimatedHours > 3);
            }

            // Sorting
            query = sortBy.ToLower() switch
            {
                "title" => sortOrder == "asc" 
                    ? query.OrderBy(c => c.Title)
                    : query.OrderByDescending(c => c.Title),
                "priority" => sortOrder == "asc"
                    ? query.OrderBy(c => c.Priority)
                    : query.OrderByDescending(c => c.Priority),
                "difficulty" => sortOrder == "asc"
                    ? query.OrderBy(c => c.Difficulty)
                    : query.OrderByDescending(c => c.Difficulty),
                "hours" => sortOrder == "asc"
                    ? query.OrderBy(c => c.TotalEstimatedHours)
                    : query.OrderByDescending(c => c.TotalEstimatedHours),
                _ => sortOrder == "asc"
                    ? query.OrderBy(c => c.CreatedAt)
                    : query.OrderByDescending(c => c.CreatedAt)
            };

            var courses = await query.ToListAsync();

            var response = courses.Select(c => new CourseResponseDto
            {
                Id = c.Id,
                Title = c.Title,
                Description = c.Description,
                SubjectArea = c.SubjectArea,
                LearningObjectives = c.LearningObjectives,
                Difficulty = c.Difficulty,
                Priority = c.Priority,
                TotalEstimatedHours = c.TotalEstimatedHours,
                HoursRemaining = c.HoursRemaining,
                TargetCompletionDate = c.TargetCompletionDate,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
                Notes = c.Notes,
                IsActive = c.IsActive,
                LastStudiedAt = c.LastStudiedAt,
                Modules = c.Modules.OrderBy(m => m.Order).Select(m => new CourseModuleDto
                {
                    Id = m.Id,
                    Title = m.Title,
                    EstimatedHours = m.EstimatedHours,
                    Order = m.Order
                }).ToList(),
                ExternalLinks = c.ExternalLinks.Select(l => new ExternalLinkDto
                {
                    Id = l.Id,
                    Platform = l.Platform,
                    Title = l.Title,
                    Url = l.Url,
                    CreatedAt = l.CreatedAt
                }).ToList(),
                ActiveSession = c.StudySessions
                    .Where(s => !s.IsCompleted && s.EndTime == null)
                    .OrderByDescending(s => s.StartTime)
                    .Select(s => new StudySessionDto
                    {
                        Id = s.Id,
                        CourseModuleId = s.CourseModuleId,
                        StartTime = s.StartTime,
                        EndTime = s.EndTime,
                        DurationHours = s.DurationHours,
                        Notes = s.Notes,
                        IsCompleted = s.IsCompleted
                    })
                    .FirstOrDefault()
            }).ToList();

            return Ok(response);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetCourse(int id)
        {
            var userId = GetUserId();
            var course = await _db.Courses
                .Include(c => c.Modules)
                .Include(c => c.ExternalLinks)
                .Include(c => c.StudySessions)
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);

            if (course == null)
                return NotFound();

            var response = new CourseResponseDto
            {
                Id = course.Id,
                Title = course.Title,
                Description = course.Description,
                SubjectArea = course.SubjectArea,
                LearningObjectives = course.LearningObjectives,
                Difficulty = course.Difficulty,
                Priority = course.Priority,
                TotalEstimatedHours = course.TotalEstimatedHours,
                HoursRemaining = course.HoursRemaining,
                TargetCompletionDate = course.TargetCompletionDate,
                CreatedAt = course.CreatedAt,
                UpdatedAt = course.UpdatedAt,
                Notes = course.Notes,
                IsActive = course.IsActive,
                LastStudiedAt = course.LastStudiedAt,
                Modules = course.Modules.OrderBy(m => m.Order).Select(m => new CourseModuleDto
                {
                    Id = m.Id,
                    Title = m.Title,
                    EstimatedHours = m.EstimatedHours,
                    Order = m.Order
                }).ToList(),
                ExternalLinks = course.ExternalLinks.Select(l => new ExternalLinkDto
                {
                    Id = l.Id,
                    Platform = l.Platform,
                    Title = l.Title,
                    Url = l.Url,
                    CreatedAt = l.CreatedAt
                }).ToList(),
                ActiveSession = course.StudySessions
                    .Where(s => !s.IsCompleted && s.EndTime == null)
                    .OrderByDescending(s => s.StartTime)
                    .Select(s => new StudySessionDto
                    {
                        Id = s.Id,
                        CourseModuleId = s.CourseModuleId,
                        StartTime = s.StartTime,
                        EndTime = s.EndTime,
                        DurationHours = s.DurationHours,
                        Notes = s.Notes,
                        IsCompleted = s.IsCompleted
                    })
                    .FirstOrDefault()
            };

            return Ok(response);
        }

        private static DateTime? EnsureUtc(DateTime? value)
        {
            if (!value.HasValue)
                return null;

            return value.Value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.Value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            };
        }

        [HttpPost]
        public async Task<IActionResult> CreateCourse(CreateCourseDto dto)
        {
            var userId = GetUserId();

            var course = new Course
            {
                UserId = userId,
                Title = dto.Title,
                Description = dto.Description,
                SubjectArea = dto.SubjectArea,
                LearningObjectives = dto.LearningObjectives,
                Difficulty = dto.Difficulty,
                Priority = dto.Priority,
                TotalEstimatedHours = dto.TotalEstimatedHours,
                HoursRemaining = dto.TotalEstimatedHours,
                TargetCompletionDate = EnsureUtc(dto.TargetCompletionDate),
                Notes = dto.Notes,
                IsActive = true, // New courses are active by default
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.Courses.Add(course);
            await _db.SaveChangesAsync();

            // Add modules
            for (int i = 0; i < dto.Modules.Count; i++)
            {
                var moduleDto = dto.Modules[i];
                var module = new CourseModule
                {
                    CourseId = course.Id,
                    Title = moduleDto.Title,
                    EstimatedHours = moduleDto.EstimatedHours,
                    Order = i
                };
                _db.CourseModules.Add(module);
            }

            // Add external links
            foreach (var linkDto in dto.ExternalLinks)
            {
                var link = new ExternalLink
                {
                    CourseId = course.Id,
                    Platform = linkDto.Platform,
                    Title = linkDto.Title,
                    Url = linkDto.Url,
                    CreatedAt = DateTime.UtcNow
                };
                _db.ExternalLinks.Add(link);
            }

            await _db.SaveChangesAsync();

            // Reload with modules and external links
            course = await _db.Courses
                .Include(c => c.Modules)
                .Include(c => c.ExternalLinks)
                .FirstOrDefaultAsync(c => c.Id == course.Id);

            var response = new CourseResponseDto
            {
                Id = course!.Id,
                Title = course.Title,
                Description = course.Description,
                SubjectArea = course.SubjectArea,
                LearningObjectives = course.LearningObjectives,
                Difficulty = course.Difficulty,
                Priority = course.Priority,
                TotalEstimatedHours = course.TotalEstimatedHours,
                HoursRemaining = course.HoursRemaining,
                TargetCompletionDate = course.TargetCompletionDate,
                CreatedAt = course.CreatedAt,
                UpdatedAt = course.UpdatedAt,
                Notes = course.Notes,
                IsActive = course.IsActive,
                LastStudiedAt = course.LastStudiedAt,
                Modules = course.Modules.OrderBy(m => m.Order).Select(m => new CourseModuleDto
                {
                    Id = m.Id,
                    Title = m.Title,
                    EstimatedHours = m.EstimatedHours,
                    Order = m.Order
                }).ToList(),
                ExternalLinks = course.ExternalLinks.Select(l => new ExternalLinkDto
                {
                    Id = l.Id,
                    Platform = l.Platform,
                    Title = l.Title,
                    Url = l.Url,
                    CreatedAt = l.CreatedAt
                }).ToList()
            };

            return CreatedAtAction(nameof(GetCourse), new { id = course.Id }, response);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateCourse(int id, CreateCourseDto dto)
        {
            var userId = GetUserId();
            var course = await _db.Courses
                .Include(c => c.Modules)
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);

            if (course == null)
                return NotFound();

            course.Title = dto.Title;
            course.Description = dto.Description;
            course.SubjectArea = dto.SubjectArea;
            course.LearningObjectives = dto.LearningObjectives;
            course.Difficulty = dto.Difficulty;
            course.Priority = dto.Priority;
            course.TotalEstimatedHours = dto.TotalEstimatedHours;
            course.TargetCompletionDate = EnsureUtc(dto.TargetCompletionDate);
            course.UpdatedAt = DateTime.UtcNow;

            // Update hours remaining based on completed modules (if tracking was added)
            course.HoursRemaining = dto.TotalEstimatedHours;

            // Remove existing modules
            _db.CourseModules.RemoveRange(course.Modules);

            // Add new modules
            for (int i = 0; i < dto.Modules.Count; i++)
            {
                var moduleDto = dto.Modules[i];
                var module = new CourseModule
                {
                    CourseId = course.Id,
                    Title = moduleDto.Title,
                    EstimatedHours = moduleDto.EstimatedHours,
                    Order = i
                };
                _db.CourseModules.Add(module);
            }

            await _db.SaveChangesAsync();

            return Ok(new { message = "Course updated successfully" });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCourse(int id)
        {
            var userId = GetUserId();
            var course = await _db.Courses
                .Include(c => c.Modules)
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);

            if (course == null)
                return NotFound();

            _db.CourseModules.RemoveRange(course.Modules);
            _db.Courses.Remove(course);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Course deleted successfully" });
        }

        // Course Editing Methods
        [HttpPut("{id}/edit")]
        public async Task<IActionResult> EditCourse(int id, CreateCourseDto dto)
        {
            var userId = GetUserId();
            var course = await _db.Courses
                .Include(c => c.Modules)
                .Include(c => c.ExternalLinks)
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);

            if (course == null)
                return NotFound();

            // Update course fields
            course.Title = dto.Title;
            course.Description = dto.Description;
            course.SubjectArea = dto.SubjectArea;
            course.LearningObjectives = dto.LearningObjectives;
            course.Difficulty = dto.Difficulty;
            course.Priority = dto.Priority;
            course.TotalEstimatedHours = dto.TotalEstimatedHours;
            course.TargetCompletionDate = EnsureUtc(dto.TargetCompletionDate);
            course.Notes = dto.Notes;
            course.UpdatedAt = DateTime.UtcNow;

            // Update hours remaining based on completed sessions
            var completedHours = await _db.StudySessions
                .Where(s => s.CourseId == id && s.IsCompleted)
                .SumAsync(s => s.DurationHours);
            course.HoursRemaining = Math.Max(0, course.TotalEstimatedHours - (int)completedHours);

            // Update modules
            _db.CourseModules.RemoveRange(course.Modules);
            for (int i = 0; i < dto.Modules.Count; i++)
            {
                var moduleDto = dto.Modules[i];
                var module = new CourseModule
                {
                    CourseId = course.Id,
                    Title = moduleDto.Title,
                    EstimatedHours = moduleDto.EstimatedHours,
                    Order = i
                };
                _db.CourseModules.Add(module);
            }

            // Update external links
            _db.ExternalLinks.RemoveRange(course.ExternalLinks);
            foreach (var linkDto in dto.ExternalLinks)
            {
                var link = new ExternalLink
                {
                    CourseId = course.Id,
                    Platform = linkDto.Platform,
                    Title = linkDto.Title,
                    Url = linkDto.Url,
                    CreatedAt = DateTime.UtcNow
                };
                _db.ExternalLinks.Add(link);
            }

            await _db.SaveChangesAsync();

            return Ok(new { message = "Course updated successfully" });
        }

        // Session Management Methods
        [HttpPost("{courseId}/sessions/start")]
        public async Task<IActionResult> StartStudySession(int courseId, [FromQuery] int? moduleId)
        {
            var userId = GetUserId();

            // Check if course belongs to user
            var course = await _db.Courses.FindAsync(courseId);
            if (course == null || course.UserId != userId)
                return NotFound();

            // Check if there's already an active session
            var activeSession = await _db.StudySessions
                .FirstOrDefaultAsync(s => s.CourseId == courseId && !s.IsCompleted && s.EndTime == null);

            if (activeSession != null)
                return BadRequest(new { message = "An active study session already exists for this course" });

            var session = new StudySession
            {
                CourseId = courseId,
                CourseModuleId = moduleId,
                StartTime = DateTime.UtcNow,
                DurationHours = 0,
                Notes = "",
                IsCompleted = false,
                CreatedAt = DateTime.UtcNow
            };

            _db.StudySessions.Add(session);
            await _db.SaveChangesAsync();

            return Ok(new StudySessionDto
            {
                Id = session.Id,
                CourseModuleId = session.CourseModuleId,
                StartTime = session.StartTime,
                DurationHours = session.DurationHours,
                Notes = session.Notes,
                IsCompleted = session.IsCompleted
            });
        }

        [HttpPut("sessions/{sessionId}/stop")]
        public async Task<IActionResult> StopStudySession(int sessionId, [FromBody] string notes = "")
        {
            var userId = GetUserId();

            var session = await _db.StudySessions
                .Include(s => s.Course)
                .FirstOrDefaultAsync(s => s.Id == sessionId && s.Course!.UserId == userId);

            if (session == null)
                return NotFound();

            if (session.IsCompleted)
                return BadRequest(new { message = "Session is already completed" });

            var endTime = DateTime.UtcNow;
            var duration = (decimal)(endTime - session.StartTime).TotalHours;

            session.EndTime = endTime;
            session.DurationHours = Math.Round(duration, 2);
            session.Notes = notes;
            session.IsCompleted = true;

            // Update course's last studied time
            session.Course!.LastStudiedAt = endTime;
            session.Course.UpdatedAt = endTime;

            await _db.SaveChangesAsync();

            // Update progress tracking
            await UpdateActivityLog(userId, session.StartTime.Date, duration);

            return Ok(new StudySessionDto
            {
                Id = session.Id,
                CourseModuleId = session.CourseModuleId,
                StartTime = session.StartTime,
                EndTime = session.EndTime,
                DurationHours = session.DurationHours,
                Notes = session.Notes,
                IsCompleted = session.IsCompleted
            });
        }

        [HttpGet("{courseId}/sessions")]
        public async Task<IActionResult> GetCourseSessions(int courseId)
        {
            var userId = GetUserId();

            var course = await _db.Courses.FindAsync(courseId);
            if (course == null || course.UserId != userId)
                return NotFound();

            var sessions = await _db.StudySessions
                .Where(s => s.CourseId == courseId)
                .OrderByDescending(s => s.StartTime)
                .Select(s => new StudySessionDto
                {
                    Id = s.Id,
                    CourseModuleId = s.CourseModuleId,
                    StartTime = s.StartTime,
                    EndTime = s.EndTime,
                    DurationHours = s.DurationHours,
                    Notes = s.Notes,
                    IsCompleted = s.IsCompleted
                })
                .ToListAsync();

            return Ok(sessions);
        }

        private async Task UpdateActivityLog(int userId, DateTime date, decimal hours)
        {
            var activityLog = await _db.ActivityLogs
                .FirstOrDefaultAsync(a => a.UserId == userId && a.Date.Date == date.Date);

            if (activityLog == null)
            {
                activityLog = new ActivityLog
                {
                    UserId = userId,
                    Date = date.Date,
                    HoursCompleted = hours,
                    ActivityLevel = GetActivityLevel(hours)
                };
                _db.ActivityLogs.Add(activityLog);
            }
            else
            {
                activityLog.HoursCompleted += hours;
                activityLog.ActivityLevel = GetActivityLevel(activityLog.HoursCompleted);
            }

            await _db.SaveChangesAsync();
        }

        private int GetActivityLevel(decimal hours)
        {
            if (hours == 0) return 0;
            if (hours < 2) return 1;
            if (hours < 4) return 2;
            return 3;
        }

        [HttpGet("stats")]
        public async Task<IActionResult> GetStats()
        {
            var userId = GetUserId();
            var courses = await _db.Courses
                .Where(c => c.UserId == userId)
                .ToListAsync();

            var activeCourses = courses.Count;
            var totalHours = courses.Sum(c => c.TotalEstimatedHours);
            var weeklyFocus = totalHours / 4; // Rough estimate

            return Ok(new
            {
                activeCourses = activeCourses.ToString("D2"),
                weeklyFocus = $"{weeklyFocus} hrs",
                nextMilestone = courses.OrderBy(c => c.TargetCompletionDate ?? DateTime.MaxValue)
                    .FirstOrDefault()?.Title ?? "No upcoming milestones"
            });
        }
    }
}

