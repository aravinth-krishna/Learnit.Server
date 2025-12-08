using Learnit.Server.Data;
using Learnit.Server.Models;
using Learnit.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Linq;

namespace Learnit.Server.Controllers
{
    [ApiController]
    [Route("api/ai")]
    [Authorize]
    public class AiController : ControllerBase
    {
        private readonly IAiProvider _provider;
        private readonly AiContextBuilder _contextBuilder;
        private readonly AppDbContext _db;
        private readonly FriendService _friends;

        public AiController(IAiProvider provider, AiContextBuilder contextBuilder, AppDbContext db, FriendService friends)
        {
            _provider = provider;
            _contextBuilder = contextBuilder;
            _db = db;
            _friends = friends;
        }

        private int GetUserId()
        {
            var userIdClaim = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                throw new UnauthorizedAccessException("Invalid user token");

            return userId;
        }

        [HttpPost("chat")]
        public async Task<ActionResult<AiChatResponse>> Chat([FromBody] AiChatRequest request, CancellationToken cancellationToken)
        {
            var userId = GetUserId();
            var context = await _contextBuilder.BuildContextAsync(userId, cancellationToken);

            var systemPrompt = "You are Learnit AI. Be concise (<=6 bullets). Suggest next actions with course/module names and short time estimates. If you propose scheduling, include duration.";

            var history = request.History?.Select(h => new AiMessage(h.Role, h.Content)) ?? Enumerable.Empty<AiMessage>();

            var prompt = $"User asked: {request.Message}\n\nContext:\n{context}";
            var reply = await _provider.GenerateAsync(systemPrompt, prompt, history, cancellationToken);

            return Ok(new AiChatResponse { Reply = reply });
        }

        [HttpPost("create-course")]
        public async Task<ActionResult<AiCourseGenerateResponse>> CreateCourse([FromBody] AiCourseGenerateRequest request, CancellationToken cancellationToken)
        {
            var systemPrompt = "You draft detailed, user-specific course plans. Respond with JSON ONLY (no prose). Schema: {title, description, subjectArea, learningObjectives (array of 3-6 short goals), difficulty, priority, totalEstimatedHours (int), targetCompletionDate (yyyy-MM-dd), notes, modules:[{title, description, estimatedHours (int), subModules:[{title, description, estimatedHours}]}]}. Honor the user's prompt and customize subjects, goals, and module names/hours to their needs.";
            var reply = await _provider.GenerateAsync(systemPrompt, request.Prompt, null, cancellationToken);

            var parsed = TryParseCourseJson(reply) ?? BuildHeuristicCourse(request.Prompt);
            return Ok(parsed);
        }

        private static string ExtractJsonBlock(string reply)
        {
            // Attempt to pull a JSON object even if the model added prose around it
            try
            {
                var fenceMatch = Regex.Match(reply, "```json\\s*(?<json>{[\\s\\S]*?})\\s*```", RegexOptions.IgnoreCase);
                if (fenceMatch.Success)
                    return fenceMatch.Groups["json"].Value;

                var firstBrace = reply.IndexOf('{');
                var lastBrace = reply.LastIndexOf('}');
                if (firstBrace >= 0 && lastBrace > firstBrace)
                    return reply.Substring(firstBrace, lastBrace - firstBrace + 1);
            }
            catch
            {
                // ignore and fall through
            }

            return reply;
        }

        private static AiCourseGenerateResponse? TryParseCourseJson(string reply)
        {
            try
            {
                var json = ExtractJsonBlock(reply);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var resp = new AiCourseGenerateResponse
                {
                    Title = root.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? "Generated Course" : "Generated Course",
                    Description = root.TryGetProperty("description", out var descEl) ? descEl.GetString() ?? string.Empty : string.Empty,
                    SubjectArea = root.TryGetProperty("subjectArea", out var subj) ? subj.GetString() ?? string.Empty : string.Empty,
                    LearningObjectives = root.TryGetProperty("learningObjectives", out var goals)
                        ? goals.ValueKind == JsonValueKind.Array
                            ? string.Join("; ", goals.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)))
                            : goals.GetString() ?? string.Empty
                        : string.Empty,
                    Difficulty = root.TryGetProperty("difficulty", out var diff) ? diff.GetString() ?? "Balanced" : "Balanced",
                    Priority = root.TryGetProperty("priority", out var pr) ? pr.GetString() ?? "Medium" : "Medium",
                    TotalEstimatedHours = root.TryGetProperty("totalEstimatedHours", out var hrs) ? hrs.GetInt32() : 10,
                    TargetCompletionDate = root.TryGetProperty("targetCompletionDate", out var tcd) ? tcd.GetString() ?? string.Empty : string.Empty,
                    Notes = root.TryGetProperty("notes", out var nts) ? nts.GetString() ?? string.Empty : string.Empty,
                    Modules = new List<AiModuleDraft>()
                };

                if (root.TryGetProperty("modules", out var modules) && modules.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in modules.EnumerateArray())
                    {
                        var moduleDraft = new AiModuleDraft
                        {
                            Title = m.TryGetProperty("title", out var mt) ? mt.GetString() ?? "Module" : "Module",
                            Description = m.TryGetProperty("description", out var md) ? md.GetString() ?? string.Empty : string.Empty,
                            EstimatedHours = m.TryGetProperty("estimatedHours", out var eh) ? eh.GetInt32() : 2,
                            SubModules = new List<AiSubModuleDraft>()
                        };

                        if (m.TryGetProperty("subModules", out var subs) && subs.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var s in subs.EnumerateArray())
                            {
                                moduleDraft.SubModules.Add(new AiSubModuleDraft
                                {
                                    Title = s.TryGetProperty("title", out var st) ? st.GetString() ?? "Submodule" : "Submodule",
                                    EstimatedHours = s.TryGetProperty("estimatedHours", out var sh) ? sh.GetInt32() : 1,
                                    Description = s.TryGetProperty("description", out var sd) ? sd.GetString() ?? string.Empty : string.Empty
                                });
                            }
                        }

                        resp.Modules.Add(moduleDraft);
                    }
                }

                if (resp.Modules.Count == 0)
                {
                    resp.Modules.Add(new AiModuleDraft { Title = "Module 1", EstimatedHours = 2 });
                }

                return resp;
            }
            catch
            {
                return null;
            }
        }

        private static AiCourseGenerateResponse BuildHeuristicCourse(string prompt)
        {
            // Basic heuristic draft when LLM output is unusable or stubbed
            var trimmed = (prompt ?? string.Empty).Trim();
            var subjectTokens = string.IsNullOrWhiteSpace(trimmed)
                ? Array.Empty<string>()
                : trimmed.Split(new[] { ' ', ',', ';', '.' }, StringSplitOptions.RemoveEmptyEntries).Take(4).ToArray();
            var subject = subjectTokens.Length == 0 ? "Custom Course" : string.Join(" ", subjectTokens);

            string TitleFromPrompt()
            {
                if (string.IsNullOrWhiteSpace(trimmed)) return "Tailored Course Plan";
                var words = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Take(10);
                var title = string.Join(' ', words);
                return title.Length > 4 ? title : "Tailored Course Plan";
            }

            var goals = new[]
            {
                $"Understand the fundamentals of {subject}",
                $"Apply {subject} in hands-on exercises",
                $"Deliver a small project using {subject}"
            };

            var now = DateTime.UtcNow;
            return new AiCourseGenerateResponse
            {
                Title = TitleFromPrompt(),
                Description = string.IsNullOrWhiteSpace(trimmed)
                    ? "Auto-generated plan based on your request."
                    : trimmed,
                SubjectArea = subject,
                LearningObjectives = string.Join("; ", goals),
                Difficulty = "Balanced",
                Priority = "Medium",
                TotalEstimatedHours = 24,
                TargetCompletionDate = now.AddDays(28).ToString("yyyy-MM-dd"),
                Notes = "Heuristic plan generated because AI response was not structured JSON.",
                Modules = new List<AiModuleDraft>
                {
                    new()
                    {
                        Title = $"Foundations: {subject}",
                        Description = "Key concepts, terminology, and setup.",
                        EstimatedHours = 6,
                        SubModules = new List<AiSubModuleDraft>
                        {
                            new() { Title = "Basics", EstimatedHours = 2, Description = "Core principles" },
                            new() { Title = "Setup", EstimatedHours = 2, Description = "Environment and tooling" },
                            new() { Title = "First steps", EstimatedHours = 2, Description = "Hello world and simple task" }
                        }
                    },
                    new()
                    {
                        Title = $"Practice: {subject}",
                        Description = "Apply skills with guided exercises.",
                        EstimatedHours = 8,
                        SubModules = new List<AiSubModuleDraft>
                        {
                            new() { Title = "Core exercises", EstimatedHours = 3, Description = "Hands-on drills" },
                            new() { Title = "Patterns", EstimatedHours = 3, Description = "Common approaches" },
                            new() { Title = "Review", EstimatedHours = 2, Description = "Checkpoint and feedback" }
                        }
                    },
                    new()
                    {
                        Title = $"Project: {subject}",
                        Description = "Build a small project to consolidate learning.",
                        EstimatedHours = 10,
                        SubModules = new List<AiSubModuleDraft>
                        {
                            new() { Title = "Plan", EstimatedHours = 2, Description = "Define scope" },
                            new() { Title = "Build", EstimatedHours = 6, Description = "Implement features" },
                            new() { Title = "Polish", EstimatedHours = 2, Description = "Test and refine" }
                        }
                    }
                }
            };
        }

        [HttpPost("schedule-insights")]
        public async Task<ActionResult<AiInsightResponse>> ScheduleInsights([FromBody] AiInsightRequest request, CancellationToken cancellationToken)
        {
            var userId = GetUserId();
            var context = await _contextBuilder.BuildContextAsync(userId, cancellationToken);
            var systemPrompt = "Provide 3 concise scheduling suggestions. Each should include duration and target module. Return bullet text.";
            var reply = await _provider.GenerateAsync(systemPrompt, request.Prompt + "\nContext:\n" + context, null, cancellationToken);
            return Ok(new AiInsightResponse
            {
                Insights = new List<AiInsight>
                {
                    new() { Title = string.Empty, Detail = reply }
                }
            });
        }

        [HttpPost("progress-insights")]
        public async Task<ActionResult<AiInsightResponse>> ProgressInsights([FromBody] AiInsightRequest request, CancellationToken cancellationToken)
        {
            var userId = GetUserId();
            var context = await _contextBuilder.BuildContextAsync(userId, cancellationToken);
            var systemPrompt = "Provide 3 concise progress insights and next best actions. Keep each under 140 chars.";
            var reply = await _provider.GenerateAsync(systemPrompt, request.Prompt + "\nContext:\n" + context, null, cancellationToken);
            return Ok(new AiInsightResponse
            {
                Insights = new List<AiInsight>
                {
                    new() { Title = string.Empty, Detail = reply }
                }
            });
        }

        [HttpPost("compare")]
        public async Task<ActionResult<FriendCompareResponse>> Compare([FromBody] FriendCompareRequest request, CancellationToken cancellationToken)
        {
            var userId = GetUserId();
            var context = await _contextBuilder.BuildContextAsync(userId, cancellationToken);

            var selected = await _friends.GetFriendsByIdsAsync(userId, request.FriendIds.Take(2), cancellationToken);
            if (selected.Count == 0)
            {
                return Ok(new FriendCompareResponse
                {
                    Friends = new List<FriendDto>(),
                    Insights = new List<AiInsight>
                    {
                        new() { Title = "", Detail = "Pick one friend to compare." }
                    }
                });
            }

            var friend = selected.First();
            var friendsSummary = $"Friend {friend.DisplayName}: {friend.CompletionRate}% done, {friend.WeeklyHours}h/wk";
            var systemPrompt = "You are Learnit AI. Compare the current user versus one friend as a benchmark. Prioritize the user. Provide: (1) a short comparison of completion % and weekly hours (user vs friend), (2) 3 actionable next steps for the user. Do NOT use tables or markdown tables; use bullets or short paragraphs only. Keep it friendly and concise.";
            var reply = await _provider.GenerateAsync(systemPrompt, "Friend: " + friendsSummary + "\nContext (user):\n" + context, null, cancellationToken);

            return Ok(new FriendCompareResponse
            {
                Friends = selected,
                Insights = new List<AiInsight>
                {
                    new() { Title = string.Empty, Detail = reply }
                }
            });
        }

        private static List<AiInsight> SplitBullets(string text)
        {
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(l => l.Length > 0)
                .Take(5)
                .ToList();

            return lines.Select((l, i) => new AiInsight
            {
                Title = $"Idea {i + 1}",
                Detail = l
            }).ToList();
        }
    }
}
