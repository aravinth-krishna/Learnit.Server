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
            var systemPrompt = "You are Learnit AI. RESPOND WITH ONLY MINIFIED JSON (no prose, no code fences) matching: {title, description (optional, can be empty), subjectArea, learningObjectives:[3-6 short strings], difficulty, priority, totalEstimatedHours:int, targetCompletionDate:'yyyy-MM-dd', notes, modules:[{title, description (optional), estimatedHours:int, subModules:[{title, description (optional), estimatedHours:int}]}]}. Hard rules: (1) Always include at least 4 modules; each module has >=2 subModules. (2) All estimatedHours are positive integers; if missing, set 2 for modules and 1 for subModules. (3) Module/subModule titles must be plain titles only — do NOT prefix titles with labels like 'Module 2:' or 'Submodule 2.1:'. (4) Make titles specific to the prompt; descriptions are optional. (5) learningObjectives must be outcome-focused bullets (no numbering). (6) targetCompletionDate must be within 30-90 days from today in 'yyyy-MM-dd'. (7) difficulty ∈ {Beginner, Intermediate, Advanced}, priority ∈ {High, Medium, Low}. (8) No markdown, no extra text—pure JSON only. (9) Ensure the JSON is syntactically valid; never put objects in quotes (no \"{...}\").";
            var reply = await _provider.GenerateAsync(systemPrompt, request.Prompt, null, cancellationToken);

            // Temporary diagnostics for client debugging
            Console.WriteLine("[AI raw create-course reply]");
            Console.WriteLine(reply);

            var parsed = TryParseCourseJson(reply, request.Prompt) ?? BuildHeuristicCourse(request.Prompt);
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

        private static AiCourseGenerateResponse? TryParseCourseJson(string reply, string prompt)
        {
            foreach (var candidate in BuildJsonCandidates(ExtractJsonBlock(reply)))
            {
                try
                {
                    using var doc = JsonDocument.Parse(candidate, new JsonDocumentOptions
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
                                EstimatedHours = ParseHours(m, 1, "estimatedHours", "estimated_hours", "hours"),
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
                                        EstimatedHours = ParseHours(s, 1, "estimatedHours", "estimated_hours", "hours"),
                                        Description = GetString(s, "description"),
                                    });
                                }
                            }

                            resp.Modules.Add(moduleDraft);
                        }
                    }

                    if (resp.Modules.Count == 0)
                    {
                        resp.Modules.Add(new AiModuleDraft { Title = "Getting started", EstimatedHours = 2 });
                    }

                    return resp;
                }
                catch
                {
                    // try next candidate
                }
            }

            // Salvage just the modules when the full parse fails (common with truncated JSON)
            var salvage = ParseModulesOnly(reply, prompt);
            if (salvage is not null) return salvage;

            return null;
        }

        private static AiCourseGenerateResponse? ParseModulesOnly(string reply, string prompt)
        {
            var idx = reply.IndexOf("\"modules\"", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;

            var startBracket = reply.IndexOf('[', idx);
            if (startBracket < 0) return null;

            int depth = 0;
            for (int i = startBracket; i < reply.Length; i++)
            {
                if (reply[i] == '[') depth++;
                if (reply[i] == ']') depth--;
                if (depth == 0)
                {
                    var modulesSlice = reply.Substring(startBracket, i - startBracket + 1);
                    var candidate = RepairJson($"{{\"modules\": {modulesSlice}}}");
                    try
                    {
                        using var doc = JsonDocument.Parse(candidate, new JsonDocumentOptions
                        {
                            AllowTrailingCommas = true,
                            CommentHandling = JsonCommentHandling.Skip,
                        });

                        var resp = new AiCourseGenerateResponse
                        {
                            Title = DeriveTitle(prompt),
                            SubjectArea = DeriveSubject(prompt),
                            Difficulty = "Beginner",
                            Priority = "Medium",
                            TargetCompletionDate = DateTime.UtcNow.AddDays(28).ToString("yyyy-MM-dd"),
                            Modules = new List<AiModuleDraft>()
                        };

                        var modules = doc.RootElement.GetProperty("modules");
                        if (modules.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var m in modules.EnumerateArray())
                            {
                                var moduleDraft = new AiModuleDraft
                                {
                                    Title = GetString(m, "title", "Module"),
                                    Description = string.Empty,
                                    EstimatedHours = ParseHours(m, 2, "estimatedHours", "hours"),
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
                                            EstimatedHours = ParseHours(s, 1, "estimatedHours", "hours"),
                                            Description = string.Empty,
                                        });
                                    }
                                }

                                resp.Modules.Add(moduleDraft);
                            }
                        }

                        if (!resp.Modules.Any()) return null;

                        resp.TotalEstimatedHours = resp.Modules.Sum(m => Math.Max(1, m.EstimatedHours));
                        return resp;
                    }
                    catch
                    {
                        return null;
                    }
                }
            }

            // If the reply is truncated mid-array, salvage what we have by repairing + closing.
            try
            {
                var modulesSlice = reply.Substring(startBracket);
                var candidate = RepairJson($"{{\"modules\": {modulesSlice}}}");

                using var doc = JsonDocument.Parse(candidate, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });

                var resp = new AiCourseGenerateResponse
                {
                    Title = DeriveTitle(prompt),
                    SubjectArea = DeriveSubject(prompt),
                    Difficulty = "Beginner",
                    Priority = "Medium",
                    TargetCompletionDate = DateTime.UtcNow.AddDays(45).ToString("yyyy-MM-dd"),
                    Modules = new List<AiModuleDraft>()
                };

                var modules = doc.RootElement.GetProperty("modules");
                if (modules.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in modules.EnumerateArray())
                    {
                        var moduleDraft = new AiModuleDraft
                        {
                            Title = GetString(m, "title", "Module"),
                            Description = string.Empty,
                            EstimatedHours = ParseHours(m, 2, "estimatedHours", "hours"),
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
                                    EstimatedHours = ParseHours(s, 1, "estimatedHours", "hours"),
                                    Description = string.Empty,
                                });
                            }
                        }

                        resp.Modules.Add(moduleDraft);
                    }
                }

                if (!resp.Modules.Any()) return null;
                resp.TotalEstimatedHours = resp.Modules.Sum(m => Math.Max(1, m.EstimatedHours));
                return resp;
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<string> BuildJsonCandidates(string raw)
        {
            if (!string.IsNullOrWhiteSpace(raw))
            {
                yield return raw;

                var trimmed = TrimAfterLastBrace(raw);
                if (trimmed != raw)
                    yield return trimmed;

                var repaired = RepairJson(raw);
                if (repaired != raw)
                    yield return repaired;

                var repairedTrimmed = RepairJson(trimmed);
                if (repairedTrimmed != repaired)
                    yield return repairedTrimmed;
            }
        }

        private static string TrimAfterLastBrace(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            var lastObj = text.LastIndexOf('}');
            var lastArr = text.LastIndexOf(']');
            var cut = Math.Max(lastObj, lastArr);
            return cut > 0 ? text.Substring(0, cut + 1) : text;
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
            // Important: avoid overload recursion by binding explicitly to the params overload.
            return GetString(element, fallback, names: new[] { name });
        }

        private static string GetString(JsonElement element, string fallback, params string[] names)
        {
            foreach (var name in names)
            {
                var prop = GetProperty(element, name);
                if (prop is null) continue;

                try
                {
                    var valueKind = prop.Value.ValueKind;
                    if (valueKind == JsonValueKind.String)
                    {
                        var val = prop.Value.GetString();
                        if (!string.IsNullOrEmpty(val)) return val;
                    }
                    else if (valueKind == JsonValueKind.Number && prop.Value.TryGetDouble(out var d))
                    {
                        return d.ToString();
                    }
                    else if (valueKind == JsonValueKind.True || valueKind == JsonValueKind.False)
                    {
                        return prop.Value.GetBoolean().ToString();
                    }
                    else if (valueKind != JsonValueKind.Undefined && valueKind != JsonValueKind.Null)
                    {
                        var val = prop.Value.ToString();
                        if (!string.IsNullOrEmpty(val)) return val;
                    }
                }
                catch
                {
                    // ignore and try next name
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

            // Fix a common LLM failure mode where objects inside arrays are accidentally wrapped
            // with a leading quote, e.g.  ,"{"title":"..."}  (invalid JSON).
            // Remove the stray quote when it appears right before an object that follows '[' or ','.
            cleaned = Regex.Replace(
                cleaned,
                "(?<=\\[|,)\\s*\"(?=\\s*\\{)",
                string.Empty,
                RegexOptions.Multiline);

            // Remove a stray quote immediately after a closing brace/bracket when it is followed by
            // a comma or a closing bracket/brace (another symptom of the same mistake).
            cleaned = Regex.Replace(
                cleaned,
                "(?<=[\\}\\]])\\s*\"(?=\\s*(,|\\]|\\}))",
                string.Empty,
                RegexOptions.Multiline);

            int openObj = cleaned.Count(c => c == '{');
            int closeObj = cleaned.Count(c => c == '}');
            int openArr = cleaned.Count(c => c == '[');
            int closeArr = cleaned.Count(c => c == ']');

            var sb = new System.Text.StringBuilder(cleaned);
            // Close arrays before objects. In truncated replies the common missing tail is "]}".
            for (int i = 0; i < openArr - closeArr; i++) sb.Append(']');
            for (int i = 0; i < openObj - closeObj; i++) sb.Append('}');

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
                    .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.ToString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!.Trim())
                    .ToArray();

                return string.Join("; ", items);
            }

            if (goals.ValueKind == JsonValueKind.String)
            {
                return goals.GetString() ?? string.Empty;
            }

            return goals.ToString();
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

            // Ensure target date is usable and within 30-90 days.
            var today = DateTime.UtcNow.Date;
            if (!DateTime.TryParse(resp.TargetCompletionDate, out var parsedDate))
            {
                parsedDate = today.AddDays(45);
            }
            if (parsedDate < today.AddDays(30) || parsedDate > today.AddDays(90))
            {
                parsedDate = today.AddDays(45);
            }
            resp.TargetCompletionDate = parsedDate.ToString("yyyy-MM-dd");

            if (resp.Modules == null)
            {
                resp.Modules = new List<AiModuleDraft>();
            }

            if (!resp.Modules.Any())
            {
                resp.Modules.Add(new AiModuleDraft
                {
                    Title = "Getting started",
                    Description = "Getting started",
                    EstimatedHours = resp.TotalEstimatedHours > 0 ? resp.TotalEstimatedHours : 4,
                    SubModules = new List<AiSubModuleDraft>
                    {
                        new() { Title = "Overview", EstimatedHours = 2, Description = "Overview" },
                        new() { Title = "Practice", EstimatedHours = 2, Description = "Practice" }
                    }
                });
            }

            // Ensure a minimum of 4 modules for UI + downstream expectations.
            if (resp.Modules.Count < 4)
            {
                var topic = string.IsNullOrWhiteSpace(resp.SubjectArea) ? "Course" : resp.SubjectArea;
                while (resp.Modules.Count < 4)
                {
                    var idx = resp.Modules.Count + 1;
                    resp.Modules.Add(new AiModuleDraft
                    {
                        Title = FallbackModuleTitle(idx, topic),
                        Description = string.Empty,
                        EstimatedHours = 2,
                        SubModules = new List<AiSubModuleDraft>
                        {
                            new() { Title = FallbackSubModuleTitle(1), EstimatedHours = 1, Description = string.Empty },
                            new() { Title = FallbackSubModuleTitle(2), EstimatedHours = 1, Description = string.Empty },
                        }
                    });
                }
            }

            foreach (var module in resp.Modules)
            {
                module.Title = string.IsNullOrWhiteSpace(module.Title) ? "Untitled" : module.Title.Trim();
                module.Title = StripSectionLabel(module.Title);
                if (string.IsNullOrWhiteSpace(module.Title)) module.Title = "Untitled";
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
                        Title = FallbackSubModuleTitle(1),
                        EstimatedHours = Math.Max(1, module.EstimatedHours / 2),
                        Description = ""
                    });
                }

                // Ensure each module has >= 2 sub-modules.
                while (module.SubModules.Count < 2)
                {
                    module.SubModules.Add(new AiSubModuleDraft
                    {
                        Title = FallbackSubModuleTitle(module.SubModules.Count + 1),
                        EstimatedHours = 1,
                        Description = string.Empty
                    });
                }

                foreach (var sub in module.SubModules)
                {
                    sub.Title = string.IsNullOrWhiteSpace(sub.Title) ? "Untitled" : sub.Title.Trim();
                    sub.Title = StripSectionLabel(sub.Title);
                    if (string.IsNullOrWhiteSpace(sub.Title)) sub.Title = "Untitled";
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

        private static string StripSectionLabel(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return string.Empty;

            var trimmed = title.Trim();

            // Remove leading "Module 2:" / "Submodule 2.1 -" / "Sub module 3." etc.
            // Keep safe: require either numbering or punctuation to avoid stripping legitimate titles like "Module Federation".
            var stripped = Regex.Replace(
                trimmed,
                @"^\s*(module|submodule|sub-module|sub module)\s*(\d+(?:\.\d+)*)?\s*[:\.\-–—\)\]]\s*",
                string.Empty,
                RegexOptions.IgnoreCase);

            stripped = Regex.Replace(
                stripped,
                @"^\s*(module|submodule|sub-module|sub module)\s*(\d+(?:\.\d+)*)\s+",
                string.Empty,
                RegexOptions.IgnoreCase);

            return string.IsNullOrWhiteSpace(stripped) ? trimmed : stripped.Trim();
        }

        private static string FallbackModuleTitle(int index, string topic)
        {
            topic = string.IsNullOrWhiteSpace(topic) ? "Course" : topic.Trim();

            return index switch
            {
                1 => $"Foundations of {topic}",
                2 => $"{topic} setup and tooling",
                3 => $"Core concepts in {topic}",
                4 => $"Hands-on practice with {topic}",
                _ => $"{topic} project"
            };
        }

        private static string FallbackSubModuleTitle(int index)
        {
            return index switch
            {
                1 => "Overview",
                2 => "Hands-on",
                3 => "Review",
                4 => "Mini project",
                _ => "Practice"
            };
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
