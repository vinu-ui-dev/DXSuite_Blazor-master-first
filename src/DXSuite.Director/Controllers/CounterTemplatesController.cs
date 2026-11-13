using DXSuite.Director.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Controllers;

[Route("api/counter-templates")]
[ApiController]
[AllowAnonymous]
public sealed class CounterTemplatesController : ControllerBase
{
    private readonly IDirectorDBContext _db;

    public CounterTemplatesController(IDirectorDBContext db)
    {
        _db = db;
    }

    [HttpGet("page-options")]
    public async Task<IActionResult> GetPageOptions([FromQuery] int airlineId, CancellationToken ct)
    {
        if (airlineId <= 0)
            return BadRequest(new { message = "airlineId must be a positive integer." });

        var airline = await _db.TblAirlineAccounts
            .AsNoTracking()
            .Where(a => a.AirlineId == airlineId && a.RetiredUtc == null)
            .Select(a => new CounterTemplateAirlineDto(
                a.AirlineId,
                a.NameEnglish,
                a.NameFrench,
                a.IataCode,
                a.IcaoCode,
                a.CuppsName,
                a.IsCanada,
                a.IsUnitedStates,
                a.IsInternational,
                a.StandardBackgroundColour,
                a.StandardTextColour,
                a.DcsProviderId,
                a.AllianceId,
                a.ShowAirportStandardTemplates,
                a.DefaultCounterTemplateId,
                a.PickerTab,
                a.UserGroupId,
                a.FullTemplateLibraryId,
                a.HalfTemplateLibraryId))
            .FirstOrDefaultAsync(ct);

        if (airline is null)
            return NotFound(new { message = $"No active airline account found for airlineId {airlineId}." });

        const byte StandardKind = 0;
        const byte CustomKind = 1;

        var todayUtc = DateTime.UtcNow.Date;
        var includeAirportStandard = airline.ShowAirportStandardTemplates;

        var templates = await _db.TblCounterTemplates
            .AsNoTracking()
            .Where(t => t.RetiredUtc == null)
            .Where(t => t.AirlineId == airlineId || (includeAirportStandard && t.AirlineId == 0))
            .Where(t => t.EndDate == null || t.EndDate >= todayUtc)
            .Select(t => new
            {
                t.CounterTemplateId,
                t.Name,
                t.AirlineId,
                t.Kind,
                t.ImageLayout,
                t.StartDate,
                t.EndDate,
                t.RetiredUtc,
                Outcome = t.Outcome == null
                    ? null
                    : new CounterTemplateOutcomeDto(t.Outcome.FirstApprovalRequestId, t.Outcome.SentUtc),
                Texts = t.Texts
                    .Select(x => new CounterTemplateTextDto(x.LanguageCode, x.Body))
                    .ToList(),
                Assets = t.Assets
                    .Select(x => new CounterTemplateAssetDto(x.SlotKey, x.AssetId))
                    .ToList(),
                Replacements = t.Replacements
                    .Select(x => new CounterTemplateAssetDto(x.SlotKey, x.AssetId))
                    .ToList()
            })
            .OrderBy(t => t.Kind)
            .ThenBy(t => t.Name)
            .ToListAsync(ct);

        var mapped = templates
            .Select(t => new CounterTemplatePageOptionDto(
                t.CounterTemplateId,
                t.Name,
                t.AirlineId,
                t.AirlineId == 0 ? "airport" : "airline",
                t.Kind == StandardKind ? "Standard" : t.Kind == CustomKind ? "Custom" : t.Kind.ToString(),
                t.ImageLayout,
                t.StartDate,
                t.EndDate,
                t.EndDate.HasValue && t.EndDate.Value.Date == todayUtc,
                TextFor(t.Texts, "en"),
                TextFor(t.Texts, "fr"),
                t.Assets,
                t.Replacements,
                t.Outcome))
            .ToList();

        var standard = mapped
            .Where(t => string.Equals(t.Kind, "standard", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var custom = mapped
            .Where(t => string.Equals(t.Kind, "custom", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Ok(new CounterTemplatePageOptionsResponse(airline, standard, custom));
    }

    private static string? TextFor(IEnumerable<CounterTemplateTextDto> texts, string langPrefix)
        => texts.FirstOrDefault(t => t.LanguageCode.StartsWith(langPrefix, StringComparison.OrdinalIgnoreCase))?.Body;
}

public sealed record CounterTemplatePageOptionsResponse(
    CounterTemplateAirlineDto Airline,
    IReadOnlyList<CounterTemplatePageOptionDto> StandardTemplates,
    IReadOnlyList<CounterTemplatePageOptionDto> CustomTemplates);

public sealed record CounterTemplateAirlineDto(
    int AirlineId,
    string? NameEnglish,
    string? NameFrench,
    string? IataCode,
    string? IcaoCode,
    string? CuppsName,
    bool IsCanada,
    bool IsUnitedStates,
    bool IsInternational,
    string? StandardBackgroundColour,
    byte? StandardTextColour,
    int? DcsProviderId,
    int? AllianceId,
    bool ShowAirportStandardTemplates,
    int? DefaultCounterTemplateId,
    byte? PickerTab,
    int? UserGroupId,
    int? FullTemplateLibraryId,
    int? HalfTemplateLibraryId);

public sealed record CounterTemplatePageOptionDto(
    int CounterTemplateId,
    string Name,
    int AirlineId,
    string Scope,
    string Kind,
    byte? ImageLayout,
    DateTime StartDate,
    DateTime? EndDate,
    bool EndsToday,
    string? TextEn,
    string? TextFr,
    IReadOnlyList<CounterTemplateAssetDto> Assets,
    IReadOnlyList<CounterTemplateAssetDto> Replacements,
    CounterTemplateOutcomeDto? Outcome);

public sealed record CounterTemplateAssetDto(string SlotKey, int AssetId);

public sealed record CounterTemplateOutcomeDto(int? FirstApprovalRequestId, DateTime? SentUtc);

public sealed record CounterTemplateTextDto(string LanguageCode, string Body);
