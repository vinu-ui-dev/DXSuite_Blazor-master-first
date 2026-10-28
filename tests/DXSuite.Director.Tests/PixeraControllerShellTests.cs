using DXSuite.Director.Controllers;

namespace DXSuite.Director.Tests;

// The layer/counter shells are cached by players (HTTP cache + localStorage), so a shell
// served under one URL prefix must self-reference only that prefix: a /videowall page
// leaking a /pixera URL (or vice versa) would poison the player's cached state.
public class PixeraControllerShellTests
{
    private const string LayersJson = "[\"background\",\"mask\",\"counter\",\"logo\"]";

    [Fact]
    public void LayerShell_CanonicalPrefix_ContainsNoLegacyPaths()
    {
        var html = PixeraController.BuildLayerHtml(
            LayersJson, 3214, 0, "/videowall", "/api/videowall");

        Assert.DoesNotContain("/pixera", html);
        Assert.Contains("'/videowall'", html);
        Assert.Contains("'/api/videowall'", html);
    }

    [Fact]
    public void LayerShell_LegacyPrefix_ContainsNoCanonicalPaths()
    {
        var html = PixeraController.BuildLayerHtml(
            LayersJson, 3214, 0, "/pixera", "/api/pixera");

        Assert.DoesNotContain("/videowall", html);
        Assert.Contains("'/pixera'", html);
        Assert.Contains("'/api/pixera'", html);
    }

    [Fact]
    public void CounterShell_CanonicalPrefix_ContainsNoLegacyPaths()
    {
        var html = PixeraController.BuildCounterLayerShellHtml(3214, 0, "/videowall", "/api/videowall");

        Assert.DoesNotContain("/pixera", html);
        Assert.Contains("'/api/videowall'", html);
    }

    [Fact]
    public void CounterShell_LegacyPrefix_ContainsNoCanonicalPaths()
    {
        var html = PixeraController.BuildCounterLayerShellHtml(3214, 0, "/pixera", "/api/pixera");

        Assert.DoesNotContain("/videowall", html);
    }

    [Fact]
    public void LayerShell_ThreadsMonitorId_IntoPollingConstants()
    {
        var tagged = PixeraController.BuildLayerHtml(
            LayersJson, 3214, 0, "/videowall", "/api/videowall", monitorId: "MOXIE-01-OUT2");
        var untagged = PixeraController.BuildLayerHtml(
            LayersJson, 3214, 0, "/videowall", "/api/videowall");

        Assert.Contains("var MONITOR   = 'MOXIE-01-OUT2';", tagged);
        Assert.Contains("var MONITOR   = '';", untagged);
    }

    [Fact]
    public void Shells_KeepLegacyLocalStorageKeys_RegardlessOfPrefix()
    {
        // Renaming the cache keys would blank every deployed output for one render
        // cycle; both prefixes must keep reading/writing the same px-* entries.
        var layer   = PixeraController.BuildLayerHtml(LayersJson, 3214, 0, "/videowall", "/api/videowall");
        var counter = PixeraController.BuildCounterLayerShellHtml(3214, 0, "/videowall", "/api/videowall");

        Assert.Contains("'px-l-'", layer);
        Assert.Contains("'px-c-'", counter);
    }

    [Fact]
    public void Shells_RegisterServiceWorker_ViaServingPrefix()
    {
        // Registration must go through PAGE_BASE so a /pixera-served page never
        // registers (and scopes) a /videowall worker or vice versa.
        var layer   = PixeraController.BuildLayerHtml(LayersJson, 3214, 0, "/videowall", "/api/videowall");
        var counter = PixeraController.BuildCounterLayerShellHtml(3214, 0, "/videowall", "/api/videowall");

        Assert.Contains("serviceWorker.register(PAGE_BASE + '/sw.js')", layer);
        Assert.Contains("serviceWorker.register(PAGE_BASE + '/sw.js')", counter);
        Assert.Contains("'dx-precache'", layer);
        Assert.Contains("'dx-precache'", counter);
    }

    [Fact]
    public void ServiceWorker_IsPrefixNeutral_AndNeverCachesServertime()
    {
        // One script serves both /videowall/sw.js and /pixera/sw.js, so it must not
        // hardcode either prefix. Servertime feeds the video phase-lock clock; a
        // cached copy would silently desync outputs, so the worker must bypass it.
        Assert.DoesNotContain("/videowall", PixeraController.ServiceWorkerJs);
        Assert.DoesNotContain("/pixera", PixeraController.ServiceWorkerJs);
        Assert.Contains("servertime", PixeraController.ServiceWorkerJs);
        Assert.Contains("addEventListener('fetch'", PixeraController.ServiceWorkerJs);
        Assert.Contains("dx-precache", PixeraController.ServiceWorkerJs);
    }

    [Fact]
    public void ServiceWorker_CachesItsOwnClientPage()
    {
        // The install-time navigation completes before the worker controls the
        // page, so without this the shell HTML is absent from the cache until
        // some later reload — and a player that boots once then reboots into an
        // outage still gets a browser error page.
        Assert.Contains("function ensurePageCached", PixeraController.ServiceWorkerJs);
        Assert.Contains("ensurePageCached(pageKey)", PixeraController.ServiceWorkerJs);
    }
}
