using System.Text.Json;

namespace Learnit.Server.Services
{
    public record AiMessage(string Role, string Content);

    public interface IAiProvider
    {
        Task<string> GenerateAsync(string systemPrompt, string userPrompt, IEnumerable<AiMessage>? history = null, CancellationToken cancellationToken = default);
    }
}
