using DXSuite.Connectors.Services.CoreApi.Models;

namespace DXSuite.Connectors.Services.CoreApi
{
    /// <summary>
    /// The audiences a flight display serves (CoreAPI's fixed set, ZONE-14). Director stores none of them; it only sets
    /// which ones a flight is rolled off, through the ROP, ROB, ROS and ROT flags (<see cref="RollCategories"/>).
    /// </summary>
    public enum DisplayCategory
    {
        Public,
        Baggage,
        Staff,
        Throw,
    }

    /// <summary>
    /// Which Display Categories (Public, Baggage, Staff, Throw) a flight is shown in (FDE-07). True = shown (rolled
    /// on), false = hidden (rolled off).
    /// </summary>
    public readonly record struct RollCategories(bool Public, bool Baggage, bool Staff, bool Throw)
    {
        /// <summary>CoreAPI's value for a shown category.</summary>
        public const string On = "Y";

        /// <summary>
        /// The explicit off Director sends. C-01 leaves the encoding to CoreAPI and is still open, so this is the
        /// assumed value (A-25); CoreAPI's answer changes only this line.
        /// </summary>
        public const string Off = "N";

        /// <summary>A new flight is shown in every category until the user unticks one.</summary>
        public static RollCategories AllShown { get; } = new(true, true, true, true);

        /// <summary>The four Display Categories, in the order every screen lists them.</summary>
        public static IReadOnlyList<DisplayCategory> All { get; } =
            [DisplayCategory.Public, DisplayCategory.Baggage, DisplayCategory.Staff, DisplayCategory.Throw];

        // The one map from a Display Category to its flag, its field setting and its member here; everything else
        // that handles the four categories goes through it.

        /// <summary>The category's flag on a flight or a Flight Edit (ROP, ROB, ROS or ROT).</summary>
        public static string? Flag(IRollFlags flags, DisplayCategory category) => category switch
        {
            DisplayCategory.Public => flags.ROP,
            DisplayCategory.Baggage => flags.ROB,
            DisplayCategory.Staff => flags.ROS,
            DisplayCategory.Throw => flags.ROT,
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };

        /// <summary>The field setting that opens the category on the Manage form (ROPModify to ROTModify).</summary>
        public static bool? ModifySetting(UserSettings settings, DisplayCategory category) => category switch
        {
            DisplayCategory.Public => settings.ROPModify,
            DisplayCategory.Baggage => settings.ROBModify,
            DisplayCategory.Staff => settings.ROSModify,
            DisplayCategory.Throw => settings.ROTModify,
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };

        public bool IsShown(DisplayCategory category) => category switch
        {
            DisplayCategory.Public => Public,
            DisplayCategory.Baggage => Baggage,
            DisplayCategory.Staff => Staff,
            DisplayCategory.Throw => Throw,
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };

        /// <summary>The categories for which <paramref name="shown"/> is true.</summary>
        public static RollCategories Of(Func<DisplayCategory, bool> shown) =>
            new(shown(DisplayCategory.Public), shown(DisplayCategory.Baggage), shown(DisplayCategory.Staff), shown(DisplayCategory.Throw));

        private static void SetFlag(IRollFlags flags, DisplayCategory category, string? value)
        {
            switch (category)
            {
                case DisplayCategory.Public: flags.ROP = value; break;
                case DisplayCategory.Baggage: flags.ROB = value; break;
                case DisplayCategory.Staff: flags.ROS = value; break;
                case DisplayCategory.Throw: flags.ROT = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(category));
            }
        }

        /// <summary>
        /// The categories as screens show them now: a flag on the flight's Flight Edit wins over the feed's, and
        /// anything other than <see cref="On"/> (null, empty or CoreAPI's off) counts as hidden.
        /// </summary>
        public static RollCategories Current(FEDepartureFlight flight) => Current(flight, flight.overrideFlightData);

        /// <inheritdoc cref="Current(FEDepartureFlight)"/>
        public static RollCategories Current(FEArrivalFlight flight) => Current(flight, flight.overrideFlightData);

        public static bool IsOn(string? flag) => string.Equals(flag?.Trim(), On, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Whether a Flight Edit's flag rolls its category off: the edit carries a value and it is not <see cref="On"/>,
        /// whatever CoreAPI's off encoding becomes (FDE-09). An unset flag is no user choice, so it never counts.
        /// </summary>
        public static bool IsRolledOff(string? editFlag) => !string.IsNullOrWhiteSpace(editFlag) && !IsOn(editFlag);

        /// <summary>
        /// Writes a Flight Edit's flags for going from these categories (as the form opened) to <paramref name="chosen"/>:
        /// a changed category gets <see cref="On"/> or the explicit <see cref="Off"/>, an unchanged one null, which
        /// the wire DTOs omit, so CoreAPI never sees a flag the user did not change (FDE-06, C-01).
        /// </summary>
        public void WriteChanges(RollCategories chosen, IRollFlags edit)
        {
            foreach (var category in All) SetFlag(edit, category, Changed(IsShown(category), chosen.IsShown(category)));
        }

        /// <summary>Writes every flag of a new flight, an unticked category as the explicit <see cref="Off"/> (C-01).</summary>
        public void WriteAll(IRollFlags flight)
        {
            foreach (var category in All) SetFlag(flight, category, Encode(IsShown(category)));
        }

        private static string? Changed(bool was, bool now) => was == now ? null : Encode(now);

        private static string Encode(bool shown) => shown ? On : Off;

        private static RollCategories Current(IRollFlags feed, IRollFlags? edit) =>
            Of(c => IsOn((edit is null ? null : Flag(edit, c)) ?? Flag(feed, c)));
    }
}
