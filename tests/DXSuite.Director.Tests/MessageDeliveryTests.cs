using System.Text;
using DXSuite.Director.Controllers;
using DXSuite.Director.Entities;
using DXSuite.Director.Services;
using DXSuite.Director.Services.ContentSource;
using DXSuite.Director.Services.Messaging;
using DXSuite.Director.Services.Reports;
using DXSuite.Shared.Audit;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.CustomerDeviations;
using DXSuite.Shared.Entities.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static DXSuite.Director.Tests.MessageAccessDatabase;

namespace DXSuite.Director.Tests;

// Clause ids refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// The delivery document (B-81, INK-04 to INK-08) built by <see cref="MessageDelivery"/> over a Director database on
/// LocalDB: every display, the three parts arbitration decides, the shown version each slot publishes, and Delivered to
/// signage. The route around it is covered in <see cref="MessagingDeliveryRouteTests"/>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MessageDeliveryTests : IClassFixture<MessageAccessDatabase>, IAsyncLifetime
{
    // Tuesday 20 Oct 2026, 10:00 in Toronto.
    private static readonly DateTime Now = new(2026, 10, 20, 14, 0, 0, DateTimeKind.Utc);
    private const int Irrops = 2;

    private readonly MessageAccessDatabase _db;

    public MessageDeliveryTests(MessageAccessDatabase db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // INK-04: every Director Display, ordered by id, each with three empty parts while nothing is live.
    [LocalDbFact]
    public async Task With_nothing_live_every_display_is_listed_by_id_with_three_empty_parts()
    {
        var document = await Delivery().BuildAsync(default);

        Assert.Equal(
            """{"schema":1,"displays":[""" +
            """{"displayId":101,"fullScreen":[],"overlay":[],"ticker":[]},{"displayId":102,"fullScreen":[],"overlay":[],"ticker":[]},""" +
            """{"displayId":103,"fullScreen":[],"overlay":[],"ticker":[]},{"displayId":301,"fullScreen":[],"overlay":[],"ticker":[]},""" +
            """{"displayId":302,"fullScreen":[],"overlay":[],"ticker":[]}]}""",
            Encoding.UTF8.GetString(document.Json));
    }

    // INK-04, INK-07, ZONE-06: an entry carries its shown version's texts and the Activation's frozen values, and appears
    // only on the displays of its frozen screen rows.
    [LocalDbFact]
    public async Task A_live_Activation_is_published_on_its_frozen_displays_with_its_shown_versions_texts()
    {
        var message = await _db.AddMessageAsync(Emergency, name: "Evacuate");
        var activation = await _db.AddActivationAsync(message, Now.AddMinutes(-5), Now.AddMinutes(25), [GateA1, CheckIn1], zoneId: GatesA);
        await SetSecondsAsync(activation, 8, 4);
        var version = await _db.AddShownVersionAsync(activation, 1, Now.AddMinutes(-5),
            new Dictionary<string, string> { ["eng"] = "Evacuate now", ["fra"] = "Évacuez" }, typeCode: "EMERGENCY", rank: 10);

        var document = await Delivery().BuildAsync(default);

        var entry = $$"""{"aslPath":null,"rank":10,"typeCode":"EMERGENCY","texts":{"eng":"Evacuate now","fra":"Évacuez"},"textColour":"#FFFFFF","backgroundColour":"#C8102E","showSeconds":8,"hideSeconds":4,"endUtc":"2026-10-20T14:25:00Z","shownVersionId":{{version}}}""";
        var json = Encoding.UTF8.GetString(document.Json);
        Assert.Contains($$"""{"displayId":101,"fullScreen":[{{entry}}],"overlay":[],"ticker":[]}""", json);
        Assert.Contains($$"""{"displayId":301,"fullScreen":[{{entry}}],"overlay":[],"ticker":[]}""", json);
        Assert.Contains("""{"displayId":102,"fullScreen":[],"overlay":[],"ticker":[]}""", json);
    }

    // INK-04, INK-10, INK-16: a Full screen entry names its ASL video by the asset's content source path, and null when the
    // Message has none. Another slot's asset is not the ASL video.
    [LocalDbFact]
    public async Task A_full_screen_entry_names_its_ASL_videos_content_source_path_or_null_without_one()
    {
        const string sharePath = @"\\media-share\dxsuite\MediaAssets\7\9\Evacuate-ASL.mp4";
        var withVideo = await _db.AddMessageAsync(Emergency, name: "Evacuate");
        var asl = await _db.AddMessageAssetAsync(withVideo, "ASL", "Evacuate-ASL.mp4", "/MediaAssets/7/9/Evacuate-ASL.mp4");
        await SetContentSourceUrlAsync(asl, sharePath);
        await LiveAsync(withVideo, [GateA1], Now.AddMinutes(-5), null);
        var withoutVideo = await _db.AddMessageAsync(Emergency, name: "Shelter");
        var poster = await _db.AddMessageAssetAsync(withoutVideo, "POSTER", "Shelter.png", "/MediaAssets/7/8/Shelter.png");
        await SetContentSourceUrlAsync(poster, @"\\media-share\dxsuite\MediaAssets\7\8\Shelter.png");
        await LiveAsync(withoutVideo, [GateA2], Now.AddMinutes(-5), null);

        var document = await Delivery().BuildAsync(default);

        Assert.Equal(sharePath, Assert.Single(FullScreen(document, GateA1)).AslPath);
        Assert.Null(Assert.Single(FullScreen(document, GateA2)).AslPath);
        Assert.Contains("""
            "aslPath":"\\\\media-share\\dxsuite\\MediaAssets\\7\\9\\Evacuate-ASL.mp4"
            """.Trim(), Encoding.UTF8.GetString(document.Json));
    }

    // INK-09, INK-10: deleting a Message's ASL video from its library empties aslPath, so the delete's own save moves the
    // delivery version; deleting an asset no Message names as its video leaves it alone.
    [LocalDbFact]
    public async Task Deleting_a_Messages_ASL_video_from_the_library_moves_the_delivery_version()
    {
        var message = await _db.AddMessageAsync(Emergency, name: "Evacuate");
        var video = await _db.AddMessageAssetAsync(message, "ASL", "Evacuate-ASL.mp4", "/MediaAssets/7/9/Evacuate-ASL.mp4");
        var poster = await _db.AddMessageAssetAsync(message, "POSTER", "Evacuate.png", "/MediaAssets/7/8/Evacuate.png");
        var library = new AssetSoftDeleteService(_db.Factory, new SilentInvalidator());
        var before = await DeliveryVersionProbe.ReadAsync(_db.Director);

        await library.SoftDeleteAsync(poster);
        Assert.Equal(before, await DeliveryVersionProbe.ReadAsync(_db.Director));

        await library.SoftDeleteAsync(video);
        Assert.NotEqual(before, await DeliveryVersionProbe.ReadAsync(_db.Director));
    }

    // B-83 "Done when": replacing the ASL video changes aslPath in the replacement's own save, with a new delivery version for
    // the doorbell to ring for, and the old file stays readable on the share for the grace period.
    [LocalDbFact]
    public async Task Replacing_an_ASL_video_on_the_share_changes_aslPath_at_once_and_keeps_the_old_file()
    {
        const int library = 7;
        using var mediaRoot = new TempMediaRoot();
        var share = mediaRoot.GetRoot();
        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["FilePaths:BaseUNC"] = share }).Build();
        var paths = new ContentSourcePaths(settings, null!, NullLogger<ContentSourcePaths>.Instance);
        var message = await _db.AddMessageAsync(Emergency, name: "Evacuate");
        var asl = await _db.AddMessageAssetAsync(message, "ASL", "Evacuate-ASL.mp4", "/MediaAssets/7/9/Evacuate-ASL.mp4");
        mediaRoot.Write("/MediaAssets/7/9/Evacuate-ASL.mp4", "old video");
        await LiveAsync(message, [GateA1], Now.AddMinutes(-5), null);
        await using (var db = _db.Director())
        {
            // A library location whose content source is the media share (A-31). Its parents are not read.
            await db.Database.ExecuteSqlRawAsync($"""
                ALTER TABLE tblLocation NOCHECK CONSTRAINT ALL;
                IF NOT EXISTS (SELECT 1 FROM tblLocation WHERE LocationId = {library})
                BEGIN
                    SET IDENTITY_INSERT tblLocation ON;
                    INSERT INTO tblLocation (LocationId, LocationTypeId, LocationGroupId, LocationName, SortOrder, EnableShuffle, EnableFade, EnableContentApproval)
                    VALUES ({library}, 1, 1, 'ASL videos', 9, 0, 1, 0);
                    SET IDENTITY_INSERT tblLocation OFF;
                END
                UPDATE tblAsset SET LocationId = {library}, AssetTypeId = 1 WHERE AssetId = {asl};
                """);
            var location = await db.TblLocations.SingleAsync(l => l.LocationId == library);
            location.ExtendedData = null;
            Assert.Null(await paths.ChangeModeAsync(db, location, ContentSourceModes.Unc, null, default));
            await db.SaveChangesAsync();
        }
        Assert.Equal(Path.Combine(share, "MediaAssets", "7", "9", "Evacuate-ASL.mp4"), Assert.Single(FullScreen(await Delivery().BuildAsync(default), GateA1)).AslPath);
        var before = await DeliveryVersionProbe.ReadAsync(_db.Director);

        await using (var db = _db.Director())
        {
            var overwrite = new ContentOverwriteService(db, null!, null!, null!, mediaRoot, new ReplacedFileGraceTests.SilentPublicationInvalidator(),
                new RecordingEventLog(Admin), NullLogger<ContentOverwriteService>.Instance, paths,
                new PendingFileDeletes(_db.Factory, mediaRoot, Options.Create(new ContentSourceOptions()),
                    new FixedClock(Now), NullLogger<PendingFileDeletes>.Instance));
            await using var content = new MemoryStream(Encoding.UTF8.GetBytes("new video"));
            Assert.True((await overwrite.OverwriteLibraryAssetAsync(asl, content, "Evacuate-ASL-v2.mp4", Admin)).Success);
        }

        var aslPath = Assert.Single(FullScreen(await Delivery().BuildAsync(default), GateA1)).AslPath!;
        Assert.EndsWith("_Evacuate-ASL-v2.mp4", aslPath);
        Assert.StartsWith(Path.Combine(share, "MediaAssets", "7", "9"), aslPath);
        Assert.True(File.Exists(aslPath));
        Assert.True(File.Exists(Path.Combine(share, "MediaAssets", "7", "9", "Evacuate-ASL.mp4")));
        Assert.NotEqual(before, await DeliveryVersionProbe.ReadAsync(_db.Director));
    }

    // MSG-17: live means not cancelled, inside its window on the display's own clock, and for a canned Message, Approved.
    [LocalDbFact]
    public async Task Only_Activations_live_on_the_displays_clock_and_still_Approved_are_published()
    {
        var gateChange = await _db.AddMessageAsync(Paging, name: "Gate change");
        var (_, showing) = await LiveAsync(gateChange, [GateA1], Now.AddMinutes(-10), null);
        await LiveAsync(gateChange, [GateA2], Now.AddMinutes(-10), null, cancelledUtc: Now.AddMinutes(-1));
        await LiveAsync(gateChange, [GateA2], Now.AddHours(-2), Now.AddMinutes(-1));
        await LiveAsync(gateChange, [GateA2], Now.AddMinutes(5), null);
        var retired = await _db.AddMessageAsync(Paging, MessageStatus.Retired, name: "Old paging");
        await LiveAsync(retired, [CheckIn1], Now.AddMinutes(-10), null);
        var pending = await _db.AddMessageAsync(Paging, MessageStatus.Pending, name: "Not yet approved");
        await LiveAsync(pending, [CheckIn2], Now.AddMinutes(-10), null);

        // Today 09:00 to 11:00 on each screen's clock: 10:00 in Toronto, 07:00 in Vancouver.
        await _db.AddDeviceAsync("dev-lounge", Lounge, "America/Vancouver");
        var timing = ActivationTiming.Resolve(new MessageSendTiming.Scheduled(new(2026, 10, 20, 9, 0, 0), new(2026, 10, 20, 11, 0, 0)), Now,
            [Toronto, TimeZoneInfo.FindSystemTimeZoneById("America/Vancouver")]);
        var scheduledMessage = await _db.AddMessageAsync(Irrops, name: "Scheduled");
        var (_, scheduled) = await LiveAsync(scheduledMessage, [GateA1, Lounge], timing.StartUtc, timing.EndUtc, timing: timing);

        var document = await Delivery().BuildAsync(default);

        // IRROPS outranks Visual Paging in the overlay, so the paging waits behind the scheduled send on Gate A1.
        Assert.Equal([("overlay", scheduled, (string?)"2026-10-20T15:00:00Z")], Published(document, GateA1));
        Assert.Empty(Published(document, GateA2));
        Assert.Empty(Published(document, Lounge));
        Assert.Empty(Published(document, CheckIn1));
        Assert.Empty(Published(document, CheckIn2));

        // On a Vancouver clock the scheduled send has not started, so the paging shows.
        await _db.AddDeviceAsync("dev-a1", GateA1, "America/Vancouver");
        Assert.Equal([("overlay", showing, (string?)null)], Published(await Delivery().BuildAsync(default), GateA1));
    }

    // INK-04, MSG-10: NewestOnly publishes the tie's newest; Rotate its 3 newest, oldest first, and a 4th waits.
    [LocalDbFact]
    public async Task A_part_carries_one_entry_under_NewestOnly_and_at_most_three_under_Rotate()
    {
        var versions = new List<int>();
        foreach (var minutesAgo in new[] { 40, 30, 20, 10 })
        {
            var message = await _db.AddMessageAsync(Irrops, name: $"IRROPS {minutesAgo}");
            versions.Add((await LiveAsync(message, [GateA1], Now.AddMinutes(-minutesAgo), null)).Version);
        }
        var paging = await _db.AddMessageAsync(Paging, name: "Paging");
        await LiveAsync(paging, [GateA1], Now.AddMinutes(-1), null);

        Assert.Equal([versions[3]], Published(await Delivery().BuildAsync(default), GateA1).Select(p => p.Version));
        var rotate = await Delivery(TieBehaviour.Rotate).BuildAsync(default);
        Assert.Equal(versions[1..], Published(rotate, GateA1).Select(p => p.Version));
        Assert.All(rotate.Body.Displays, d => Assert.True(d.FullScreen.Count <= 3 && d.Overlay.Count <= 3 && d.Ticker.Count <= 3));
    }

    // MSG-20, INK-12: under D-02 the built-in Counter type rides counter slots, never a Display Zone part. An Activation
    // with no shown version holds nothing recorded as approved, so it is not published.
    [LocalDbFact]
    public async Task The_built_in_Counter_type_under_D02_and_an_Activation_without_a_shown_version_are_not_published()
    {
        var counter = await _db.AddMessageAsync(Counter, name: "Counter");
        var (_, counterVersion) = await LiveAsync(counter, [GateA1], Now.AddMinutes(-10), null);
        var noVersion = await _db.AddMessageAsync(Paging, name: "No version");
        await _db.AddActivationAsync(noVersion, Now.AddMinutes(-10), null, [GateA2]);

        Assert.Empty(Published(await Delivery(gtaa: true).BuildAsync(default), GateA1));
        Assert.Equal([("ticker", counterVersion, (string?)null)], Published(await Delivery(gtaa: false).BuildAsync(default), GateA1));
        Assert.Empty(Published(await Delivery().BuildAsync(default), GateA2));
    }

    // INK-05: the same data gives the same bytes and ETag on every build; any change to the body gives a new ETag.
    [LocalDbFact]
    public async Task The_ETag_is_the_bodys_SHA_256_in_base64_and_changes_only_with_the_body()
    {
        var message = await _db.AddMessageAsync(Paging);
        await LiveAsync(message, [GateA1], Now.AddMinutes(-10), null);

        var first = await Delivery().BuildAsync(default);
        var again = await Delivery().BuildAsync(default);

        Assert.Equal(first.Json, again.Json);
        Assert.Equal(first.ETag, again.ETag);
        Assert.Equal($"\"{Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(first.Json))}\"", first.ETag);
        await LiveAsync(message, [GateA2], Now.AddMinutes(-5), null);
        Assert.NotEqual(first.ETag, (await Delivery().BuildAsync(default)).ETag);
    }

    // INK-06: Delivered to signage is stamped once, for the versions a served document carried, and never moved.
    [LocalDbFact]
    public async Task Marking_a_document_delivered_stamps_each_unserved_version_in_it_once()
    {
        var message = await _db.AddMessageAsync(Paging);
        var (_, served) = await LiveAsync(message, [GateA1], Now.AddMinutes(-10), null);
        var (_, waiting) = await LiveAsync(message, [GateA2], Now.AddMinutes(10), null);
        var earlier = Now.AddMinutes(-30);
        var (_, alreadyServed) = await LiveAsync(message, [CheckIn1], Now.AddMinutes(-40), null, firstServedUtc: earlier);

        var document = await Delivery().BuildAsync(default);
        Assert.Equal([served], document.UndeliveredVersionIds);
        await Delivery().MarkDeliveredAsync(document, default);
        await Delivery(clockUtc: Now.AddMinutes(1)).MarkDeliveredAsync(document, default);

        Assert.Equal(((DateTime?)Now, (DateTime?)null, (DateTime?)earlier),
            (await FirstServedAsync(served), await FirstServedAsync(waiting), await FirstServedAsync(alreadyServed)));
        Assert.Empty((await Delivery().BuildAsync(default)).UndeliveredVersionIds);
    }

    // MSG-19, INK-07 with B-38: a send by key appears on the displays of its frozen screen rows, and only there.
    [LocalDbFact]
    public async Task A_send_by_key_appears_on_the_displays_of_its_frozen_screen_rows()
    {
        var message = await _db.AddMessageAsync(Paging, name: "Boarding moved");

        await ApiSendAsync(message);
        // A later Display Zone edit never changes a sent Activation (ZONE-06).
        await _db.SetZoneDisplaysAsync(GatesA, GateA1, Lounge);

        var document = await Delivery().BuildAsync(default);
        Assert.Equal([GateA1, GateA2], document.Body.Displays.Where(d => d.Overlay.Count > 0).Select(d => d.DisplayId));
        Assert.Equal("Boarding moved", Assert.Single(Entries(document, GateA2)).Texts["eng"]);
    }

    // MSG-05, INK-07: a one-off skips approval, so it is published from its send.
    [LocalDbFact]
    public async Task A_one_off_appears_at_send()
    {
        await _db.SetLanguagesAsync(Paging, "eng");
        await _db.AddRuleAsync(Paging, GatesA, canFreeText: true, userId: Ana);
        var d02 = Options.Create(new D02GtaaMessagingOptions());
        var sending = new MessageSending(_db.Factory, new MessageSendAccess(_db.Factory), new MessageLanguageCatalogue(d02),
            new KeySession(PermissionKeys.PageMessages) { User = new TblUser { UserId = Ana, RoleId = 3 } }, d02, new FixedClock(Now),
            TimeZones(), NoAssetVersions(), new MessagingApiTests.RecordingLog(), NullLogger<MessageSending>.Instance);

        var result = await sending.SendAsync(new MessageSendRequest
        {
            MessageTypeId = Paging, Target = MessageSendTarget.DisplayZones(GatesA), When = new MessageSendTiming.SendNow(15),
            OneOffTexts = new Dictionary<string, string> { ["eng"] = "Gate B4 is now open" },
        }, null, default);

        Assert.Equal(MessageSendOutcome.Sent, result.Outcome);
        var entry = Assert.Single(Entries(await Delivery().BuildAsync(default), GateA1));
        Assert.Equal(("Gate B4 is now open", (string?)"2026-10-20T14:15:00Z"), (entry.Texts["eng"], entry.EndUtc));
    }

    // AUD-06, AUD-18 with B-56: a shownVersionId read from the route finds its Activation in the Message Playback Log,
    // which shows the version's Sent and, once served, its Delivered to signage.
    [LocalDbFact]
    public async Task A_shown_version_id_from_the_route_opens_its_Activation_in_the_Message_Playback_Log()
    {
        var message = await _db.AddMessageAsync(Paging, name: "Boarding moved");
        var sent = await ApiSendAsync(message);
        var document = await Delivery().BuildAsync(default);
        var versionId = Assert.Single(Entries(document, GateA1)).ShownVersionId;
        var query = new MessagePlaybackLogQuery { StartUtc = Now.AddDays(-1), EndUtc = Now, Find = versionId };

        var before = Assert.Single((await PlaybackLog().QueryAsync(query)).Rows);
        Assert.Equal(sent, before.ActivationId);
        var shown = before.Versions.Single(v => v.VersionId == versionId);
        Assert.Equal((Now, (DateTime?)null), (shown.SentUtc, shown.FirstServedUtc));

        await Delivery().MarkDeliveredAsync(document, default);

        var after = Assert.Single((await PlaybackLog().QueryAsync(query)).Rows);
        Assert.Equal((DateTime?)Now, after.Versions.Single(v => v.VersionId == versionId).FirstServedUtc);
    }

    private static readonly TimeZoneInfo Toronto = TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");

    /// <summary>A live Activation as a send writes it: frozen rows and shown version 1.</summary>
    private async Task<(int Activation, int Version)> LiveAsync(int messageId, int[] displays, DateTime startUtc, DateTime? endUtc,
        DateTime? cancelledUtc = null, ResolvedTiming? timing = null, DateTime? firstServedUtc = null)
    {
        var activation = await _db.AddActivationAsync(messageId, startUtc, endUtc, displays, cancelledUtc: cancelledUtc, timing: timing);
        var version = await _db.AddShownVersionAsync(activation, 1, startUtc, new Dictionary<string, string> { ["eng"] = $"Message {messageId}" },
            firstServedUtc: firstServedUtc);
        return (activation, version);
    }

    private static List<(string Part, int Version, string? EndUtc)> Published(DeliveryDocument document, int displayId)
    {
        var display = document.Body.Displays.Single(d => d.DisplayId == displayId);
        return display.FullScreen.Select(e => ("fullScreen", e.ShownVersionId, e.EndUtc))
            .Concat(display.Overlay.Select(e => ("overlay", e.ShownVersionId, e.EndUtc)))
            .Concat(display.Ticker.Select(e => ("ticker", e.ShownVersionId, e.EndUtc)))
            .ToList();
    }

    private static List<DeliveryEntry> Entries(DeliveryDocument document, int displayId)
    {
        var display = document.Body.Displays.Single(d => d.DisplayId == displayId);
        return [.. display.FullScreen, .. display.Overlay, .. display.Ticker];
    }

    private static IReadOnlyList<DeliveryFullScreenEntry> FullScreen(DeliveryDocument document, int displayId) =>
        document.Body.Displays.Single(d => d.DisplayId == displayId).FullScreen;

    private async Task SetContentSourceUrlAsync(int assetId, string url)
    {
        await using var db = _db.Director();
        await db.TblAssets.Where(a => a.AssetId == assetId).ExecuteUpdateAsync(s => s.SetProperty(a => a.ContentSourceUrl, url));
    }

    private async Task<DateTime?> FirstServedAsync(int versionId)
    {
        await using var db = _db.Director();
        return await db.TblMessageActivationVersions.Where(v => v.MessageActivationVersionId == versionId).Select(v => v.FirstServedUtc).SingleAsync();
    }

    /// <summary>Sends the canned Message to Gates A by API key, through the API trigger's own action.</summary>
    private async Task<int> ApiSendAsync(int messageId)
    {
        var d02 = Options.Create(new D02GtaaMessagingOptions());
        var sending = new MessageSending(_db.Factory, new MessageSendAccess(_db.Factory), new MessageLanguageCatalogue(d02), new KeySession(), d02,
            new FixedClock(Now), TimeZones(), NoAssetVersions(), new MessagingApiTests.RecordingLog(), NullLogger<MessageSending>.Instance);
        var http = new DefaultHttpContext();
        http.Items["ApiKeyPermissions"] = ApiKeyScopes.MessagingSend;
        var controller = new MessagingApiController(sending) { ControllerContext = new ControllerContext { HttpContext = http } };

        var result = await controller.Send(new MessageApiSendRequest { MessageId = messageId, ZoneIds = [GatesA] }, default);

        return Assert.IsType<MessageApiSendResponse>(Assert.IsType<ObjectResult>(result).Value).ActivationId;
    }

    private IMessagePlaybackLogReport PlaybackLog()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(_db.Factory);
        services.AddSingleton<TimeProvider>(new FixedClock(Now));
        services.AddSingleton<ISessionState>(new KeySession(PermissionKeys.PageMessagePlaybackLog) { User = new TblUser { UserId = Admin, RoleId = 3 } });
        services.AddSingleton<IUserEventLog>(new RecordingEventLog(Admin));
        services.AddSingleton(TimeZones());
        services.AddMessagePlaybackLogReport();
        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IMessagePlaybackLogReport>();
    }

    private MessageDelivery Delivery(TieBehaviour tie = TieBehaviour.NewestOnly, bool gtaa = false, DateTime? clockUtc = null) =>
        new(_db.Factory, new MessageArbitration(Options.Create(new MessagingOptions { TieBehaviour = tie })), TimeZones(),
            Options.Create(new D02GtaaMessagingOptions { Enabled = gtaa }), new FixedClock(clockUtc ?? Now), NullLogger<MessageDelivery>.Instance);

    private async Task SetSecondsAsync(int activationId, int show, int hide)
    {
        await using var db = _db.Director();
        await db.TblMessageActivations.Where(a => a.MessageActivationId == activationId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.DisplaySecondsOverride, show).SetProperty(a => a.HideSecondsOverride, hide));
    }

    private static DisplayTimeZones TimeZones() =>
        new(Options.Create(new MessagingOptions { DefaultTimeZone = "America/Toronto" }), NullLogger<DisplayTimeZones>.Instance);

    private sealed class FixedClock(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc);
    }
}
