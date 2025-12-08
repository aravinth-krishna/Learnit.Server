using Learnit.Server.Data;
using Learnit.Server.Models;
using Learnit.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

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
            var systemPrompt = "You draft concise course plans. Respond with strict JSON: {title, description, difficulty, priority, totalEstimatedHours, modules:[{title, description, estimatedHours, subModules:[{title, estimatedHours}]}]}";
            var reply = await _provider.GenerateAsync(systemPrompt, request.Prompt, null, cancellationToken);

            try
            {
                using var doc = JsonDocument.Parse(reply);
                var root = doc.RootElement;
                var resp = new AiCourseGenerateResponse
                {
                    Title = root.GetProperty("title").GetString() ?? "Generated Course",
                    Description = root.GetProperty("description").GetString() ?? "",
                    Difficulty = root.TryGetProperty("difficulty", out var diff) ? diff.GetString() ?? "Balanced" : "Balanced",
                    Priority = root.TryGetProperty("priority", out var pr) ? pr.GetString() ?? "Medium" : "Medium",
                    TotalEstimatedHours = root.TryGetProperty("totalEstimatedHours", out var hrs) ? hrs.GetInt32() : 10,
                    Modules = new List<AiModuleDraft>()
                };

                if (root.TryGetProperty("modules", out var modules) && modules.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in modules.EnumerateArray())
                    {
                        var moduleDraft = new AiModuleDraft
                        {
                            Title = m.GetProperty("title").GetString() ?? "Module",
                            Description = m.TryGetProperty("description", out var md) ? md.GetString() ?? "" : "",
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
                                    EstimatedHours = s.TryGetProperty("estimatedHours", out var sh) ? sh.GetInt32() : 1
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

                return Ok(resp);
            }
            catch
            {
                // Fallback simple draft if parsing fails
                var fallback = new AiCourseGenerateResponse
                {
                    Title = "Generated Course",
                    Description = "AI generated course plan.",
                    Difficulty = "Balanced",
                    Priority = "Medium",
                    TotalEstimatedHours = 10,
                    Modules = new List<AiModuleDraft>
                    {
                        new() { Title = "Foundations", EstimatedHours = 3 },
                        new() { Title = "Practice", EstimatedHours = 3 },
                        new() { Title = "Project", EstimatedHours = 4 }
                    }
                };
                return Ok(fallback);
            }
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
            var systemPrompt = "You are Learnit AI. Prioritize helping the current user; use the friend only as a benchmark. Deliver concise comparative analysis (user-first) plus next best actions for the user. Keep it friendly and actionable. Return markdown (lists encouraged).";
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
