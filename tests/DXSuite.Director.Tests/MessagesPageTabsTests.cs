using DXSuite.Director.Entities;
using DXSuite.Director.Models.CoreAPI;
using DXSuite.Director.Pages.Messages;
using DXSuite.Director.Services;
using DXSuite.Director.Services.Messaging;
using DXSuite.Shared.Audit;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.CustomerDeviations;
using DXSuite.Shared.Entities.Identity;
using DXSuite.Shared.Multitenancy;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static DXSuite.Director.Tests.MessageAccessDatabase;

namespace DXSuite.Director.Tests;

// Clause ids refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// The Messages page's Message Type tabs (MSG-18): each tab lists its own type's canned Messages however the user moves
/// between them, driven through <see cref="InteractiveRenderer"/> over a Director database on LocalDB.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MessagesPageTabsTests : IClassFixture<MessageAccessDatabase>, IAsyncLifetime
{
    private const int Irrops = 2;

    private readonly MessageAccessDatabase _db;

    public MessagesPageTabsTests(MessageAccessDatabase db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [LocalDbFact]
    public async Task Moving_from_one_Message_Type_tab_straight_to_another_shows_only_that_types_Messages()
    {
        var evacuate = await _db.AddMessageAsync(Emergency, name: "Evacuate the terminal");
        var delays = await _db.AddMessageAsync(Irrops, name: "Expect delays");
        var paging = await _db.AddMessageAsync(Paging, name: "Passenger Smith to Gate A1");
        int[] all = [evacuate, delays, paging];
        using var page = await RenderPageAsync();

        foreach (var (code, own) in new[] { ("EMERGENCY", evacuate), ("IRROPS", delays), ("VISUAL_PAGING", paging) })
        {
            await page.ChangeAsync("dx-tab-select", code);
            await WaitForAsync(() => page.Html.Contains($"id=\"mp-row-{own}\""), code);

            foreach (var other in all.Where(m => m != own))
                Assert.DoesNotContain($"id=\"mp-row-{other}\"", page.Html);
        }
    }

    [LocalDbFact]
    public async Task A_Message_Type_tab_handed_another_type_lists_that_types_Messages()
    {
        var evacuate = await _db.AddMessageAsync(Emergency, name: "Evacuate the terminal");
        var delays = await _db.AddMessageAsync(Irrops, name: "Expect delays");
        await using var db = _db.Director();
        var emergency = await db.TblMessageTypes.Include(t => t.Languages).SingleAsync(t => t.MessageTypeId == Emergency);
        var irrops = await db.TblMessageTypes.Include(t => t.Languages).SingleAsync(t => t.MessageTypeId == Irrops);
        using var page = await InteractiveRenderer.RenderAsync<MessagePage>(
            new Dictionary<string, object?> { ["MessageType"] = emergency, ["Languages"] = new List<TblLanguage>() }, Services());
        await WaitForAsync(() => page.Html.Contains($"id=\"mp-row-{evacuate}\""), "EMERGENCY");

        await page.InvokeAsync(() => page.Component.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?> { ["MessageType"] = irrops, ["Languages"] = new List<TblLanguage>() })));

        await WaitForAsync(() => page.Html.Contains($"id=\"mp-row-{delays}\""), "IRROPS");
        Assert.DoesNotContain($"id=\"mp-row-{evacuate}\"", page.Html);
    }

    private static async Task WaitForAsync(Func<bool> condition, string tab)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException($"The {tab} tab never listed its own Message.");
            await Task.Delay(20);
        }
    }

    private Task<InteractiveRenderer> RenderPageAsync() =>
        InteractiveRenderer.RenderAsync<Messages>(new Dictionary<string, object?>(), Services());

    /// <summary>The Messages page's services as GTAA runs it (D-02 on), signed in as an Admin.</summary>
    private IServiceProvider Services()
    {
        var d02 = Options.Create(new D02GtaaMessagingOptions { Enabled = true });
        var session = new KeySession((int)Roles.Role.Admin) { User = new TblUser { UserId = Admin, RoleId = (int)Roles.Role.Admin } };
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IDirectorDBContext>(_db.Director());
        services.AddSingleton(_db.Factory);
        services.AddSingleton(d02);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ISessionState>(session);
        services.AddScoped<IMessageLanguageCatalogue, MessageLanguageCatalogue>();
        services.AddScoped<IMessageSendAccess, MessageSendAccess>();
        services.AddScoped<MessageSending>();
        services.AddSingleton<IUserEventLog>(new RecordingEventLog(Admin));
        services.Configure<MessagingOptions>(o => o.DefaultTimeZone = "America/Toronto");
        services.AddSingleton<DisplayTimeZones>();
        services.AddScoped<ShownVersions>();
        services.AddScoped<DXSuite.Director.Services.ContentSource.PendingFileDeletes>();
        services.AddScoped<ActiveMessages>();
        services.AddSingleton<Microsoft.JSInterop.IJSRuntime>(new TorontoBrowser());
        // Listing Messages touches no asset, so the authoring service's asset services are never called.
        services.AddSingleton((IAssetIngestService)ThrowingProxy.For(typeof(IAssetIngestService)));
        services.AddSingleton((ITenantMediaPathResolver)ThrowingProxy.For(typeof(ITenantMediaPathResolver)));
        services.AddScoped<MessageAuthoring>();

        return services.BuildServiceProvider();
    }
}
