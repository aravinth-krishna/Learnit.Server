namespace Learnit.Server.Models
{
        public class User
        {
            public int Id { get; set; }
            public string FullName { get; set; } = "";
            public string Email { get; set; } = "";
            public string PasswordHash { get; set; } = "";
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

            // User Preferences
            public string StudySpeed { get; set; } = "normal"; // slow, normal, fast
            public int MaxSessionMinutes { get; set; } = 60;
            public int WeeklyStudyLimitHours { get; set; } = 10;
            public bool DarkMode { get; set; } = false;
        }

}
