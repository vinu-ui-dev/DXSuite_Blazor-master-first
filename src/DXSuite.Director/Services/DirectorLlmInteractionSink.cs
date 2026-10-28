using System.Security.Cryptography;
using System.Text;
using DXSuite.Connectors.Services.Llm;
using DXSuite.Director.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DXSuite.Director.Services;

/// <summary>
/// Director's implementation of <see cref="ILlmInteractionSink"/>. Persists each LLM call to
/// tblLlmInteraction (prompt/response/usage) for audit and tuning, returning the new id so callers
/// (e.g. Sentinel's RemediationLog) can link to it.
/// </summary>
public class DirectorLlmInteractionSink : ILlmInteractionSink
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DirectorLlmInteractionSink> _logger;

    public DirectorLlmInteractionSink(IServiceScopeFactory scopeFactory, ILogger<DirectorLlmInteractionSink> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<long?> LogAsync(LlmInteractionRecord record, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var entity = new TblLlmInteraction
            {
                Provider = Trim(record.Provider, 50),
                Model = Trim(record.Model, 100),
                PromptHash = HashPrompt(record.RequestText),
                PromptTokens = record.PromptTokens,
                CompletionTokens = record.CompletionTokens,
                RequestJson = record.RequestText,
                ResponseJson = record.ResponseText,
                LatencyMs = record.LatencyMs,
                Success = record.Success,
                ErrorMessage = Trim(record.ErrorMessage, 500),
                Purpose = Trim(record.Purpose, 50),
                CreateDate = DateTime.UtcNow
            };

            db.TblLlmInteractions.Add(entity);
            await db.SaveChangesAsync(cancellationToken);
            return entity.InteractionId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write tblLlmInteraction");
            return null;
        }
    }

    private static string HashPrompt(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Trim(string? value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : (value.Length <= max ? value : value[..max]);
}
