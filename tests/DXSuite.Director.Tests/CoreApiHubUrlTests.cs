using System.Net;
using System.Text;
using DXSuite.Connectors.Components.CoreApi;
using DXSuite.Connectors.Services.CoreApi;
using DXSuite.Shared.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace DXSuite.Director.Tests;

/// <summary>
/// B-11: CoreAPI's hub is reached at a configured URL, which an already configured tenant sets or changes on the
/// CoreAPI connection tab without entering its key again.
/// </summary>
public class CoreApiHubUrlTests
{
    private const string Server = "http://coreapi.test";

    [Fact]
    public async Task A_configured_tenant_changes_its_hub_address_without_entering_the_key_again()
    {
        var store = new CoreApiClientTests.FakeSettingsStore(Server) { HubUrl = "http://old.test/pulse" };
        using var page = await RenderAsync(store, PermissionKeys.ConfigTabCoreApiConnection);
        Assert.Contains("value=\"http://old.test/pulse\"", page.Html);

        await page.ChangeAsync("coreapi-hub-url", "http://hub.test/pulse/");
        await page.ClickAsync("coreapi-hub-save");

        Assert.Equal("http://hub.test/pulse", store.HubUrl);
        Assert.Equal("director-key", store.DirectorKey);
        Assert.Equal([Server], store.Servers);
        Assert.Equal(0, store.KeySaves);
    }

    [Fact]
    public async Task A_configured_tenant_without_a_hub_sets_one_and_can_clear_it_again()
    {
        var store = new CoreApiClientTests.FakeSettingsStore(Server);
        using var page = await RenderAsync(store, PermissionKeys.ConfigTabCoreApiConnection);

        await page.ChangeAsync("coreapi-hub-url", "http://hub.test/pulse");
        await page.ClickAsync("coreapi-hub-save");
        Assert.Equal("http://hub.test/pulse", store.HubUrl);

        await page.ChangeAsync("coreapi-hub-url", "");
        await page.ClickAsync("coreapi-hub-save");
        Assert.Null(store.HubUrl);
    }

    [Fact]
    public async Task An_address_that_is_not_http_is_refused_and_the_stored_one_kept()
    {
        var store = new CoreApiClientTests.FakeSettingsStore(Server) { HubUrl = "http://old.test/pulse" };
        using var page = await RenderAsync(store, PermissionKeys.ConfigTabCoreApiConnection);

        await page.ChangeAsync("coreapi-hub-url", "hub.test/pulse");
        await page.ClickAsync("coreapi-hub-save");

        Assert.Equal("http://old.test/pulse", store.HubUrl);
        Assert.Contains("Invalid Hub URL", page.Html);
    }

    [Fact]
    public async Task Without_the_connection_tab_key_there_is_no_hub_address_to_change()
    {
        var store = new CoreApiClientTests.FakeSettingsStore(Server);
        using var page = await RenderAsync(store);

        Assert.DoesNotContain("coreapi-hub-url", page.Html);
    }

    private static Task<InteractiveRenderer> RenderAsync(CoreApiClientTests.FakeSettingsStore store, params string[] keys)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<ISessionState>(new KeySession(keys));
        services.AddSingleton<ICoreApiSettingsStore>(store);
        services.AddCoreApiClient().ConfigurePrimaryHttpMessageHandler(() => new FakeCoreApi());
        return InteractiveRenderer.RenderAsync<CoreAPIKey>(new Dictionary<string, object?>(), services.BuildServiceProvider());
    }

    /// <summary>Answers the key page's reads.</summary>
    private sealed class FakeCoreApi : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var json = request.RequestUri!.AbsolutePath.TrimStart('/') switch
            {
                "ApiKey/ApiKeyID" => "\"key-1\"",
                "ApiKey/key-1" => """{ "ApiKeyID": "key-1", "Name": "Director" }""",
                "ApiKey/all" => """[ { "ApiKeyID": "key-1", "Name": "Director" } ]""",
                _ => "[]",
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
