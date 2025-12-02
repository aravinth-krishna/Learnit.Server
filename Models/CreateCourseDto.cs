namespace Learnit.Server.Models
{
    public class CreateCourseDto
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string SubjectArea { get; set; } = "";
        public string LearningObjectives { get; set; } = "";
        public string Difficulty { get; set; } = "";
        public string Priority { get; set; } = "";
        public int TotalEstimatedHours { get; set; }
        public DateTime? TargetCompletionDate { get; set; }
        public List<CreateCourseModuleDto> Modules { get; set; } = new();
    }

    public class CreateCourseModuleDto
    {
        public string Title { get; set; } = "";
        public int EstimatedHours { get; set; }
    }
}

