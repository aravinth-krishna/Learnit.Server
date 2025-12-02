namespace Learnit.Server.Models
{
    public class Course
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string SubjectArea { get; set; } = "";
        public string LearningObjectives { get; set; } = "";
        public string Difficulty { get; set; } = ""; // Beginner, Intermediate, Advanced
        public string Priority { get; set; } = ""; // Low, Medium, High
        public int TotalEstimatedHours { get; set; }
        public int HoursRemaining { get; set; }
        public DateTime? TargetCompletionDate { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        
        // Navigation property
        public List<CourseModule> Modules { get; set; } = new();
    }
}

