using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using DXSuite.Director.Authorization;
using DXSuite.Director.Controllers;
using DXSuite.Director.Entities;
using DXSuite.Director.Services;
using DXSuite.Director.Services.Messaging;
using DXSuite.Shared.CustomerDeviations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Tests.TwoHost;

// Clause ids refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// The delivery route on two instances (B-81, INK-05, INK-06, HOST-04): both hosts serve the same bytes under the same
/// ETag for the same data, either host answers 304 to the other's ETag, and a shown version served by both is stamped
/// Delivered to signage once. The script calls the load-balanced name, so consecutive pulls may land on either host.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "TwoHost")]
public sealed class DeliveryRouteTwoHostTests
{
    private const int Gate = 101, Lounge = 102;
    private static readonly DateTime HostAClock = new(2026, 10, 20, 14, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime HostBClock = HostAClock.AddSeconds(7);

    [LocalDbFact]
    public async Task Both_hosts_serve_one_ETag_and_a_version_served_by_both_is_stamped_once()
    {
        await using var hosts = await TwoHosts.StartAsync(Configure);
        var version = await ShownVersionAsync(hosts);

        var fromA = await GetAsync(hosts.A, ifNoneMatch: null);
        var fromB = await GetAsync(hosts.B, ifNoneMatch: null);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (fromA.Status, fromB.Status));
        Assert.Equal(fromA.ETag, fromB.ETag);
        Assert.Equal(fromA.Body, fromB.Body);
        Assert.Contains($"\"shownVersionId\":{version}", fromA.Body);
        Assert.Equal(HttpStatusCode.NotModified, (await GetAsync(hosts.B, fromA.ETag)).Status);
        Assert.Equal(HttpStatusCode.NotModified, (await GetAsync(hosts.A, fromB.ETag)).Status);
        Assert.Equal(HostAClock, await FirstServedAsync(hosts, version));
    }

    [LocalDbFact]
    public async Task A_version_both_hosts_serve_at_once_is_stamped_by_one_of_them()
    {
        await using var hosts = await TwoHosts.StartAsync(Configure);
        var version = await ShownVersionAsync(hosts);

        var both = await Task.WhenAll(GetAsync(hosts.A, null), GetAsync(hosts.B, null));

        Assert.All(both, r => Assert.Equal(HttpStatusCode.OK, r.Status));
        Assert.Equal(both[0].ETag, both[1].ETag);
        Assert.Contains(await FirstServedAsync(hosts, version), new DateTime?[] { HostAClock, HostBClock });
    }

    private static void Configure(TwoHostOptions o)
    {
        o.SeedAsync = SeedDisplaysAsync;
        o.ConfigureServices = (name, services, _) =>
        {
            services.AddControllers().AddApplicationPart(typeof(MessagingDeliveryController).Assembly);
            services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, MessagingReadKey>(AuthSchemes.AdminApiKey, _ => { });
            services.AddAuthorization();
            services.AddLogging();
            services.AddSingleton(Options.Create(new MessagingOptions { DefaultTimeZone = "America/Toronto" }));
            services.AddSingleton(Options.Create(new D02GtaaMessagingOptions { Enabled = true }));
            services.AddSingleton<MessageArbitration>();
            services.AddSingleton<DisplayTimeZones>();
            services.AddSingleton<TimeProvider>(new FixedClock(name == "A" ? HostAClock : HostBClock));
            services.AddScoped<MessageDelivery>();
        };
        o.ConfigureApp = (_, app) =>
        {
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();
        };
    }

    // Parents the delivery does not read (location types, display types, constraints) are left out.
    private static Task SeedDisplaysAsync(DirectorDbContext db) => db.Database.ExecuteSqlRawAsync($"""
        ALTER TABLE tblLocation NOCHECK CONSTRAINT ALL;
        ALTER TABLE tblDisplay NOCHECK CONSTRAINT ALL;
        SET IDENTITY_INSERT tblLocation ON;
        INSERT INTO tblLocation (LocationId, LocationTypeId, LocationGroupId, LocationName, SortOrder, EnableShuffle, EnableFade, EnableContentApproval)
        VALUES (1, 1, 1, 'Terminal 1', 1, 0, 1, 0);
        SET IDENTITY_INSERT tblLocation OFF;
        SET IDENTITY_INSERT tblDisplay ON;
        INSERT INTO tblDisplay (DisplayId, LocationId, DisplayName, DisplayMode, DisplayTypeId, ConstraintId, UseClock, UseVideoMask, EnableFade, EnableContentApproval)
        VALUES ({Lounge}, 1, 'Lounge', 0, 1, 1, 0, 0, 1, 0), ({Gate}, 1, 'Gate', 0, 1, 1, 0, 0, 1, 0);
        SET IDENTITY_INSERT tblDisplay OFF;
        """);

    /// <summary>An Emergency sent now to the gate display until cancelled, with its first shown version, as a send writes it.</summary>
    private static async Task<int> ShownVersionAsync(TwoHosts hosts)
    {
        await using var db = hosts.CreateDbContext();
        var sent = HostAClock.AddMinutes(-10);
        var activation = new TblMessageActivation
        {
            Message = new TblMessage
            {
                MessageTypeId = 1, Name = "Evacuate", IsCanned = true, Status = MessageStatus.Approved, CreatedUtc = sent,
                Texts = [new TblMessageText { LanguageCode = "eng", Body = "Evacuate" }],
            },
            StartUtc = sent, ActivatedUtc = sent, Source = MessageActivationSource.Api,
            Targets = [new TblMessageActivationTarget { DisplayId = Gate, DisplayName = "Gate", LocationName = "Terminal 1" }],
            Versions =
            [
                new TblMessageActivationVersion
                {
                    Sequence = 1, Reason = ShownVersionReason.FirstSend, MessageTypeId = 1, MessageTypeCode = "EMERGENCY", EffectiveRank = 10,
                    Texts = """{"eng":"Evacuate","fra":"Évacuez"}""", AssetSlots = "[]", SentUtc = sent,
                },
            ],
        };
        db.TblMessageActivations.Add(activation);
        await db.SaveChangesAsync();
        return activation.Versions.Single().MessageActivationVersionId;
    }

    private static async Task<DateTime?> FirstServedAsync(TwoHosts hosts, int versionId)
    {
        await using var db = hosts.CreateDbContext();
        return await db.TblMessageActivationVersions.Where(v => v.MessageActivationVersionId == versionId).Select(v => v.FirstServedUtc).SingleAsync();
    }

    private sealed record Pull(HttpStatusCode Status, string? ETag, string Body);

    private static async Task<Pull> GetAsync(DirectorTestHost host, string? ifNoneMatch)
    {
        using var client = host.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/messaging/delivery");
        request.Headers.Add("X-API-KEY", "moxie-script");
        if (ifNoneMatch is not null) request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(ifNoneMatch));
        using var response = await client.SendAsync(request);
        return new Pull(response.StatusCode, response.Headers.ETag?.ToString(), await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Stands in for the AdminApiKey scheme, which these hosts do not wire to a key store: every call is the script's key
    /// with <c>messaging:read</c>, left where the real scheme leaves it.
    /// </summary>
    private sealed class MessagingReadKey(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            Context.Items["ApiKeyPermissions"] = ApiKeyScopes.MessagingRead;
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("ApiKeyName", "Moxie script")], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }

    private sealed class FixedClock(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc);
    }
}
