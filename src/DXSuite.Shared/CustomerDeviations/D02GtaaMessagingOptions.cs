namespace DXSuite.Shared.CustomerDeviations;

/// <summary>
/// Register row D-02, the GTAA messaging deviation (spec DEV-11). When on, messaging narrows to what
/// the customer can use without new code: a built-in Counter Message Type, English and French only,
/// and at most one Asset Slot (an ASL video) per Message Type. When off, those stay open.
/// </summary>
public sealed class D02GtaaMessagingOptions : ICustomerDeviationSwitch
{
    public static string Name => "D02_GtaaMessaging";

    public bool Enabled { get; set; }
}
