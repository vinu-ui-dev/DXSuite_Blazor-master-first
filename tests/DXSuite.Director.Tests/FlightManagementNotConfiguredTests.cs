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
/// Opening the Flight Data Editor before the CoreAPI connection is set up (FDE-01, CAPI-10): every user stays on the page
/// and is pointed at Settings' CoreAPI Connection tab, which is open to everyone.
/// </summary>
public class FlightManagementNotConfiguredTests
{
    private const string NotSetUp = "The CoreAPI connection is not set up.";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Every_user_stays_on_the_page_and_is_pointed_at_the_CoreAPI_Connection_tab(bool admin)
    {
        var session = admin
            ? new KeySession((int)Roles.Role.Admin)
            : new KeySession(PermissionKeys.ContentUpload);
        var navigation = new RecordingNavigation();

        using var page = await RenderAsync(session, navigation);

        Assert.Empty(navigation.Visited);
        Assert.Contains(NotSetUp, page.Html);
        Assert.Contains("/settings/coreapi-connection", page.Html);
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
