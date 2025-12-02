using System;

namespace Learnit.Server.Models
{
    public class ExternalLink
    {
        public int Id { get; set; }
        public int CourseId { get; set; }
        public string Platform { get; set; } = ""; // Udemy, Coursera, YouTube, Website, etc.
        public string Title { get; set; } = ""; // Link title/description
        public string Url { get; set; } = ""; // The actual URL
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property
        public Course? Course { get; set; }
    }
}
