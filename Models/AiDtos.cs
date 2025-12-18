namespace Learnit.Server.Models
{
    public class AiChatRequest
    {
        public string Message { get; set; } = "";
        public List<AiChatMessage> History { get; set; } = new();
        public string Mode { get; set; } = "general"; // general | schedule | progress | course
    }

    public class AiChatMessage
    {
        public string Role { get; set; } = "user"; // user|assistant
        public string Content { get; set; } = "";
    }

    public class AiChatResponse
    {
        public string Reply { get; set; } = "";
    }

    public class AiCourseGenerateRequest
    {
        public string Prompt { get; set; } = "";
    }

    public class AiCourseGenerateResponse
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string SubjectArea { get; set; } = "";
        public string LearningObjectives { get; set; } = "";
        public string Difficulty { get; set; } = "Balanced";
        public string Priority { get; set; } = "Medium";
        public int TotalEstimatedHours { get; set; }
            = 10;
        public string TargetCompletionDate { get; set; } = "";
        public string Notes { get; set; } = "";
        public List<AiModuleDraft> Modules { get; set; } = new();
    }

    public class AiModuleDraft
    {
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        public int EstimatedHours { get; set; } = 2;
        public List<AiSubModuleDraft> SubModules { get; set; } = new();
    }

    public class AiSubModuleDraft
    {
        public string Title { get; set; } = "";
        public int EstimatedHours { get; set; } = 1;
        public string? Description { get; set; }
    }

    public class AiInsightRequest
    {
        public string Prompt { get; set; } = "";
    }

    public class AiInsight
    {
        public string Title { get; set; } = "";
        public string Detail { get; set; } = "";
        public string? ModuleSuggestion { get; set; }
            = null;
    }

    public class AiInsightResponse
    {
        public List<AiInsight> Insights { get; set; } = new();
    }

    public class FriendDto
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string DisplayName { get; set; } = "";
        public string Email { get; set; } = "";
        public int FriendUserId { get; set; }
        public decimal CompletionRate { get; set; }
            = 0;
        public decimal WeeklyHours { get; set; }
            = 0;
    }

    public class FriendCompareResponse
    {
        public List<FriendDto> Friends { get; set; } = new();
        public List<AiInsight> Insights { get; set; } = new();
    }

    public class FriendCompareRequest
    {
        public List<string> FriendIds { get; set; } = new();
    }
}
