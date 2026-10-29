namespace DXSuite.Aviation.Pages.Cupps;

// Prototype data for the CUPPS agent screens (DXME-500). All of it is in memory; nothing is saved.

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
    public static readonly Dictionary<string, Airline> Airlines = new()
    {
        ["WS"] = new("WS", "WestJet", "#00788a"),
        ["AC"] = new("AC", "Air Canada", "#d22630", "STAR ALLIANCE"),
        ["PD"] = new("PD", "Porter Airlines", "#1c355e"),
        ["F8"] = new("F8", "Flair Airlines", "#5a2d82"),
        ["TS"] = new("TS", "Air Transat", "#172b4d"),
        ["AI"] = new("AI", "Air India", "#c8102e")
    };

    public static readonly List<Counter> Counters =
    [
        .. new[] { 1, 2, 3, 4 }.Select(i => new Counter($"G1-{i}", ["Terminal 3", "T3 Aisle G1"])),
        .. new[] { 1, 2, 3, 4, 5, 6 }.Select(i => new Counter($"G2-{i}", ["Terminal 3", "T3 Aisle G2"],
            i switch { 1 => "G2-2", 2 => "G2-1", 4 => "G2-5", 5 => "G2-4", _ => null })),
        new("G2-7", ["Terminal 3", "T3 Aisle G2", "T3 SSBDs"], Ssbd: true),
        new("G2-8", ["Terminal 3", "T3 Aisle G2", "T3 SSBDs"], Ssbd: true),
        .. new[] { 1, 2, 3, 4 }.Select(i => new Counter($"H1-{i}", ["Terminal 3", "T3 Aisle H1"])),
        .. new[] { 1, 2, 3 }.Select(i => new Counter($"C1-{i}", ["Terminal 1", "T1 Aisle C1"]))
    ];

    public static readonly string[] Zones = ["Terminal 3", "T3 Aisle G1", "T3 Aisle G2", "T3 Aisle H1", "T3 SSBDs", "Terminal 1", "T1 Aisle C1"];

    public static readonly List<Alloc> Allocations =
    [
        new("G1-1", "WS", 420, 720, "Dedicated"), new("G1-2", "WS", 420, 720, "Dedicated"),
        new("G1-3", "PD", 330, 720, "Common"), new("G1-4", "PD", 330, 720, "Common"),
        new("G2-1", "WS", 360, 840, "Common"), new("G2-2", "WS", 360, 840, "Common"), new("G2-3", "WS", 360, 840, "Common"),
        new("G2-4", "WS", 360, 840, "Common"), new("G2-5", "F8", 480, 780, "Common"), new("G2-6", "WS", 780, 1080, "Common"),
        new("G2-7", "WS", 360, 840, "Common"), new("G2-8", "WS", 360, 840, "Common"),
        new("H1-1", "PD", 330, 720, "Common"), new("H1-2", "PD", 330, 720, "Common"), new("H1-3", "TS", 600, 900, "Common"),
        new("C1-1", "AC", 240, 1380, "Common"), new("C1-2", "AC", 240, 1380, "Common"), new("C1-3", "AC", 240, 1380, "Common"),
        new("G1-4", "AI", 960, 1260, "Common"),
        new("G2-5", "WS", 780, 1080, "Common"), new("G1-3", "WS", 720, 960, "Common"), new("H1-4", "WS", 900, 1140, "Common")
    ];

    public static readonly List<Template> Templates =
    [
        new("t1", "Standard", "airport", "Check-In", "Enregistrement"),
        new("t2", "Standard", "airport", "Bag Drop", "Dépôt des bagages"),
        new("t3", "Standard", "airport", "Agent Assist", "Aide d'un agent"),
        new("t4", "Standard", "airport", "Oversized Baggage", "Bagages hors format", EndsToday: true),
        new("t5", "Standard", "airport", "Economy", "Économique"),
        new("t6", "Standard", "airport", "Business", "Affaires"),
        new("t7", "Standard", "WS", "WestJet Rewards", "WestJet Récompenses"),
        new("t8", "Standard", "WS", "Family Check-In", "Enregistrement familles"),
        new("c1", "Custom", "WS", "Summer Sale Bag Drop", "Solde d'été", Img: "linear-gradient(135deg,#00788a,#f2a900)"),
        new("c2", "Custom", "WS", "WestJet Vacations", "Vacances WestJet", EndsToday: true, Img: "linear-gradient(135deg,#0b4f6c,#62c3d0)"),
        new("c3", "Custom", "WS", "Calgary Stampede", "Stampede de Calgary", Img: "linear-gradient(135deg,#7a1f1f,#e0a43a)")
    ];

    /// <summary>Extra airport-wide Standard templates; the prototype switch adds enough to give WestJet 20 or 50.</summary>
    public static readonly List<Template> Many = BuildMany();

    private static List<Template> BuildMany()
    {
        (string En, string Fr)[] named =
        [
            ("Priority Check-In", "Enregistrement prioritaire"), ("Premium Economy", "Économique Privilège"), ("Group Check-In", "Enregistrement de groupe"),
            ("Baggage Services", "Services bagages"), ("Self Bag Drop", "Dépôt libre-service"), ("Kiosk Check-In", "Enregistrement par borne"),
            ("US Departures", "Départs vers les É.-U."), ("International", "International"), ("Domestic", "Intérieur"),
            ("Charter Check-In", "Enregistrement nolisé"), ("Pet Travel", "Voyage avec animaux"), ("Special Assistance", "Assistance spéciale")
        ];
        string[] cities =
        [
            "Calgary", "Vancouver", "Edmonton", "Winnipeg", "Halifax", "Ottawa", "Montreal", "Quebec City", "Regina", "Saskatoon",
            "Victoria", "Kelowna", "St. John's", "Charlottetown", "Moncton", "Thunder Bay", "Cancun", "Punta Cana", "Montego Bay", "Varadero",
            "Puerto Vallarta", "Los Cabos", "Orlando", "Tampa", "Fort Lauderdale", "Las Vegas", "Phoenix", "Los Angeles", "London Gatwick", "Paris"
        ];
        return named.Concat(cities.Select(c => ($"{c} Check-In", $"Enregistrement {c}")))
            .Select((x, i) => new Template($"x{i + 1}", "Standard", "airport", x.Item1, x.Item2))
            .ToList();
    }

    public static Template? Tpl(string? id) =>
        id is null ? null : Templates.FirstOrDefault(t => t.Id == id) ?? Many.FirstOrDefault(t => t.Id == id);

    /// <summary>The airline's approved canned Messages (EN, FR).</summary>
    public static readonly List<Canned> CannedMessages =
    [
        new("m1", "Please have your passport ready.", "Veuillez avoir votre passeport en main."),
        new("m2", "Self-service kiosks available for quick check-in and bag tagging.", "Bornes libre-service pour l'enregistrement et l'étiquetage rapides."),
        new("m3", "Ensure all luggage is attended.", "Ne laissez pas vos bagages sans surveillance."),
        new("m4", "Flight WS 2104 to Cancun is delayed. New time 12:40.", "Le vol WS 2104 vers Cancún est retardé. Nouvelle heure 12 h 40.")
    ];

    public static string MsgText(string id) => CannedMessages.FirstOrDefault(m => m.Id == id)?.En ?? id;

    /// <summary>Departures with their codeshares, Diagram 1 style.</summary>
    public static readonly List<Flight> Flights =
    [
        new(675, 675, "On Time", "Calgary (AB), CAN", [("WestJet", "WS1201"), ("Delta Air Lines", "DL7212"), ("KLM", "KL4421")], "D24"),
        new(700, 700, "On Time", "Vancouver (BC), CAN", [("WestJet", "WS655"), ("Air France", "AF6113")], "D30"),
        new(725, 760, "Delayed", "Cancun, MEX", [("WestJet", "WS2104")], "D41"),
        new(750, 750, "On Time", "Halifax (NS), CAN", [("WestJet", "WS3302"), ("Qantas", "QF3801")], "D22"),
        new(790, 790, "On Time", "Edmonton (AB), CAN", [("WestJet", "WS1411")], "D26"),
        new(815, 815, "On Time", "Punta Cana, DOM", [("WestJet", "WS2650")], "D43"),
        new(850, 850, "On Time", "Winnipeg (MB), CAN", [("WestJet", "WS711"), ("Delta Air Lines", "DL7290")], "D25")
    ];

    /// <summary>Air Canada's view swaps codeshares for Star Alliance partners.</summary>
    public static readonly Dictionary<string, Dictionary<string, (string Name, string Code)>> Partners = new()
    {
        ["AC"] = new()
        {
            ["Delta Air Lines"] = ("Lufthansa", "LH"), ["KLM"] = ("United Airlines", "UA"),
            ["Air France"] = ("Swiss", "LX"), ["Qantas"] = ("ANA", "NH")
        }
    };

    public static readonly Dictionary<string, Scenario> Scenarios = new()
    {
        ["agent"] = new("Shared", "ws.agent14", "WestJet agent", "Airline Agent", "G2-3", 630),
        ["ac"] = new("Shared", "ac.agent07", "Air Canada agent", "Airline Agent", "C1-1", 630, "AC"),
        ["super"] = new("Shared", "ws.super02", "WestJet supervisor", "Airline Supervisor", "G1-1", 630),
        ["split2"] = new("Shared", "ws.agent14", "WestJet agent", "Airline Agent", "G2-4", 630),
        ["splitboth"] = new("Shared", "ws.agent14", "WestJet agent", "Airline Agent", "G2-1", 630),
        ["waiting"] = new("Shared", "ws.agent14", "WestJet agent", "Airline Agent", "G2-6", 761),
        ["rmu"] = new("Individual", "jordan.smith@gtaa.com", "Jordan Smith", "RMU", null, 630)
    };

    public static readonly Dictionary<string, string[]> Keys = new()
    {
        ["Airline Agent"] = ["page:cupps"],
        ["Airline Supervisor"] = ["page:cupps", "feature:cupps-free-text", "feature:cupps-save-canned"],
        ["RMU"] = ["page:cupps", "feature:cupps-free-text", "feature:cupps-save-canned", "page:cupps-act-for"]
    };

    /// <summary>Published counter state, as Director last sent it. It belongs to the allocation, not the session.</summary>
    public static Dictionary<string, CounterSetup> SeedPublished()
    {
        static CounterSetup Open(string page, int sentAt, Action<CounterSetup>? extra = null)
        {
            var s = new CounterSetup { Open = true, Page = page, SentAt = sentAt };
            extra?.Invoke(s);
            return s;
        }
        return new()
        {
            ["G1-1"] = Open("t6", 555, s => { s.Msgs = ["m4"]; s.Dest = "Cancun"; }),
            ["G1-2"] = Open("t5", 555, s => s.Dest = "Cancun"),
            ["G2-1"] = Open("t1", 400),
            ["G2-2"] = CounterSetup.Closed(),
            ["G2-3"] = Open("t3", 582, s => { s.Msgs = ["m2"]; s.Sunflower = true; }),
            ["G2-4"] = Open("t2", 455),
            ["G2-7"] = Open("t2", 370),
            ["G2-8"] = CounterSetup.Closed(),
            ["C1-1"] = Open("t5", 300, s => { s.Msgs = ["m1"]; s.Sunflower = true; s.Asl = true; }),
            ["C1-2"] = Open("t2", 300),
            ["C1-3"] = CounterSetup.Closed(),
            ["H1-1"] = Open("t1", 340),
            ["H1-2"] = Open("t2", 340),
            ["H1-3"] = CounterSetup.Closed()
        };
    }

    public static string Hm(int m) => $"{m / 60:00}:{m % 60:00}";
    public static string Left(int m) => $"{m / 60}:{m % 60:00}";
    public static string Clock12(int m) => $"{(m / 60 % 12 == 0 ? 12 : m / 60 % 12)}:{m % 60:00} {(m >= 720 ? "PM" : "AM")}";
}
