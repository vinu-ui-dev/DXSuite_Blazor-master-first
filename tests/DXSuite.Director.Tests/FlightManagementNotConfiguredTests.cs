using DXSuite.Aviation.Pages.FlightDataEditor;
using DXSuite.Aviation.Services.FlightDataEditor;
using DXSuite.Connectors.Services.CoreApi;
using DXSuite.Connectors.Services.CoreApi.Hub;
using DXSuite.Shared.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DXSuite.Director.Tests;

// Clause ids refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// Opening the Flight Data Editor before the CoreAPI connection is set up (FDE-01, CAPI-10): a user who may set it up is
/// taken to Settings' CoreAPI Connection tab; anyone else stays on the page and is told who can.
/// </summary>
public class FlightManagementNotConfiguredTests
{
    private const string NotSetUp =
        "The CoreAPI connection is not set up. Ask an administrator to configure it in Settings &gt; CoreAPI Connection.";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_user_who_may_set_up_the_connection_is_taken_to_the_CoreAPI_Connection_tab(bool admin)
    {
        var session = admin
            ? new KeySession((int)Roles.Role.Admin)
            : new KeySession(PermissionKeys.PageFlightDataEditor, PermissionKeys.ConfigTabCoreApiConnection);
        var navigation = new RecordingNavigation();

        using var page = await RenderAsync(session, navigation);

        Assert.Equal(["https://director.test/settings/coreapi-connection"], navigation.Visited);
        Assert.DoesNotContain(NotSetUp, page.Html);
    }

    [Fact]
    public async Task Anyone_else_stays_on_the_page_and_is_told_who_can_set_it_up()
    {
        var navigation = new RecordingNavigation();

        using var page = await RenderAsync(new KeySession(PermissionKeys.PageFlightDataEditor), navigation);

        Assert.Empty(navigation.Visited);
        Assert.Contains(NotSetUp, page.Html);
        Assert.DoesNotContain("Connecting to CoreAPI", page.Html);
    }

    // Nothing past the configuration check runs, so the feed, the live updates and the list clock throw if touched.
    private static Task<InteractiveRenderer> RenderAsync(KeySession session, RecordingNavigation navigation)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<ISessionState>(session);
        services.AddSingleton<NavigationManager>(navigation);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<ICoreApiSettingsStore>(new CoreApiClientTests.FakeSettingsStore { DirectorKey = null });
        services.AddSingleton((ICoreApiLiveFeed)ThrowingProxy.For(typeof(ICoreApiLiveFeed)));
        services.AddSingleton((ICoreApiFeedStateReader)ThrowingProxy.For(typeof(ICoreApiFeedStateReader)));
        services.AddSingleton((IFlightListClock)ThrowingProxy.For(typeof(IFlightListClock)));
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new FlightDataEditorOptions()));
        services.AddFlightEditAudit();
        services.AddCoreApiClient();
        return InteractiveRenderer.RenderAsync<FlightManagement>(new Dictionary<string, object?>(), services.BuildServiceProvider());
    }

    private sealed class RecordingNavigation : NavigationManager
    {
        public RecordingNavigation() => Initialize("https://director.test/", "https://director.test/");

        public List<string> Visited { get; } = new();

        protected override void NavigateToCore(string uri, NavigationOptions options) => Visited.Add(ToAbsoluteUri(uri).ToString());
    }
}
