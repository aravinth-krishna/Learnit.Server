namespace Learnit.Server.Models
{
    public class CourseModule
    {
        public int Id { get; set; }
        public int CourseId { get; set; }
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public int EstimatedHours { get; set; }
        public int Order { get; set; }
        public int? ParentModuleId { get; set; } // For nested modules
        public string Notes { get; set; } = "";
        public bool IsCompleted { get; set; } = false;
        
        // Navigation properties
        public Course? Course { get; set; }
        public CourseModule? ParentModule { get; set; }
        public List<CourseModule> SubModules { get; set; } = new();
    }
}

