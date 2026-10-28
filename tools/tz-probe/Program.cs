using System.Globalization;
using System.Reflection;
using System.Text;

// Throwaway probe for DXME-162. Prints what the runtime actually does with IANA
// timezone ids under whatever globalization configuration it is launched in.
// Run it three ways to cover the shapes the product can be deployed in:
//   dotnet run                                              (ICU, the Host's shipped config)
//   dotnet run -p:InvariantGlobalization=true               (the DbTool's config)
//   DOTNET_SYSTEM_GLOBALIZATION_USENLS=1 dotnet run         (a Windows box with no ICU)

var ids = new[]
{
    "America/Chicago",
    "America/Denver",
    "America/Phoenix",
    "America/New_York",
    "Central Standard Time",
    "Mountain Standard Time",
    "US Mountain Standard Time",
    "Eastern Standard Time",
};

Console.OutputEncoding = Encoding.UTF8;

Section("runtime + globalization mode");
Console.WriteLine($"Environment.Version        : {Environment.Version}");
Console.WriteLine($"RuntimeInformation.FWDesc  : {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
Console.WriteLine($"OSDescription              : {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
Console.WriteLine($"Environment.OSVersion      : {Environment.OSVersion}");
PrintGlobalizationMode();
Console.WriteLine($"CurrentCulture             : {CultureInfo.CurrentCulture.Name} ({CultureInfo.CurrentCulture.DisplayName})");
Console.WriteLine($"CultureInfo.GetCultures    : {SafeCultureCount()}");
Console.WriteLine($"TimeZoneInfo.Local.Id      : {TimeZoneInfo.Local.Id}");
Console.WriteLine($"GetSystemTimeZones().Count : {SafeZoneCount()}");

Section("FindSystemTimeZoneById");
foreach (var id in ids)
{
    try
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(id);
        var jan = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var jul = new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);
        Console.WriteLine($"OK   \"{id}\"");
        Console.WriteLine($"       -> Id={tz.Id}  Base={tz.BaseUtcOffset}  SupportsDST={tz.SupportsDaylightSavingTime}  Rules={tz.GetAdjustmentRules().Length}");
        Console.WriteLine($"       -> Jan offset={tz.GetUtcOffset(jan)} (DST={tz.IsDaylightSavingTime(jan)})  Jul offset={tz.GetUtcOffset(jul)} (DST={tz.IsDaylightSavingTime(jul)})");
        Console.WriteLine($"       -> Standard=\"{tz.StandardName}\"  Daylight=\"{tz.DaylightName}\"  Display=\"{tz.DisplayName}\"");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL \"{id}\"");
        Console.WriteLine($"       -> {ex.GetType().FullName}: {ex.Message}");
        if (ex.InnerException is { } inner)
            Console.WriteLine($"       -> inner {inner.GetType().FullName}: {inner.Message}");
    }
}

Section("TryConvertIanaIdToWindowsId / TryConvertWindowsIdToIanaId");
Console.WriteLine($"(declaring assembly: {typeof(TimeZoneInfo).Assembly.GetName().Name} {typeof(TimeZoneInfo).Assembly.GetName().Version})");
foreach (var id in ids)
{
    var iana = TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var win);
    var back = TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var ia);
    var backUs = TimeZoneInfo.TryConvertWindowsIdToIanaId(id, "US", out var iaUs);
    Console.WriteLine($"\"{id}\"");
    Console.WriteLine($"   Iana->Windows = {iana} \"{win}\"");
    Console.WriteLine($"   Windows->Iana = {back} \"{ia}\"   (region US) = {backUs} \"{iaUs}\"");
}

Section("POSIX TZ derived from TimeZoneInfo adjustment rules");
foreach (var id in new[] { "America/Chicago", "America/Denver", "America/Phoenix" })
{
    try
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(id);
        Console.WriteLine($"{id,-20} -> {DerivePosix(tz)}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{id,-20} -> unavailable ({ex.GetType().Name})");
    }
}

Section("ICU / NLS artefacts on this box");
foreach (var p in new[] { @"C:\Windows\System32\icu.dll", @"C:\Windows\System32\icuuc.dll" })
    Console.WriteLine($"{p} exists={File.Exists(p)}");
Console.WriteLine($"registry TZ key count = {SafeRegistryZoneCount()}");

static void Section(string title)
{
    Console.WriteLine();
    Console.WriteLine(new string('=', 78));
    Console.WriteLine(title);
    Console.WriteLine(new string('=', 78));
}

static void PrintGlobalizationMode()
{
    // GlobalizationMode is internal; reflection is the only way to read the effective
    // mode rather than infer it from the switches that were requested.
    var t = typeof(object).Assembly.GetType("System.Globalization.GlobalizationMode");
    if (t is null) { Console.WriteLine("GlobalizationMode         : <type not found>"); return; }
    foreach (var name in new[] { "Invariant", "UseNls", "Hybrid", "PredefinedCulturesOnly" })
    {
        var p = t.GetProperty(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Console.WriteLine($"GlobalizationMode.{name,-24}: {(p is null ? "<absent>" : p.GetValue(null)?.ToString() ?? "null")}");
    }
}

static string SafeCultureCount()
{
    try { return CultureInfo.GetCultures(CultureTypes.AllCultures).Length.ToString(); }
    catch (Exception ex) { return $"threw {ex.GetType().Name}"; }
}

static string SafeZoneCount()
{
    try { return TimeZoneInfo.GetSystemTimeZones().Count.ToString(); }
    catch (Exception ex) { return $"threw {ex.GetType().Name}"; }
}

static string SafeRegistryZoneCount()
{
    try
    {
        if (!OperatingSystem.IsWindows()) return "n/a";
        using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Time Zones");
        return k?.GetSubKeyNames().Length.ToString() ?? "<key missing>";
    }
    catch (Exception ex) { return $"threw {ex.GetType().Name}"; }
}

// Emits a POSIX TZ string (IEEE Std 1003.1 "TZ" rule form) from a TimeZoneInfo.
// Abbreviations are synthesised, because Windows TimeZoneInfo carries no POSIX
// abbreviation: StandardName is a localised sentence, not "CST".
static string DerivePosix(TimeZoneInfo tz)
{
    string Off(TimeSpan o)
    {
        // POSIX offsets are west-positive, the inverse of a UTC offset.
        var inv = o.Negate();
        var sign = inv < TimeSpan.Zero ? "-" : "";
        var a = inv.Duration();
        return a.Minutes == 0 && a.Seconds == 0
            ? $"{sign}{a.Hours}"
            : $"{sign}{a.Hours}:{a.Minutes:00}";
    }

    var std = Abbrev(tz.StandardName);
    if (!tz.SupportsDaylightSavingTime)
        return $"{std}{Off(tz.BaseUtcOffset)}";

    var rule = tz.GetAdjustmentRules()
                 .LastOrDefault(r => r.DateEnd >= DateTime.Today);
    if (rule is null)
        return $"{std}{Off(tz.BaseUtcOffset)}";

    var dst = Abbrev(tz.DaylightName);
    var sb = new StringBuilder();
    sb.Append(std).Append(Off(tz.BaseUtcOffset));
    sb.Append(dst).Append(Off(tz.BaseUtcOffset + rule.DaylightDelta));
    sb.Append(',').Append(Transition(rule.DaylightTransitionStart));
    sb.Append(',').Append(Transition(rule.DaylightTransitionEnd));
    return sb.ToString();

    static string Transition(TimeZoneInfo.TransitionTime t)
    {
        // Mm.w.d: month, week (5 = last), day-of-week (0 = Sunday). "/2" is the POSIX
        // default so it is emitted only when the transition is not at 02:00 local.
        var time = t.TimeOfDay.TimeOfDay == TimeSpan.FromHours(2)
            ? ""
            : "/" + (t.TimeOfDay.TimeOfDay.Minutes == 0
                ? t.TimeOfDay.TimeOfDay.Hours.ToString()
                : $"{t.TimeOfDay.TimeOfDay.Hours}:{t.TimeOfDay.TimeOfDay.Minutes:00}");
        if (t.IsFixedDateRule) return $"J{t.Day}{time}";
        return $"M{t.Month}.{t.Week}.{(int)t.DayOfWeek}{time}";
    }

    static string Abbrev(string name)
    {
        var letters = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                          .Where(w => char.IsUpper(w[0]))
                          .Select(w => w[0])
                          .ToArray();
        var s = new string(letters);
        return s.Length >= 3 ? s : "XXX";
    }
}
