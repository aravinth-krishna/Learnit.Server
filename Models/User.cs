namespace Learnit.Server.Models
{
        public class User
        {
            public int Id { get; set; }
            public string FullName { get; set; } = "";
            public string Email { get; set; } = "";
            public string PasswordHash { get; set; } = "";

        public string StudySpeed { get; set; } = "normal";
        public int MaxSessionMinutes { get; set; } = 60;
        public int WeeklyLimitHours { get; set; } = 10;
        public bool DarkMode { get; set; } = false;

    }

}
