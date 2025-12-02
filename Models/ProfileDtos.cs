namespace Learnit.Server.Models
{
    public class UserProfileDto
    {
        public int Id { get; set; }
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    public class UpdateProfileDto
    {
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
    }

    public class ChangePasswordDto
    {
        public string CurrentPassword { get; set; } = "";
        public string NewPassword { get; set; } = "";
        public string ConfirmPassword { get; set; } = "";
    }

    public class UserPreferencesDto
    {
        public string StudySpeed { get; set; } = "normal"; // slow, normal, fast
        public int MaxSessionMinutes { get; set; } = 60;
        public int WeeklyStudyLimitHours { get; set; } = 10;
        public bool DarkMode { get; set; } = false;
    }
}
