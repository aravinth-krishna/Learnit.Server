using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Learnit.Server.Data;
using Learnit.Server.Models;
using System.Security.Claims;
using System.Collections.Generic;

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
                    Description = m.Description,
                    EstimatedHours = m.EstimatedHours,
                    Order = m.Order,
                    ParentModuleId = m.ParentModuleId,
                    Notes = m.Notes,
                    IsCompleted = m.IsCompleted
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
                    Description = m.Description,
                    EstimatedHours = m.EstimatedHours,
                    Order = m.Order,
                    ParentModuleId = m.ParentModuleId,
                    Notes = m.Notes,
                    IsCompleted = m.IsCompleted
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
                    Description = moduleDto.Description,
                    EstimatedHours = moduleDto.EstimatedHours,
                    Order = i,
                    ParentModuleId = moduleDto.ParentModuleId,
                    Notes = moduleDto.Notes,
                    IsCompleted = moduleDto.IsCompleted
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
                    Order = m.Order,
                    ParentModuleId = m.ParentModuleId,
                    Notes = m.Notes,
                    IsCompleted = m.IsCompleted
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
        public async Task<IActionResult> UpdateCourse(int id, [FromBody] Dictionary<string, object> updates)
        {
            var userId = GetUserId();
            var course = await _db.Courses
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);

            if (course == null)
                return NotFound();

            // Update only provided fields
            if (updates.ContainsKey("title") && updates["title"] != null)
                course.Title = updates["title"].ToString() ?? "";
            if (updates.ContainsKey("description") && updates["description"] != null)
                course.Description = updates["description"].ToString() ?? "";
            if (updates.ContainsKey("subjectArea") && updates["subjectArea"] != null)
                course.SubjectArea = updates["subjectArea"].ToString() ?? "";
            if (updates.ContainsKey("learningObjectives") && updates["learningObjectives"] != null)
                course.LearningObjectives = updates["learningObjectives"].ToString() ?? "";
            if (updates.ContainsKey("difficulty") && updates["difficulty"] != null)
                course.Difficulty = updates["difficulty"].ToString() ?? "";
            if (updates.ContainsKey("priority") && updates["priority"] != null)
                course.Priority = updates["priority"].ToString() ?? "";
            if (updates.ContainsKey("totalEstimatedHours") && updates["totalEstimatedHours"] != null)
            {
                if (int.TryParse(updates["totalEstimatedHours"].ToString(), out int hours))
                {
                    course.TotalEstimatedHours = hours;
                    // Recalculate hours remaining
                    var completedHours = await _db.StudySessions
                        .Where(s => s.CourseId == id && s.IsCompleted)
                        .SumAsync(s => s.DurationHours);
                    course.HoursRemaining = Math.Max(0, course.TotalEstimatedHours - (int)completedHours);
                }
            }
            if (updates.ContainsKey("targetCompletionDate") && updates["targetCompletionDate"] != null)
            {
                if (DateTime.TryParse(updates["targetCompletionDate"].ToString(), out DateTime date))
                    course.TargetCompletionDate = EnsureUtc(date);
            }
            if (updates.ContainsKey("notes") && updates["notes"] != null)
                course.Notes = updates["notes"].ToString() ?? "";

            course.UpdatedAt = DateTime.UtcNow;
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
        [HttpPatch("modules/{moduleId}/toggle-completion")]
        public async Task<IActionResult> ToggleModuleCompletion(int moduleId)
        {
            var userId = GetUserId();
            var module = await _db.CourseModules
                .Include(m => m.Course)
                .FirstOrDefaultAsync(m => m.Id == moduleId && m.Course.UserId == userId);

            if (module == null)
                return NotFound();

            module.IsCompleted = !module.IsCompleted;

            // Update course progress
            var course = module.Course;
            var completedHours = await _db.StudySessions
                .Where(s => s.CourseId == course.Id && s.IsCompleted)
                .SumAsync(s => s.DurationHours);
            course.HoursRemaining = Math.Max(0, course.TotalEstimatedHours - (int)completedHours);

            await _db.SaveChangesAsync();

            return Ok(new { module.IsCompleted, course.HoursRemaining });
        }

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
                    Description = moduleDto.Description,
                    EstimatedHours = moduleDto.EstimatedHours,
                    Order = i,
                    ParentModuleId = moduleDto.ParentModuleId,
                    Notes = moduleDto.Notes,
                    IsCompleted = moduleDto.IsCompleted
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

        // Module Management
        [HttpPost("{courseId}/modules")]
        public async Task<IActionResult> CreateModule(int courseId, [FromBody] CreateModuleDto dto)
        {
            var userId = GetUserId();
            var course = await _db.Courses
                .Include(c => c.Modules)
                .FirstOrDefaultAsync(c => c.Id == courseId && c.UserId == userId);

            if (course == null)
                return NotFound();

            if (string.IsNullOrWhiteSpace(dto.Title))
                return BadRequest(new { message = "Title is required" });

            if (dto.ParentModuleId.HasValue)
            {
                var parentExists = course.Modules.Any(m => m.Id == dto.ParentModuleId.Value);
                if (!parentExists)
                    return BadRequest(new { message = "Parent module not found" });
            }

            var nextOrder = course.Modules.Any() ? course.Modules.Max(m => m.Order) + 1 : 0;

            var module = new CourseModule
            {
                CourseId = courseId,
                Title = dto.Title,
                Description = dto.Description ?? "",
                EstimatedHours = dto.EstimatedHours ?? 0,
                Order = nextOrder,
                ParentModuleId = dto.ParentModuleId,
                Notes = dto.Notes ?? "",
                IsCompleted = false
            };

            _db.CourseModules.Add(module);
            course.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new CourseModuleDto
            {
                Id = module.Id,
                Title = module.Title,
                Description = module.Description,
                EstimatedHours = module.EstimatedHours,
                Order = module.Order,
                ParentModuleId = module.ParentModuleId,
                Notes = module.Notes,
                IsCompleted = module.IsCompleted
            });
        }

        [HttpPut("modules/{moduleId}")]
        public async Task<IActionResult> UpdateModule(int moduleId, [FromBody] UpdateModuleDto dto)
        {
            var userId = GetUserId();
            var module = await _db.CourseModules
                .Include(m => m.Course)
                .FirstOrDefaultAsync(m => m.Id == moduleId && m.Course!.UserId == userId);

            if (module == null)
                return NotFound();

            if (!string.IsNullOrEmpty(dto.Title))
                module.Title = dto.Title;
            if (!string.IsNullOrEmpty(dto.Description))
                module.Description = dto.Description;
            if (dto.EstimatedHours.HasValue)
                module.EstimatedHours = dto.EstimatedHours.Value;
            if (!string.IsNullOrEmpty(dto.Notes))
                module.Notes = dto.Notes;
            if (dto.ParentModuleId.HasValue)
                module.ParentModuleId = dto.ParentModuleId.Value;

            module.Course!.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new CourseModuleDto
            {
                Id = module.Id,
                Title = module.Title,
                Description = module.Description,
                EstimatedHours = module.EstimatedHours,
                Order = module.Order,
                ParentModuleId = module.ParentModuleId,
                Notes = module.Notes,
                IsCompleted = module.IsCompleted
            });
        }

        // External Links Management
        [HttpPost("{courseId}/external-links")]
        public async Task<IActionResult> AddExternalLink(int courseId, [FromBody] CreateExternalLinkDto dto)
        {
            var userId = GetUserId();
            var course = await _db.Courses
                .FirstOrDefaultAsync(c => c.Id == courseId && c.UserId == userId);

            if (course == null)
                return NotFound();

            var link = new ExternalLink
            {
                CourseId = courseId,
                Platform = dto.Platform,
                Title = dto.Title,
                Url = dto.Url,
                CreatedAt = DateTime.UtcNow
            };

            _db.ExternalLinks.Add(link);
            course.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new ExternalLinkDto
            {
                Id = link.Id,
                Platform = link.Platform,
                Title = link.Title,
                Url = link.Url,
                CreatedAt = link.CreatedAt
            });
        }

        [HttpPut("external-links/{linkId}")]
        public async Task<IActionResult> UpdateExternalLink(int linkId, [FromBody] UpdateExternalLinkDto dto)
        {
            var userId = GetUserId();
            var link = await _db.ExternalLinks
                .Include(l => l.Course)
                .FirstOrDefaultAsync(l => l.Id == linkId && l.Course!.UserId == userId);

            if (link == null)
                return NotFound();

            if (!string.IsNullOrEmpty(dto.Platform))
                link.Platform = dto.Platform;
            if (!string.IsNullOrEmpty(dto.Title))
                link.Title = dto.Title;
            if (!string.IsNullOrEmpty(dto.Url))
                link.Url = dto.Url;

            link.Course!.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new ExternalLinkDto
            {
                Id = link.Id,
                Platform = link.Platform,
                Title = link.Title,
                Url = link.Url,
                CreatedAt = link.CreatedAt
            });
        }

        [HttpDelete("external-links/{linkId}")]
        public async Task<IActionResult> DeleteExternalLink(int linkId)
        {
            var userId = GetUserId();
            var link = await _db.ExternalLinks
                .Include(l => l.Course)
                .FirstOrDefaultAsync(l => l.Id == linkId && l.Course!.UserId == userId);

            if (link == null)
                return NotFound();

            link.Course!.UpdatedAt = DateTime.UtcNow;
            _db.ExternalLinks.Remove(link);
            await _db.SaveChangesAsync();

            return Ok(new { message = "External link deleted successfully" });
        }

        // Active Time Tracking
        [HttpPost("{courseId}/active-time")]
        public async Task<IActionResult> UpdateActiveTime(int courseId, [FromBody] UpdateActiveTimeDto dto)
        {
            var userId = GetUserId();
            var course = await _db.Courses
                .FirstOrDefaultAsync(c => c.Id == courseId && c.UserId == userId);

            if (course == null)
                return NotFound();

            // Update last studied time
            course.LastStudiedAt = DateTime.UtcNow;
            course.UpdatedAt = DateTime.UtcNow;

            // Create or update study session for active time tracking
            var activeSession = await _db.StudySessions
                .FirstOrDefaultAsync(s => s.CourseId == courseId && !s.IsCompleted && s.EndTime == null);

            if (activeSession != null)
            {
                // Update existing session duration
                var timeDiff = (DateTime.UtcNow - activeSession.StartTime).TotalHours;
                activeSession.DurationHours = Math.Round((decimal)timeDiff, 2);
            }
            else
            {
                // Create new session for tracking
                activeSession = new StudySession
                {
                    CourseId = courseId,
                    StartTime = DateTime.UtcNow.AddHours((double)-dto.Hours),
                    DurationHours = Math.Round((decimal)dto.Hours, 2),
                    Notes = "",
                    IsCompleted = false,
                    CreatedAt = DateTime.UtcNow
                };
                _db.StudySessions.Add(activeSession);
            }

            // Update course progress based on active time
            var totalCompletedHours = await _db.StudySessions
                .Where(s => s.CourseId == courseId && s.IsCompleted)
                .SumAsync(s => s.DurationHours);

            // Add current active session time
            totalCompletedHours += activeSession.DurationHours;

            course.HoursRemaining = Math.Max(0, course.TotalEstimatedHours - (int)totalCompletedHours);

            await _db.SaveChangesAsync();

            return Ok(new { 
                message = "Active time updated",
                hoursRemaining = course.HoursRemaining,
                activeTime = activeSession.DurationHours
            });
        }
    }

    // DTOs for module and external link updates
    public class UpdateModuleDto
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public int? EstimatedHours { get; set; }
        public string? Notes { get; set; }
        public int? ParentModuleId { get; set; }
    }

    public class CreateModuleDto
    {
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        public int? EstimatedHours { get; set; }
        public int? ParentModuleId { get; set; }
        public string? Notes { get; set; }
    }

    public class UpdateExternalLinkDto
    {
        public string? Platform { get; set; }
        public string? Title { get; set; }
        public string? Url { get; set; }
    }

    public class UpdateActiveTimeDto
    {
        public decimal Hours { get; set; }
    }
}

