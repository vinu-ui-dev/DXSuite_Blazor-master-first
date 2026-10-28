using DXSuite.Director.Entities;
using DXSuite.Director.Services.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace DXSuite.Director.Tests;

/// <summary>
/// Arbitration per display, per Screen Part (MSG-07 to MSG-10): Top priority first, then lowest effective rank,
/// ties by <c>Messaging:TieBehaviour</c>. Each row lists the Activations live on one display and the expected
/// result of each part as "activationId@rank" in order.
/// </summary>
public class MessageArbitrationTests
{
    private const MessageDisplayMode Full = MessageDisplayMode.FullScreen;

    // An Activation live on the display: its type's part, rank and Top priority flag, its own override, and the
    // minute (after a fixed base) it was sent at.
    public sealed record Live(int Id, MessageDisplayMode Part, int TypePriority, bool Top = false, int? Override = null, int SentMinute = 0);

    public static TheoryData<string, TieBehaviour, Live[], string, string, string> Cases() => new()
    {
        {
            "Top priority outranks a lower priority override",
            TieBehaviour.NewestOnly,
            [new(1, Full, TypePriority: 5, Top: true), new(2, Full, TypePriority: 2, Override: -100)],
            "1@5", "", ""
        },
        {
            "Top priority Activations order among themselves by effective rank",
            TieBehaviour.NewestOnly,
            [new(1, Full, TypePriority: 1, Top: true, SentMinute: 1), new(2, Full, TypePriority: 1, Top: true, Override: 0)],
            "2@0", "", ""
        },
        {
            "A priority override outranks a better type rank",
            TieBehaviour.NewestOnly,
            [new(1, Full, TypePriority: 1), new(2, Full, TypePriority: 9, Override: 0)],
            "2@0", "", ""
        },
        {
            "A worse priority override loses to a better type rank",
            TieBehaviour.NewestOnly,
            [new(1, Full, TypePriority: 3), new(2, Full, TypePriority: 1, Override: 7)],
            "1@3", "", ""
        },
        {
            "Newest only: the newest of the tied Activations wins",
            TieBehaviour.NewestOnly,
            [new(1, Full, TypePriority: 2, SentMinute: 0), new(2, Full, TypePriority: 2, SentMinute: 10), new(3, Full, TypePriority: 4, Override: 2, SentMinute: 5)],
            "2@2", "", ""
        },
        {
            "Rotate: the tied Activations in turn, oldest first, without the lower ranked one",
            TieBehaviour.Rotate,
            [new(1, Full, TypePriority: 2, SentMinute: 10), new(2, Full, TypePriority: 2, SentMinute: 0), new(3, Full, TypePriority: 3), new(4, Full, TypePriority: 5, Override: 2, SentMinute: 5)],
            "2@2,4@2,1@2", "", ""
        },
        {
            "Rotate: four tied give the 3 newest, oldest first",
            TieBehaviour.Rotate,
            [new(1, Full, TypePriority: 2, SentMinute: 0), new(2, Full, TypePriority: 2, SentMinute: 30), new(3, Full, TypePriority: 2, SentMinute: 10), new(4, Full, TypePriority: 2, SentMinute: 20)],
            "3@2,4@2,2@2", "", ""
        },
        {
            "Rotate: a single winner is shown alone",
            TieBehaviour.Rotate,
            [new(1, Full, TypePriority: 1), new(2, Full, TypePriority: 2, SentMinute: 5)],
            "1@1", "", ""
        },
        {
            "Three parts on one display are ranked separately",
            TieBehaviour.NewestOnly,
            [
                new(1, Full, TypePriority: 4),
                new(2, MessageDisplayMode.Overlay, TypePriority: 2, Top: true),
                new(3, MessageDisplayMode.Overlay, TypePriority: 1),
                new(4, MessageDisplayMode.Ticker, TypePriority: 6, Override: 3),
                new(5, MessageDisplayMode.Ticker, TypePriority: 5),
            ],
            "1@4", "2@2", "4@3"
        },
        {
            "Nothing live leaves every part empty",
            TieBehaviour.Rotate,
            [],
            "", "", ""
        },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Each_part_holds_the_arbitrated_activations_with_their_rank(
        string scenario, TieBehaviour tie, Live[] live, string fullScreen, string overlay, string ticker)
    {
        var result = Arbitrate(tie, live.Select(ToActivation));

        Assert.True(!string.IsNullOrWhiteSpace(scenario));
        Assert.Equal(fullScreen, Describe(result.FullScreen));
        Assert.Equal(overlay, Describe(result.Overlay));
        Assert.Equal(ticker, Describe(result.Ticker));
    }

    // MSG-07: a reorder rewrites the types' Priority; Activations without an override take the new order at once,
    // while an override keeps its own rank.
    [Fact]
    public void A_reorder_reranks_live_activations_that_have_no_override()
    {
        var irrops = Type(1, MessageDisplayMode.Overlay, priority: 1);
        var paging = Type(2, MessageDisplayMode.Overlay, priority: 2);
        var live = new[] { Activation(1, irrops, null, 0), Activation(2, paging, null, 0), Activation(3, irrops, 2, 0) };
        var arbitration = new MessageArbitration(Options.Create(new MessagingOptions { TieBehaviour = TieBehaviour.Rotate }));

        Assert.Equal("1@1", Describe(arbitration.Arbitrate(live).Overlay));

        irrops.Priority = 2;
        paging.Priority = 1;

        Assert.Equal("2@1", Describe(arbitration.Arbitrate(live).Overlay));
    }

    [Fact]
    public void TieBehaviour_is_NewestOnly_when_the_instance_does_not_set_it()
    {
        var arbitration = Registered(new());

        Assert.Equal("2@1", Describe(arbitration.Arbitrate(TwoTied()).Ticker));
    }

    [Fact]
    public void TieBehaviour_Rotate_binds_from_the_Messaging_section()
    {
        var arbitration = Registered(new() { ["Messaging:TieBehaviour"] = "Rotate" });

        Assert.Equal("1@1,2@1", Describe(arbitration.Arbitrate(TwoTied()).Ticker));
    }

    // Section 3.10: GTAA runs NewestOnly (A-24), the shipped default.
    [Fact]
    public void The_shipped_appsettings_set_TieBehaviour_to_NewestOnly()
    {
        var shipped = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), "src", "DXSuite.Host", "appsettings.json"))
            .Build();

        Assert.Equal("NewestOnly", shipped["Messaging:TieBehaviour"]);
    }

    private static MessageArbitration Registered(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddMessaging(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        return services.BuildServiceProvider().GetRequiredService<MessageArbitration>();
    }

    private static TblMessageActivation[] TwoTied()
    {
        var ticker = Type(1, MessageDisplayMode.Ticker, priority: 1);
        return [Activation(1, ticker, null, 0), Activation(2, ticker, null, 5)];
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DXSuite.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("DXSuite.sln not found above the test output");
    }

    private static readonly DateTime SentBase = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

    private static DisplayArbitration Arbitrate(TieBehaviour tie, IEnumerable<TblMessageActivation> live) =>
        new MessageArbitration(Options.Create(new MessagingOptions { TieBehaviour = tie })).Arbitrate(live);

    private static TblMessageActivation ToActivation(Live live) =>
        Activation(live.Id, Type(100 + live.Id, live.Part, live.TypePriority, live.Top), live.Override, live.SentMinute);

    private static TblMessageType Type(int id, MessageDisplayMode part, int priority, bool top = false) => new()
    {
        MessageTypeId = id,
        Code = $"T{id}",
        DisplayName = $"Type {id}",
        DisplayMode = part,
        Priority = priority,
        IsTopPriority = top,
        DefaultTextColor = "#FFFFFF",
        DefaultBackgroundColor = "#000000",
    };

    private static TblMessageActivation Activation(int id, TblMessageType type, int? priorityOverride, int sentMinute)
    {
        var message = new TblMessage
        {
            MessageId = 1000 + id,
            MessageTypeId = type.MessageTypeId,
            MessageType = type,
            Name = $"Message {id}",
            IsCanned = true,
            Status = MessageStatus.Approved,
        };
        var sent = SentBase.AddMinutes(sentMinute);
        return new TblMessageActivation
        {
            MessageActivationId = id,
            MessageId = message.MessageId,
            Message = message,
            StartUtc = sent,
            ActivatedUtc = sent,
            PriorityOverride = priorityOverride,
            Source = MessageActivationSource.DirectorAdmin,
        };
    }

    private static string Describe(IReadOnlyList<ArbitratedActivation> part) =>
        string.Join(",", part.Select(a => $"{a.Activation.MessageActivationId}@{a.Rank}"));
}
