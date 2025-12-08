using System.Collections.Concurrent;
using Learnit.Server.Data;
using Learnit.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Learnit.Server.Services
{
    public class FriendService
    {
        private static readonly ConcurrentDictionary<int, List<FriendDto>> Store = new();
        private readonly AppDbContext _db;

        public FriendService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<List<FriendDto>> GetFriendsAsync(int userId, CancellationToken cancellationToken = default)
        {
            var list = Store.GetOrAdd(userId, _ => new List<FriendDto>());
            await RefreshStatsAsync(list, cancellationToken);
            return list;
        }

        public async Task<FriendDto> AddFriendAsync(int userId, string email, CancellationToken cancellationToken = default)
        {
            var targetUser = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
            if (targetUser == null)
                throw new InvalidOperationException("No Learnit user found with that email.");

            if (targetUser.Id == userId)
                throw new InvalidOperationException("You cannot add yourself as a friend.");

            var list = Store.GetOrAdd(userId, _ => new List<FriendDto>());
            var existing = list.FirstOrDefault(f => f.FriendUserId == targetUser.Id);
            if (existing != null)
            {
                await RefreshStatsAsync(list, cancellationToken);
                return existing;
            }

            var stats = await ComputeStatsAsync(targetUser.Id, cancellationToken);

            var friend = new FriendDto
            {
                Id = Guid.NewGuid().ToString(),
                DisplayName = string.IsNullOrWhiteSpace(targetUser.FullName) ? targetUser.Email : targetUser.FullName,
                Email = targetUser.Email,
                FriendUserId = targetUser.Id,
                CompletionRate = stats.CompletionRate,
                WeeklyHours = stats.WeeklyHours
            };

            list.Add(friend);
            return friend;
        }

        public bool RemoveFriend(int userId, string friendId)
        {
            if (!Store.TryGetValue(userId, out var list)) return false;
            var removed = list.RemoveAll(f => f.Id == friendId) > 0;
            return removed;
        }

        public async Task<List<FriendDto>> GetFriendsByIdsAsync(int userId, IEnumerable<string> ids, CancellationToken cancellationToken = default)
        {
            var idsSet = new HashSet<string>(ids ?? Enumerable.Empty<string>());
            if (idsSet.Count == 0) return new List<FriendDto>();

            var list = await GetFriendsAsync(userId, cancellationToken);
            return list.Where(f => idsSet.Contains(f.Id)).Take(2).ToList();
        }

        private async Task RefreshStatsAsync(List<FriendDto> friends, CancellationToken cancellationToken)
        {
            foreach (var friend in friends)
            {
                if (friend.FriendUserId <= 0) continue;
                var stats = await ComputeStatsAsync(friend.FriendUserId, cancellationToken);
                friend.CompletionRate = stats.CompletionRate;
                friend.WeeklyHours = stats.WeeklyHours;
            }
        }

        private async Task<(decimal CompletionRate, decimal WeeklyHours)> ComputeStatsAsync(int targetUserId, CancellationToken cancellationToken)
        {
            var modules = await _db.CourseModules
                .Where(m => _db.Courses.Any(c => c.Id == m.CourseId && c.UserId == targetUserId))
                .ToListAsync(cancellationToken);

            var totalModules = modules.Count;
            var completedModules = modules.Count(m => m.IsCompleted);
            var completionRate = totalModules == 0 ? 0 : Math.Round((decimal)completedModules / totalModules * 100, 1);

            var weekStart = DateTime.UtcNow.Date.AddDays(-6);
            var weekEnd = DateTime.UtcNow.Date.AddDays(1);

            var completedHours = await _db.StudySessions
                .Where(s => s.IsCompleted && s.StartTime.Date >= weekStart && s.StartTime.Date < weekEnd)
                .Join(_db.Courses.Where(c => c.UserId == targetUserId), s => s.CourseId, c => c.Id, (s, _) => s)
                .SumAsync(s => s.DurationHours, cancellationToken);

            return (completionRate, completedHours);
        }
    }
}
