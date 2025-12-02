namespace Learnit.Server.Models
{
    public class CourseModule
    {
        public int Id { get; set; }
        public int CourseId { get; set; }
        public string Title { get; set; } = "";
        public int EstimatedHours { get; set; }
        public int Order { get; set; }
        
        // Navigation property
        public Course? Course { get; set; }
    }
}

