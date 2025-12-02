namespace Learnit.Server.Models
{
    public class ScheduleEventDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public DateTime StartUtc { get; set; }
        public DateTime? EndUtc { get; set; }
        public bool AllDay { get; set; }
        public int? CourseModuleId { get; set; }
        public CourseModuleInfo? CourseModule { get; set; }
    }

    public class CreateScheduleEventDto
    {
        public string Title { get; set; } = "";
        public DateTime StartUtc { get; set; }
        public DateTime? EndUtc { get; set; }
        public bool AllDay { get; set; }
        public int? CourseModuleId { get; set; }
    }

    public class CourseModuleInfo
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public int CourseId { get; set; }
        public string CourseTitle { get; set; } = "";
    }
}


