namespace DXSuite.Director.Services.Messaging;

/// <summary>How Activations tied at the same effective rank share a Screen Part (MSG-10).</summary>
public enum TieBehaviour
{
    /// <summary>The newest Activation wins the part.</summary>
    NewestOnly = 0,

    /// <summary>The 3 newest tied Activations are shown in turn, oldest first; the others wait until one ends.</summary>
    Rotate = 1
}

/// <summary>
/// Messaging's per-instance settings (1.1 section 3.10), bound from <see cref="SectionName"/>. Every node behind a
/// load balancer carries the same values (HOST-04). None of them is shown in the UI.
/// </summary>
public sealed class MessagingOptions
{
    public const string SectionName = "Messaging";

    public TieBehaviour TieBehaviour { get; set; } = TieBehaviour.NewestOnly;

    /// <summary>
    /// IANA id of the clock a scheduled send is read against on a display with no device of its own (MSG-16, A-15):
    /// GTAA's reference displays. Unset or unknown falls back to this server's time zone.
    /// </summary>
    public string? DefaultTimeZone { get; set; }

    /// <summary><c>Messaging:Delivery</c>: how Director reaches the Moxie script (INK-09).</summary>
    public MessagingDeliveryOptions Delivery { get; set; } = new();
}

/// <summary>The delivery settings under <c>Messaging:Delivery</c> (1.1 section 3.10).</summary>
public sealed class MessagingDeliveryOptions
{
    /// <summary>
    /// Where the doorbell posts its nudge, the script's listener (INK-09). Empty, the default, means pull only: the
    /// script finds changes on its own safety-net pull, as at GTAA until Director may reach the listener (A-04, Q-06).
    /// </summary>
    public string? DoorbellUrl { get; set; }

    /// <summary>False when <see cref="DoorbellUrl"/> is empty: pull only, and the doorbell does nothing.</summary>
    public bool DoorbellOn => !string.IsNullOrWhiteSpace(DoorbellUrl);
}
