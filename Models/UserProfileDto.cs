namespace Learnit.Server.Models
{
    public class UserProfileDto
    {
        public int Id { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }

        public string StudySpeed { get; set; }
        public int MaxSessionMinutes { get; set; }
        public int WeeklyLimitHours { get; set; }

        public bool DarkMode { get; set; }
    }
}
