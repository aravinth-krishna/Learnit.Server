namespace Learnit.Server.Models
{
    public class ScheduleEvent
    {
        public int Id { get; set; }
        public int UserId { get; set; }

        public string Title { get; set; } = "";

        // Stored as UTC in the database
        public DateTime StartUtc { get; set; }
        public DateTime? EndUtc { get; set; }

        public bool AllDay { get; set; }

        // Optional link to course module
        public int? CourseModuleId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property
        public CourseModule? CourseModule { get; set; }
    }
}


