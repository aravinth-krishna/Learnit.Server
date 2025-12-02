using System;

namespace Learnit.Server.Models
{
    public class StudySession
    {
        public int Id { get; set; }
        public int CourseId { get; set; }
        public int? CourseModuleId { get; set; } // Optional: which module was studied
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public decimal DurationHours { get; set; } // Actual time spent studying
        public string Notes { get; set; } = ""; // Session notes
        public bool IsCompleted { get; set; } = false; // Whether the session was completed
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public Course? Course { get; set; }
        public CourseModule? CourseModule { get; set; }
    }
}
