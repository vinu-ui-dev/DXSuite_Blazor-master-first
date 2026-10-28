using DXSuite.Connectors.Services.CoreApi;
using DXSuite.Director.Entities;
using DXSuite.Director.Services.CoreApi;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Tests;

/// <summary>
/// The tenant's CoreAPI connection as kept in its Director database (CAPI-01, CAPI-06, HOST-04). Two stores
/// on one database stand for two Director instances. Runs against a Director database on LocalDB.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CoreApiSettingsStoreTests : IAsyncLifetime
{
    private readonly string _database = $"CoreApiSettings_{Guid.NewGuid():N}"[..30];

    // One key ring shared by both instances, as the shared ring (HOST-04) gives them.
    private readonly IDataProtectionProvider _keyRing = new EphemeralDataProtectionProvider();

    private string Connection =>
        LocalDb.Connection(_database);

    private DirectorDbContext Director() =>
        new(new DbContextOptionsBuilder<DirectorDbContext>().UseSqlServer(Connection).Options);

    private CoreApiSettingsStore Instance(IDataProtectionProvider? keyRing = null) =>
        new(new Factory(Director), new CoreApiKeyProtector(keyRing ?? _keyRing), TimeProvider.System);

    public async Task InitializeAsync()
    {
        if (!LocalDbFactAttribute.IsAvailable) return;
        await using var db = Director();
        await LocalDb.EnsureCreatedAsync(db.Database);
    }

    public async Task DisposeAsync()
    {
        if (!LocalDbFactAttribute.IsAvailable) return;
        await using var db = Director();
        await db.Database.EnsureDeletedAsync();
    }

    [LocalDbFact]
    public async Task NothingSavedReadsAsNoSettings()
    {
        Assert.Null(await Instance().ReadAsync(default));
    }

    [LocalDbFact]
    public async Task SavesTheKeyProtectedAndTheServersInOrder()
    {
        await Instance().SaveAsync("director-secret", ["http://b.test/", "http://a.test"], null, default);

        var read = await Instance().ReadAsync(default);
        Assert.NotNull(read);
        Assert.Equal("director-secret", read.DirectorKey);
        Assert.Equal(["http://b.test", "http://a.test"], read.Servers);
        Assert.Null(read.LastGoodServer);

        await using var db = Director();
        var stored = await db.TblCoreApiConnections.SingleAsync();
        Assert.DoesNotContain("director-secret", stored.DirectorKeyProtected);
    }

    [LocalDbFact]
    public async Task TheLastGoodServerOneInstanceRecordsIsTheOneTheOtherStartsFrom()
    {
        var first = Instance();
        var second = Instance();
        await first.SaveAsync("director-secret", ["http://a.test", "http://b.test"], null, default);

        await first.RecordLastGoodServerAsync("http://b.test", default);

        var read = await second.ReadAsync(default);
        Assert.Equal("http://b.test", read!.LastGoodServer);
        Assert.Equal(["http://b.test", "http://a.test"], read.ServersInTryOrder());
    }

    [LocalDbFact]
    public async Task SavingAgainReplacesTheListAndForgetsTheLastGoodServer()
    {
        var store = Instance();
        await store.SaveAsync("old-key", ["http://a.test", "http://b.test"], null, default);
        await store.RecordLastGoodServerAsync("http://b.test", default);

        await store.SaveAsync("new-key", ["http://c.test"], null, default);

        var read = await store.ReadAsync(default);
        Assert.Equal("new-key", read!.DirectorKey);
        Assert.Equal(["http://c.test"], read.Servers);
        Assert.Null(read.LastGoodServer);
    }

    [LocalDbFact]
    public async Task AKeySavedUnderAnotherKeyRingIsUnreadableNotPassedThrough()
    {
        await Instance(new EphemeralDataProtectionProvider()).SaveAsync("director-secret", ["http://a.test"], null, default);

        var read = await Instance().ReadAsync(default);

        Assert.NotNull(read);
        Assert.False(read.IsKeyReadable);
        Assert.False(read.IsConfigured);
        Assert.Equal(["http://a.test"], read.Servers);
    }

    [LocalDbFact]
    public async Task KeepsTheHubAddressWithTheConnectionAndDropsItWhenSavedWithout()
    {
        var store = Instance();
        await store.SaveAsync("director-secret", ["http://a.test"], "http://hub.test/pulse/", default);

        Assert.Equal("http://hub.test/pulse", (await store.ReadAsync(default))!.HubUrl);

        await store.SaveAsync("director-secret", ["http://a.test"], null, default);

        Assert.Null((await store.ReadAsync(default))!.HubUrl);
    }

    [LocalDbFact]
    public async Task TheHubAddressChangesOnItsOwnAndKeepsTheKeyAndServers()
    {
        var store = Instance();
        await store.SaveAsync("director-secret", ["http://a.test", "http://b.test"], null, default);
        await store.RecordLastGoodServerAsync("http://b.test", default);

        await store.SaveHubUrlAsync("http://hub.test/pulse/", default);

        var read = await store.ReadAsync(default);
        Assert.Equal("http://hub.test/pulse", read!.HubUrl);
        Assert.Equal("director-secret", read.DirectorKey);
        Assert.Equal(["http://a.test", "http://b.test"], read.Servers);
        Assert.Equal("http://b.test", read.LastGoodServer);

        await store.SaveHubUrlAsync("  ", default);

        Assert.Null((await store.ReadAsync(default))!.HubUrl);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveHubUrlAsync("hub.test/pulse", default));
    }

    [Theory]
    [InlineData("hub.test/pulse")]
    [InlineData("ftp://hub.test/pulse")]
    public async Task RefusesAHubAddressThatIsNotAnHttpAddress(string hubUrl)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Instance().SaveAsync("director-secret", ["http://a.test"], hubUrl, default));
    }

    [LocalDbFact]
    public async Task RefusesAListWithNoServers()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Instance().SaveAsync("director-secret", [], null, default));
    }

    private sealed class Factory(Func<DirectorDbContext> create) : IDbContextFactory<DirectorDbContext>
    {
        public DirectorDbContext CreateDbContext() => create();
    }
}
