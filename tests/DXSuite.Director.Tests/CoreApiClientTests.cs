using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Text;
using DXSuite.Connectors.Services.CoreApi;
using DXSuite.Shared.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace DXSuite.Director.Tests;

/// <summary>
/// The CoreAPI client as a page or the watcher sees it: CAPI-01, CAPI-02, CAPI-06 and CAPI-09, through the
/// client's public calls over a fake network. Servers are told apart by host name.
/// </summary>
public class CoreApiClientTests
{
    private const string A = "http://coreapi-a.test";
    private const string B = "http://coreapi-b.test";
    private const string C = "http://coreapi-c.test";

    [Fact]
    public async Task TriesTheServersInTheConfiguredOrderAndRemembersTheOneThatAnswered()
    {
        var network = new FakeNetwork();
        network.Refuse(A);
        network.Answer(B, """{"Status":"OK"}""");
        var store = new FakeSettingsStore(A, B, C);
        using var host = Host(network, store);

        var result = await host.Client().GetFeedStatusAsync();

        Assert.Equal(CoreApiOutcome.Succeeded, result.Outcome);
        Assert.Equal("OK", result.Value!.Status);
        Assert.Equal([A, B], network.Hosts());
        Assert.Equal(B, store.LastGoodServer);
    }

    [Fact]
    public async Task StartsFromTheLastGoodServerThenTriesTheRestInOrder()
    {
        var network = new FakeNetwork();
        network.Refuse(A);
        network.Refuse(B);
        network.Answer(C, """{"Status":"OK"}""");
        var store = new FakeSettingsStore(A, B, C) { LastGoodServer = B };
        using var host = Host(network, store);

        await host.Client().GetFeedStatusAsync();

        Assert.Equal([B, A, C], network.Hosts());
        Assert.Equal(C, store.LastGoodServer);
    }

    [Fact]
    public async Task DoesNotRewriteTheLastGoodServerWhileItKeepsAnswering()
    {
        var network = new FakeNetwork();
        network.Answer(B, """{"Status":"OK"}""");
        var store = new FakeSettingsStore(A, B) { LastGoodServer = B };
        using var host = Host(network, store);

        await host.Client().GetFeedStatusAsync();
        await host.Client().GetFeedStatusAsync();

        Assert.Equal([B, B], network.Hosts());
        Assert.Equal(0, store.LastGoodWrites);
    }

    [Fact]
    public async Task GivesEachServerFiveSecondsBeforeTryingTheNext()
    {
        var clock = new ManualClock();
        var network = new FakeNetwork();
        network.Hang(A);
        network.Answer(B, """{"Status":"OK"}""");
        var store = new FakeSettingsStore(A, B);
        using var host = Host(network, store, clock);

        var call = host.Client().GetFeedStatusAsync();
        await network.Hanging[A].Task.WaitAsync(TimeSpan.FromSeconds(10));

        clock.Advance(TimeSpan.FromSeconds(4.9));
        await Task.Delay(100);
        Assert.False(call.IsCompleted);
        Assert.Equal([A], network.Hosts());

        clock.Advance(TimeSpan.FromSeconds(0.1));
        var result = await call.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(CoreApiOutcome.Succeeded, result.Outcome);
        Assert.Equal([A, B], network.Hosts());
        Assert.Equal(TimeSpan.FromSeconds(5), CoreApiClient.PerServerTimeout);
    }

    [Fact]
    public async Task IsUnreachableOnlyAfterEveryServerFailed()
    {
        var clock = new ManualClock();
        var network = new FakeNetwork();
        network.Refuse(A);
        network.Hang(B);
        network.Refuse(C);
        var store = new FakeSettingsStore(A, B, C) { LastGoodServer = A };
        using var host = Host(network, store, clock);

        var call = host.Client().GetFeedStatusAsync();
        await network.Hanging[B].Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(call.IsCompleted);
        clock.Advance(CoreApiClient.PerServerTimeout);
        var result = await call.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(CoreApiOutcome.Unreachable, result.Outcome);
        Assert.Null(result.Value);
        Assert.Equal([A, B, C], network.Hosts());
        Assert.Equal(A, store.LastGoodServer);
    }

    [Fact]
    public async Task EachResultNamesTheServersTriedInTheOrderTheyWereTried()
    {
        var network = new FakeNetwork();
        network.Refuse(A);
        network.Refuse(B);
        network.Refuse(C);
        var store = new FakeSettingsStore(A, B, C) { LastGoodServer = B };
        using var host = Host(network, store);

        var unreachable = await host.Client().GetFeedStatusAsync();

        Assert.Equal(CoreApiOutcome.Unreachable, unreachable.Outcome);
        Assert.Equal([B, A, C], unreachable.ServersTried);

        network.Answer(A, """{"Status":"OK"}""");
        var reached = await host.Client().GetFeedStatusAsync();

        Assert.Equal([B, A], reached.ServersTried);
    }

    [Fact]
    public async Task AnErrorAnswerIsReachableAndDoesNotMoveToTheNextServer()
    {
        var network = new FakeNetwork();
        network.Answer(A, "Flight not found", HttpStatusCode.NotFound);
        network.Answer(B, """{"recordID":7}""");
        var store = new FakeSettingsStore(A, B);
        using var host = Host(network, store);

        var result = await host.Client().ClearDepartureAsync(7);

        Assert.Equal(CoreApiOutcome.Failed, result.Outcome);
        Assert.True(result.Reached);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal("Flight not found", result.Error);
        Assert.Equal([A], network.Hosts());
    }

    [Fact]
    public async Task SendsTheTenantsDirectorKeyAndTheSignedInUsersLogin()
    {
        var network = new FakeNetwork();
        network.Answer(A, """{"Status":"OK"}""");
        using var host = Host(network, new FakeSettingsStore(A) { DirectorKey = "tenant-key" });
        using var circuit = host.NewCircuit("ana@airport.test");

        await circuit.ServiceProvider.GetRequiredService<CoreApiClient>().GetFeedStatusAsync();

        var request = Assert.Single(network.Requests);
        Assert.Equal("tenant-key", Header(request, "X-API-KEY"));
        Assert.Equal("ana@airport.test", Header(request, "Username"));
    }

    [Fact]
    public async Task SendsTheServiceNameForSystemWork()
    {
        var network = new FakeNetwork();
        network.Answer(A, """{"Status":"OK"}""");
        using var host = Host(network, new FakeSettingsStore(A));
        using var worker = host.NewCircuit(userPrincipalName: null);

        await worker.ServiceProvider.GetRequiredService<CoreApiClient>().GetFeedStatusAsync();

        Assert.Equal(CoreApiClient.ServiceUsername, Header(Assert.Single(network.Requests), "Username"));
    }

    // C-02: the window goes as ISO 8601 with the airport's offset, whatever the server's culture.
    [Fact]
    public async Task SendsTheListWindowAsIso8601WithItsOffset()
    {
        var network = new FakeNetwork();
        network.Answer(A, "[]");
        using var host = Host(network, new FakeSettingsStore(A));
        var client = host.Client();
        var start = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(-4));
        var end = new DateTimeOffset(2026, 11, 1, 23, 0, 0, TimeSpan.FromHours(-5));

        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("fr-CA");
        try
        {
            await client.GetArrivalsAsync(start, end);
            await client.GetDeparturesAsync(start, end);
            await client.PreserveAllAsync(start, end);
            await client.ClearAllAsync(start, end);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }

        Assert.Equal(["Override/Arrivals", "Override/Departures", "Override/PreserveAll", "Override/ClearAll"], network.Paths());
        foreach (var request in network.Requests)
        {
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri!.Query);
            Assert.Equal("2026-11-01T00:00:00-04:00", query["StartDateTime"].ToString());
            Assert.Equal("2026-11-01T23:00:00-05:00", query["EndDateTime"].ToString());
        }
    }

    [Fact]
    public async Task TwoUsersInTwoCircuitsEachSendTheirOwnUsername()
    {
        var network = new FakeNetwork();
        network.Answer(A, """{"Status":"OK"}""");
        using var host = Host(network, new FakeSettingsStore(A));
        using var ana = host.NewCircuit("ana@airport.test");
        using var ben = host.NewCircuit("ben@airport.test");
        var anaClient = ana.ServiceProvider.GetRequiredService<CoreApiClient>();
        var benClient = ben.ServiceProvider.GetRequiredService<CoreApiClient>();

        // Interleaved, so a value shared between instances would show up on the other user's calls.
        var calls = Enumerable.Range(0, 20)
            .Select(i => (i % 2 == 0 ? anaClient : benClient).ClearArrivalAsync(i))
            .ToList();
        await Task.WhenAll(calls);

        var sent = network.Requests.ToList();
        Assert.Equal(20, sent.Count);
        foreach (var request in sent)
        {
            var recordId = int.Parse(request.RequestUri!.Segments.Last());
            Assert.Equal(recordId % 2 == 0 ? "ana@airport.test" : "ben@airport.test", Header(request, "Username"));
        }
    }

    [Fact]
    public async Task NoCallReachesARetiredRoute()
    {
        var network = new FakeNetwork();
        network.Answer(A, "{}");
        using var host = Host(network, new FakeSettingsStore(A));
        var client = host.Client();

        // Every public call, so a call added later is covered without editing this test.
        var calls = typeof(CoreApiClient).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(m => typeof(Task).IsAssignableFrom(m.ReturnType))
            .ToList();
        Assert.True(calls.Count >= 20);
        foreach (var call in calls)
            await (Task)call.Invoke(client, call.GetParameters().Select(SampleArgument).ToArray())!;

        Assert.Equal(calls.Count, network.Requests.Count);
        var paths = network.Paths();
        Assert.DoesNotContain(paths, p => p.StartsWith("Server/UpdateActive", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(paths, p => p.Equals("Messages/Languages", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(paths, p => p.Equals("Override/ChangedFlights", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WithNothingSavedNoCallIsMade()
    {
        var network = new FakeNetwork();
        using var host = Host(network, new FakeSettingsStore { DirectorKey = null });

        var result = await host.Client().GetFeedStatusAsync();

        Assert.Equal(CoreApiOutcome.NotConfigured, result.Outcome);
        Assert.Empty(network.Requests);
    }

    [Fact]
    public async Task AKeyFromAnotherKeyRingIsNotConfiguredAndSaysToEnterItAgain()
    {
        var network = new FakeNetwork();
        using var host = Host(network, new FakeSettingsStore(A) { DirectorKey = null });

        var result = await host.Client().GetFeedStatusAsync();

        Assert.Equal(CoreApiOutcome.NotConfigured, result.Outcome);
        Assert.Equal(CoreApiKeyProtector.UnreadableMessage, result.Error);
        Assert.Empty(network.Requests);
    }

    [Fact]
    public async Task ACheckedConnectionUsesTheCandidateSettingsAndRecordsNothing()
    {
        var network = new FakeNetwork();
        network.Refuse(A);
        network.Answer(B, "\"key-id\"");
        var store = new FakeSettingsStore(C) { DirectorKey = "old-key" };
        using var host = Host(network, store);

        var result = await host.Client().CheckConnectionAsync("new-key", [A, B]);

        Assert.Equal("key-id", result.Value);
        Assert.Equal([A, B], network.Hosts());
        Assert.Equal("new-key", Header(network.Requests.Last(), "X-API-KEY"));
        Assert.Null(store.LastGoodServer);
    }

    [Theory]
    [InlineData("http://a.test, https://b.test/ ;http://a.test\nhttp://c.test:8080/api/", new[] { "http://a.test", "https://b.test", "http://c.test:8080/api" })]
    [InlineData("  ", new string[0])]
    public void ParsesATypedServerListInOrder(string typed, string[] expected)
    {
        Assert.Equal(expected, CoreApiSettings.ParseServers(typed));
    }

    [Theory]
    [InlineData("coreapi.test")]
    [InlineData("ftp://coreapi.test")]
    public void RefusesAServerThatIsNotAnHttpAddress(string typed)
    {
        Assert.Throws<ArgumentException>(() => CoreApiSettings.ParseServers(typed));
    }

    private static string? Header(HttpRequestMessage request, string name) =>
        request.Headers.TryGetValues(name, out var values) ? values.Single() : null;

    private static object? SampleArgument(System.Reflection.ParameterInfo parameter)
    {
        var type = parameter.ParameterType;
        if (type == typeof(CancellationToken)) return CancellationToken.None;
        if (type == typeof(string)) return "1";
        if (type == typeof(int)) return 1;
        if (type == typeof(bool)) return false;
        if (type == typeof(DateTime)) return new DateTime(2026, 10, 18, 6, 0, 0);
        if (type == typeof(DateTime?)) return null;
        if (type == typeof(DateTimeOffset)) return new DateTimeOffset(2026, 10, 18, 6, 0, 0, TimeSpan.FromHours(-4));
        if (type == typeof(IReadOnlyList<string>)) return new[] { A };
        return Activator.CreateInstance(type);
    }

    // ── Test doubles ──────────────────────────────────────────────────────

    /// <summary>A clock whose timers fire only when a test advances it.</summary>
    private sealed class ManualClock : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<ManualTimer> _timers = new();
        private DateTimeOffset _now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate) return _now;
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan by)
        {
            List<ManualTimer> due;
            lock (_gate)
            {
                _now += by;
                due = _timers.Where(t => t.DueAt <= _now).ToList();
                foreach (var t in due) _timers.Remove(t);
            }
            foreach (var t in due) t.Fire();
        }

        private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
        {
            public DateTimeOffset DueAt { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (clock._gate)
                {
                    clock._timers.Remove(this);
                    if (dueTime == Timeout.InfiniteTimeSpan) return true;
                    DueAt = clock._now + dueTime;
                    clock._timers.Add(this);
                }
                return true;
            }

            public void Fire() => callback(state);

            public void Dispose()
            {
                lock (clock._gate) clock._timers.Remove(this);
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    internal static TestHost Host(FakeNetwork network, FakeSettingsStore store, TimeProvider? clock = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock ?? TimeProvider.System);
        services.AddSingleton<ICoreApiSettingsStore>(store);
        services.AddScoped<ISessionState, FakeSession>();
        services.AddCoreApiClient().ConfigurePrimaryHttpMessageHandler(() => network);
        return new TestHost(services.BuildServiceProvider());
    }

    internal sealed class TestHost(ServiceProvider provider) : IDisposable
    {
        private readonly IServiceScope _scope = provider.CreateScope();

        public CoreApiClient Client() => _scope.ServiceProvider.GetRequiredService<CoreApiClient>();

        /// <summary>A second scope stands for a second Blazor circuit.</summary>
        public IServiceScope NewCircuit(string? userPrincipalName)
        {
            var scope = provider.CreateScope();
            var session = scope.ServiceProvider.GetRequiredService<ISessionState>();
            if (userPrincipalName is not null) session.User = new FakeUser(userPrincipalName);
            return scope;
        }

        public void Dispose()
        {
            _scope.Dispose();
            provider.Dispose();
        }
    }

    internal sealed class FakeSettingsStore(params string[] servers) : ICoreApiSettingsStore
    {
        public string? DirectorKey { get; set; } = "director-key";
        public List<string> Servers { get; set; } = servers.ToList();
        public string? LastGoodServer { get; set; }
        public string? HubUrl { get; set; }
        public int LastGoodWrites { get; private set; }
        public int KeySaves { get; private set; }

        public Task<CoreApiSettings?> ReadAsync(CancellationToken ct) =>
            Task.FromResult<CoreApiSettings?>(Servers.Count == 0 && DirectorKey is null
                ? null
                : new CoreApiSettings(DirectorKey, Servers.ToList(), LastGoodServer, HubUrl));

        public Task SaveAsync(string directorKey, IReadOnlyList<string> servers, string? hubUrl, CancellationToken ct)
        {
            DirectorKey = directorKey;
            Servers = servers.ToList();
            LastGoodServer = null;
            HubUrl = CoreApiSettings.NormaliseHubUrl(hubUrl);
            KeySaves++;
            return Task.CompletedTask;
        }

        public Task SaveHubUrlAsync(string? hubUrl, CancellationToken ct)
        {
            HubUrl = CoreApiSettings.NormaliseHubUrl(hubUrl);
            return Task.CompletedTask;
        }

        public Task RecordLastGoodServerAsync(string server, CancellationToken ct)
        {
            LastGoodServer = server;
            LastGoodWrites++;
            return Task.CompletedTask;
        }
    }

    /// <summary>Answers, refuses or hangs per server, and records every request it was sent.</summary>
    internal sealed class FakeNetwork : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<string, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _servers = new();

        public ConcurrentQueue<HttpRequestMessage> Requests { get; } = new();

        /// <summary>Completes when a request reaches a server set to hang.</summary>
        public ConcurrentDictionary<string, TaskCompletionSource> Hanging { get; } = new();

        public void Answer(string server, string json, HttpStatusCode status = HttpStatusCode.OK) =>
            _servers[server] = (_, _) => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });

        /// <summary>Answers each request with the JSON the handler builds from it, for a server with state.</summary>
        public void Serve(string server, Func<HttpRequestMessage, Task<string>> handler) =>
            _servers[server] = async (request, _) => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(await handler(request), Encoding.UTF8, "application/json")
            };

        public void Refuse(string server) =>
            _servers[server] = (_, _) => throw new HttpRequestException("Connection refused");

        public void Hang(string server)
        {
            Hanging[server] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _servers[server] = async (_, ct) =>
            {
                Hanging[server].TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
                throw new InvalidOperationException("unreachable");
            };
        }

        public List<string> Hosts() => Requests.Select(r => r.RequestUri!.GetLeftPart(UriPartial.Authority)).ToList();

        public List<string> Paths() => Requests.Select(r => r.RequestUri!.AbsolutePath.TrimStart('/')).ToList();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Enqueue(request);
            var server = request.RequestUri!.GetLeftPart(UriPartial.Authority);
            return _servers.TryGetValue(server, out var behaviour)
                ? behaviour(request, ct)
                : throw new HttpRequestException($"No such host {server}");
        }
    }

    private sealed record FakeUser(string UserPrincipalName) : ICurrentUser
    {
        public int UserId => 1;
        public string? FriendlyName => null;
        public string? GivenName => null;
        public string? Surname => null;
        public bool IsDisabled => false;
        public int RoleId => 1;
        public string? ExtendedData => null;
        public Guid SecurityStamp => Guid.Empty;
    }

    internal sealed class FakeSession : ISessionState
    {
        public ICurrentUser? User { get; set; }
        public string? UserName => User?.UserPrincipalName;
        public string? Role => null;
        public Roles.Role RoleEnum { get; set; } = Roles.Role.Admin;
        public int ActualRoleId => 1;
        public string? ActualRoleName => null;
        public Task<IReadOnlySet<string>> GetPermissionsAsync() => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
        public Task<bool> HasPermissionAsync(string permissionKey) => Task.FromResult(true);
        public List<int> AssignedLocationIds { get; set; } = new();
        public bool IsDisabled => false;
        public Dictionary<int, ResolvedAccess?> LocationAccessMap { get; set; } = new();
        public string? PreferredLanguage { get; set; }
        public string? ProfilePhotoDataUri { get; set; }
        public string UserInitials { get; set; } = "";
        public Task LoadUserAsync(string username, ClaimsPrincipal? claimsPrincipal = null) => Task.CompletedTask;
        public Task LoadBridgeIdentityAsync(BridgeSessionIdentity identity) => Task.CompletedTask;
        public TaskCompletionSource<bool> SessionLoaded { get; } = new();
        public event Action? LocationAccessChanged { add { } remove { } }
        public Task RefreshLocationAccessAsync() => Task.CompletedTask;
        public Guid? PlatformUserId => null;
        public IReadOnlyList<TenantMembershipInfo> TenantMemberships => [];
        public bool IsPlatformOperator => false;
        public bool IsTenantAdmin => false;
        public bool IsAuditor => false;
    }
}
