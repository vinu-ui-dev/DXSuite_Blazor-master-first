using DXSuite.Director.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Controllers;

[Route("api/cupps/messages")]
[ApiController]
[AllowAnonymous]
public sealed class CuppsMessagesController : ControllerBase
{
    private readonly IDirectorDBContext _db;

    public CuppsMessagesController(IDirectorDBContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Returns canned messages for one airline. Message ownership is matched by airline id and (when present)
    /// the airline's user group id against <c>tblMessage.OwningUserGroupId</c>. Message texts come from
    /// <c>tblMessageText</c>.
    /// </summary>
    [HttpGet("approved")]
    public async Task<IActionResult> GetApproved([FromQuery] int airlineId, [FromQuery] string languageCode = "en", CancellationToken ct = default)
    {
        if (airlineId <= 0)
            return BadRequest(new { message = "airlineId must be a positive integer." });

        var airline = await _db.TblAirlineAccounts
            .AsNoTracking()
            .Where(a => a.AirlineId == airlineId && a.RetiredUtc == null)
            .Select(a => new { a.AirlineId, a.UserGroupId })
            .FirstOrDefaultAsync(ct);

        if (airline is null)
            return NotFound(new { message = $"No active airline account found for airlineId {airlineId}." });

        var ownershipIds = new HashSet<int> { airline.AirlineId };
        if (airline.UserGroupId is int userGroupId)
            ownershipIds.Add(userGroupId);

        var messages = await _db.TblMessages
            .AsNoTracking()
            .Where(m => m.IsCanned)
            .Where(m => !m.IsDeleted)
            .Where(m => m.OwningUserGroupId.HasValue && ownershipIds.Contains(m.OwningUserGroupId.Value))
            .OrderBy(m => m.Name)
            .Select(m => new
            {
                m.MessageId,
                m.Name,
                Texts = m.Texts
                    .OrderBy(t => t.LanguageCode)
                    .Select(t => new CuppsMessageTextDto(t.LanguageCode, t.Body))
                    .ToList()
            })
            .ToListAsync(ct);

        var result = messages
            .Select(m => new CuppsApprovedMessageDto(
                m.MessageId,
                m.Name,
                PickBody(m.Texts, languageCode),
                m.Texts))
            .ToList();

        return Ok(new CuppsApprovedMessagesResponse(airline.AirlineId, languageCode, result));
    }

    private static string? PickBody(IReadOnlyList<CuppsMessageTextDto> texts, string languageCode)
    {
        if (texts.Count == 0) return null;

        var exact = texts.FirstOrDefault(t => string.Equals(t.LanguageCode, languageCode, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact.Body;

        var prefix = texts.FirstOrDefault(t => t.LanguageCode.StartsWith(languageCode + "-", StringComparison.OrdinalIgnoreCase));
        if (prefix is not null) return prefix.Body;

        var english = texts.FirstOrDefault(t => t.LanguageCode.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        if (english is not null) return english.Body;

        return texts[0].Body;
    }
}

public sealed record CuppsApprovedMessagesResponse(
    int AirlineId,
    string LanguageCode,
    IReadOnlyList<CuppsApprovedMessageDto> Messages);

public sealed record CuppsApprovedMessageDto(
    int MessageId,
    string Name,
    string? Body,
    IReadOnlyList<CuppsMessageTextDto> Texts);

public sealed record CuppsMessageTextDto(string LanguageCode, string Body);
