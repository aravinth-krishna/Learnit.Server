namespace Learnit.Server.Models
{
    public class CourseResponseDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string SubjectArea { get; set; } = "";
        public string LearningObjectives { get; set; } = "";
        public string Difficulty { get; set; } = "";
        public string Priority { get; set; } = "";
        public int TotalEstimatedHours { get; set; }
        public int HoursRemaining { get; set; }
        public DateTime? TargetCompletionDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<CourseModuleDto> Modules { get; set; } = new();
    }

    public class CourseModuleDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public int EstimatedHours { get; set; }
        public int Order { get; set; }
    }
}

