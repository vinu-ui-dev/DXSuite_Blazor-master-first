namespace DXSuite.Aviation.Pages.Cupps;

// Types for the CUPPS agent screens. All data (airlines, counters, allocations, page templates, Messages, flights
// and what each counter currently shows) is loaded live from Director/CoreAPI by CuppsLiveDataProvider.

public record Airline(string Code, string Name, string Bg, string? Alliance = null);

/// <summary>A counter, the Display Zones its screen is in, and the other counter on the same screen.</summary>
public record Counter(string Id, string[] Zones, string? Split = null, bool Ssbd = false);

/// <summary>An allocation as CoreAPI returns it. Type: Common | Dedicated | Info. Times are minutes from midnight.</summary>
public record Alloc(string CounterId, string Code, int From, int To, string Type);

/// <summary>A Counter template. Scope is "airport" (airport-wide Standard) or an airline code.</summary>
public record Template(string Id, string Kind, string Scope, string En, string Fr, bool EndsToday = false, string? Img = null);

public record Canned(string Id, string En, string Fr);

public record Flight(int Sched, int Actual, string Status, string Dest, (string Airline, string No)[] Carriers, string Gate);

public record Scenario(string Kind, string User, string Who, string Role, string? Station, int Now, string? Airline = null);

/// <summary>What a counter shows (published) or what the agent is preparing (draft).</summary>
public class CounterSetup
{
    public bool Open { get; set; }
    public string? Page { get; set; }
    public List<string> Msgs { get; set; } = [];
    public string Text { get; set; } = "";
    public bool SaveText { get; set; }
    public bool Asl { get; set; }
    public bool Sunflower { get; set; }
    public string Dest { get; set; } = "";
    public string Layout { get; set; } = "one";
    public int SentAt { get; set; }

    public CounterSetup Clone() => new()
    {
        Open = Open, Page = Page, Msgs = [.. Msgs], Text = Text, SaveText = SaveText, Asl = Asl,
        Sunflower = Sunflower, Dest = Dest, Layout = Layout, SentAt = SentAt
    };

    public static CounterSetup Closed() => new() { Open = false };
}

public record HubMessage(string En, string Fr)
{
    public bool Dismissed { get; set; }
}

public static class CuppsData
{
    /// <summary>Feature keys per role (who may type free-text Messages, save canned Messages, act for another airline).</summary>
    public static readonly Dictionary<string, string[]> Keys = new()
    {
        ["Airline Agent"] = ["page:cupps"],
        ["Airline Supervisor"] = ["page:cupps", "feature:cupps-free-text", "feature:cupps-save-canned"],
        ["RMU"] = ["page:cupps", "feature:cupps-free-text", "feature:cupps-save-canned", "page:cupps-act-for"]
    };

    public static string Hm(int m) => $"{m / 60:00}:{m % 60:00}";
    public static string Left(int m) => $"{m / 60}:{m % 60:00}";
    public static string Clock12(int m) => $"{(m / 60 % 12 == 0 ? 12 : m / 60 % 12)}:{m % 60:00} {(m >= 720 ? "PM" : "AM")}";
}
