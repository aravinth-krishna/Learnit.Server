using System;

namespace Learnit.Server.Models
{
    public class ActivityLog
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public DateTime Date { get; set; } // Date of the activity
        public decimal HoursCompleted { get; set; }
        public int ActivityLevel { get; set; } // 0-3 scale based on hours completed

        // Navigation property
        public User? User { get; set; }
    }
}
