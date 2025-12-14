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
            var systemPrompt = "You are Learnit AI. RESPOND WITH ONLY MINIFIED JSON (no prose, no code fences) matching: {title, description, subjectArea, learningObjectives:[3-6 short strings], difficulty, priority, totalEstimatedHours:int, targetCompletionDate:'yyyy-MM-dd', notes, modules:[{title, description, estimatedHours:int, subModules:[{title, description, estimatedHours:int}]}]}. Hard rules: (1) Always include at least 4 modules; each module has >=2 subModules. (2) All estimatedHours are positive integers; if missing, set 2 for modules and 1 for subModules. (3) Make titles/descriptions specific to the prompt. (4) learningObjectives must be outcome-focused bullets (no numbering). (5) targetCompletionDate must be within 30-90 days from today in 'yyyy-MM-dd'. (6) difficulty ∈ {Beginner, Intermediate, Advanced}, priority ∈ {High, Medium, Low}. (7) No markdown, no extra text—pure JSON only.";
            var reply = await _provider.GenerateAsync(systemPrompt, request.Prompt, null, cancellationToken);

            // Temporary diagnostics for client debugging
            Console.WriteLine("[AI raw create-course reply]");
            Console.WriteLine(reply);

            var parsed = TryParseCourseJson(reply) ?? BuildHeuristicCourse(request.Prompt);
            var normalized = NormalizeCourse(parsed);

            Console.WriteLine("[AI parsed course]");
            Console.WriteLine(JsonSerializer.Serialize(normalized));
            return Ok(normalized);
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
                var json = RepairJson(ExtractJsonBlock(reply));
                using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });

                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                {
                    root = root[0];
                }

                var resp = new AiCourseGenerateResponse
                {
                    Title = GetString(root, "title"),
                    Description = GetString(root, "description"),
                    SubjectArea = GetString(root, "subjectArea", "subject_area"),
                    LearningObjectives = ParseLearningObjectives(root),
                    Difficulty = GetString(root, "difficulty"),
                    Priority = GetString(root, "priority"),
                    TotalEstimatedHours = ParseHours(root, 0, "totalEstimatedHours", "total_estimated_hours", "hours"),
                    TargetCompletionDate = GetString(root, "targetCompletionDate", "target_completion_date"),
                    Notes = GetString(root, "notes"),
                    Modules = new List<AiModuleDraft>()
                };

                var modules = GetProperty(root, "modules");
                if (modules?.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in modules.Value.EnumerateArray())
                    {
                        var moduleDraft = new AiModuleDraft
                        {
                            Title = GetString(m, "title", "Module"),
                            Description = GetString(m, "description"),
                            EstimatedHours = ParseHours(m, 0, "estimatedHours", "estimated_hours", "hours"),
                            SubModules = new List<AiSubModuleDraft>()
                        };

                        var subs = GetProperty(m, "subModules") ?? GetProperty(m, "submodules") ?? GetProperty(m, "sub_modules");
                        if (subs?.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var s in subs.Value.EnumerateArray())
                            {
                                moduleDraft.SubModules.Add(new AiSubModuleDraft
                                {
                                    Title = GetString(s, "title", "Submodule"),
                                    EstimatedHours = ParseHours(s, 0, "estimatedHours", "estimated_hours", "hours"),
                                    Description = GetString(s, "description"),
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

        private static JsonElement? GetProperty(JsonElement element, string name)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return prop.Value;
                }
            }

            return null;
        }

        private static string GetString(JsonElement element, string name, string fallback = "")
        {
            return GetString(element, fallback, name);
        }

        private static string GetString(JsonElement element, string fallback, params string[] names)
        {
            foreach (var name in names)
            {
                var prop = GetProperty(element, name);
                if (prop is null) continue;

                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    var val = prop.Value.GetString();
                    if (!string.IsNullOrEmpty(val)) return val;
                }
            }

            return fallback;
        }

        private static string RepairJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return json;

            // Fill missing estimatedHours with 1 when value is absent
            var filledHours = Regex.Replace(
                json,
                @"""estimatedHours""\s*:\s*(?=[}\]]|$)",
                @"""estimatedHours"":1",
                RegexOptions.IgnoreCase | RegexOptions.Multiline);

            // Trim trailing commas before a closing brace/bracket
            var cleaned = Regex.Replace(filledHours, @",\s*(?=[}\]])", string.Empty);

            int openObj = cleaned.Count(c => c == '{');
            int closeObj = cleaned.Count(c => c == '}');
            int openArr = cleaned.Count(c => c == '[');
            int closeArr = cleaned.Count(c => c == ']');

            var sb = new System.Text.StringBuilder(cleaned);
            for (int i = 0; i < openObj - closeObj; i++) sb.Append('}');
            for (int i = 0; i < openArr - closeArr; i++) sb.Append(']');

            return sb.ToString();
        }

        private static string ParseLearningObjectives(JsonElement root)
        {
            var goalsProp = GetProperty(root, "learningObjectives");
            if (goalsProp is null) return string.Empty;

            var goals = goalsProp.Value;
            if (goals.ValueKind == JsonValueKind.Array)
            {
                var items = goals
                    .EnumerateArray()
                    .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : null)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!.Trim())
                    .ToArray();

                return string.Join("; ", items);
            }

            return goals.GetString() ?? string.Empty;
        }

        private static int ParseHours(JsonElement element, int fallback, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                var propNullable = GetProperty(element, name);
                if (propNullable is null) continue;
                var prop = propNullable.Value;

                try
                {
                    var value = prop.ValueKind switch
                    {
                        JsonValueKind.Number when prop.TryGetInt32(out var i) => i,
                        JsonValueKind.Number when prop.TryGetDouble(out var d) => (int)Math.Round(d),
                        JsonValueKind.String when int.TryParse(prop.GetString(), out var s) => s,
                        _ => (int?)null
                    };

                    if (value.HasValue) return value.Value;
                }
                catch
                {
                    // continue trying other names
                }
            }

            return fallback;
        }

        private static AiCourseGenerateResponse NormalizeCourse(AiCourseGenerateResponse resp)
        {
            resp.Title = string.IsNullOrWhiteSpace(resp.Title) ? "Course Plan" : resp.Title.Trim();
            resp.Description = resp.Description?.Trim() ?? string.Empty;
            resp.SubjectArea = resp.SubjectArea?.Trim() ?? string.Empty;
            resp.Difficulty = string.IsNullOrWhiteSpace(resp.Difficulty) ? "Balanced" : resp.Difficulty.Trim();
            resp.Priority = string.IsNullOrWhiteSpace(resp.Priority) ? "Medium" : resp.Priority.Trim();
            resp.Notes = resp.Notes?.Trim() ?? string.Empty;

            if (resp.Modules == null)
            {
                resp.Modules = new List<AiModuleDraft>();
            }

            if (!resp.Modules.Any())
            {
                resp.Modules.Add(new AiModuleDraft
                {
                    Title = "Module 1",
                    Description = "Getting started",
                    EstimatedHours = resp.TotalEstimatedHours > 0 ? resp.TotalEstimatedHours : 4,
                    SubModules = new List<AiSubModuleDraft>
                    {
                        new() { Title = "Lesson 1", EstimatedHours = 2, Description = "Overview" },
                        new() { Title = "Lesson 2", EstimatedHours = 2, Description = "Practice" }
                    }
                });
            }

            foreach (var module in resp.Modules)
            {
                module.Title = string.IsNullOrWhiteSpace(module.Title) ? "Module" : module.Title.Trim();
                module.Description = module.Description?.Trim() ?? string.Empty;
                module.EstimatedHours = module.EstimatedHours <= 0 ? 1 : module.EstimatedHours;

                if (module.SubModules == null)
                {
                    module.SubModules = new List<AiSubModuleDraft>();
                }

                if (!module.SubModules.Any())
                {
                    module.SubModules.Add(new AiSubModuleDraft
                    {
                        Title = "Submodule",
                        EstimatedHours = Math.Max(1, module.EstimatedHours / 2),
                        Description = ""
                    });
                }

                foreach (var sub in module.SubModules)
                {
                    sub.Title = string.IsNullOrWhiteSpace(sub.Title) ? "Submodule" : sub.Title.Trim();
                    sub.Description = sub.Description?.Trim() ?? string.Empty;
                    sub.EstimatedHours = sub.EstimatedHours <= 0 ? 1 : sub.EstimatedHours;
                }
            }

            if (resp.TotalEstimatedHours <= 0)
            {
                resp.TotalEstimatedHours = resp.Modules.Sum(m => Math.Max(1, m.EstimatedHours));
            }

            if (string.IsNullOrWhiteSpace(resp.LearningObjectives))
            {
                resp.LearningObjectives = string.Join("; ", resp.Modules.Take(3).Select(m => $"Complete {m.Title}"));
            }

            if (string.IsNullOrWhiteSpace(resp.TargetCompletionDate))
            {
                resp.TargetCompletionDate = DateTime.UtcNow.AddDays(28).ToString("yyyy-MM-dd");
            }

            return resp;
        }

        private static AiCourseGenerateResponse BuildHeuristicCourse(string prompt)
        {
            // Stronger heuristic when LLM output is unusable or stubbed
            var trimmed = (prompt ?? string.Empty).Trim();
            var subject = DeriveSubject(trimmed);
            var weeks = ExtractWeeks(trimmed);
            var now = DateTime.UtcNow;
            var targetDate = now.AddDays(Math.Max(21, weeks * 7)).ToString("yyyy-MM-dd");

            var totalHours = weeks > 0 ? Math.Max(12, weeks * 6) : 24;
            var modules = weeks > 0
                ? BuildWeekModules(subject, weeks, totalHours)
                : BuildDefaultModules(subject, totalHours);

            var goals = new[]
            {
                $"Understand the fundamentals of {subject}",
                $"Apply {subject} in guided exercises",
                $"Build and ship a small {subject} project"
            };

            return new AiCourseGenerateResponse
            {
                Title = DeriveTitle(trimmed),
                Description = string.IsNullOrWhiteSpace(trimmed)
                    ? "Auto-generated plan based on your request."
                    : trimmed,
                SubjectArea = subject,
                LearningObjectives = string.Join("; ", goals),
                Difficulty = "Balanced",
                Priority = "Medium",
                TotalEstimatedHours = totalHours,
                TargetCompletionDate = targetDate,
                Notes = "Heuristic plan generated because AI response was not structured JSON.",
                Modules = modules
            };
        }

        private static string DeriveTitle(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return "Tailored Course Plan";
            var words = prompt.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Take(12);
            var title = string.Join(' ', words);
            return title.Length > 4 ? title : "Tailored Course Plan";
        }

        private static string DeriveSubject(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return "Custom Course";

            var lower = prompt.ToLowerInvariant();
            if (lower.Contains("node.js") || lower.Contains("nodejs") || lower.Contains("node js") || lower.Contains("node"))
                return "Node.js";

            var cleaned = Regex.Replace(prompt, "\\b\\d+\\s*-?\\s*week(s)?\\b", string.Empty, RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, "\\b(course|learn|learning|about|basics|foundation|foundations)\\b", "", RegexOptions.IgnoreCase);
            var tokens = cleaned
                .Split(new[] { ' ', ',', ';', '.' }, StringSplitOptions.RemoveEmptyEntries)
                .Take(6)
                .ToArray();

            var subject = tokens.Length == 0 ? "Custom Course" : string.Join(" ", tokens);
            return subject.Trim();
        }

        private static int ExtractWeeks(string prompt)
        {
            var match = Regex.Match(prompt ?? string.Empty, "(?<num>\\d+)\\s*-?\\s*week", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups["num"].Value, out var weeks) && weeks > 0 && weeks <= 52)
            {
                return weeks;
            }
            return 0;
        }

        private static List<AiModuleDraft> BuildWeekModules(string subject, int weeks, int totalHours)
        {
            var modules = new List<AiModuleDraft>();
            var hoursPerWeek = DistributeHours(totalHours, weeks);

            for (int i = 0; i < weeks; i++)
            {
                var weekNumber = i + 1;
                var hours = hoursPerWeek[i];
                modules.Add(new AiModuleDraft
                {
                    Title = $"Week {weekNumber}: {subject}",
                    Description = weekNumber == weeks
                        ? "Capstone and consolidation"
                        : "Concepts and practice",
                    EstimatedHours = hours,
                    SubModules = new List<AiSubModuleDraft>
                    {
                        new() { Title = "Concepts", EstimatedHours = Math.Max(1, hours / 3), Description = "Key topics" },
                        new() { Title = "Hands-on", EstimatedHours = Math.Max(1, hours / 3), Description = "Guided exercises" },
                        new() { Title = "Review / Project", EstimatedHours = Math.Max(1, hours - 2 * Math.Max(1, hours / 3)), Description = "Apply and reflect" }
                    }
                });
            }

            return modules;
        }

        private static List<AiModuleDraft> BuildDefaultModules(string subject, int totalHours)
        {
            var modules = new List<AiModuleDraft>();
            var hoursPerModule = DistributeHours(totalHours, 3);

            modules.Add(new AiModuleDraft
            {
                Title = $"Foundations: {subject}",
                Description = "Key concepts, terminology, and setup.",
                EstimatedHours = hoursPerModule[0],
                SubModules = new List<AiSubModuleDraft>
                {
                    new() { Title = "Basics", EstimatedHours = Math.Max(1, hoursPerModule[0] / 3), Description = "Core principles" },
                    new() { Title = "Setup", EstimatedHours = Math.Max(1, hoursPerModule[0] / 3), Description = "Environment and tooling" },
                    new() { Title = "First steps", EstimatedHours = Math.Max(1, hoursPerModule[0] - 2 * Math.Max(1, hoursPerModule[0] / 3)), Description = "Hello world" }
                }
            });

            modules.Add(new AiModuleDraft
            {
                Title = $"Practice: {subject}",
                Description = "Apply skills with guided exercises.",
                EstimatedHours = hoursPerModule[1],
                SubModules = new List<AiSubModuleDraft>
                {
                    new() { Title = "Core exercises", EstimatedHours = Math.Max(1, hoursPerModule[1] / 3), Description = "Hands-on drills" },
                    new() { Title = "Patterns", EstimatedHours = Math.Max(1, hoursPerModule[1] / 3), Description = "Common approaches" },
                    new() { Title = "Review", EstimatedHours = Math.Max(1, hoursPerModule[1] - 2 * Math.Max(1, hoursPerModule[1] / 3)), Description = "Checkpoint" }
                }
            });

            modules.Add(new AiModuleDraft
            {
                Title = $"Project: {subject}",
                Description = "Build a small project to consolidate learning.",
                EstimatedHours = hoursPerModule[2],
                SubModules = new List<AiSubModuleDraft>
                {
                    new() { Title = "Plan", EstimatedHours = Math.Max(1, hoursPerModule[2] / 4), Description = "Define scope" },
                    new() { Title = "Build", EstimatedHours = Math.Max(1, hoursPerModule[2] / 2), Description = "Implement" },
                    new() { Title = "Polish", EstimatedHours = Math.Max(1, hoursPerModule[2] - Math.Max(1, hoursPerModule[2] / 4) - Math.Max(1, hoursPerModule[2] / 2)), Description = "Test and refine" }
                }
            });

            return modules;
        }

        private static int[] DistributeHours(int total, int buckets)
        {
            var safeTotal = Math.Max(total, buckets);
            var baseValue = safeTotal / buckets;
            var remainder = safeTotal % buckets;
            var arr = new int[buckets];
            for (int i = 0; i < buckets; i++)
            {
                arr[i] = baseValue + (i < remainder ? 1 : 0);
                if (arr[i] <= 0) arr[i] = 1;
            }
            return arr;
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
