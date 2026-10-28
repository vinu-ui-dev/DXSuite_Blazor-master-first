namespace DXSuite.Director.Services;

/// <summary>
/// Whether a device is running the flavour it was enrolled with.
///
/// Derived on read and never stored, so a reflavour fixes the cause with nothing left to clear and
/// no way for a stored flag to disagree with the facts.
/// </summary>
public enum FlavourState
{
    /// <summary>No flavour enrolled. The ordinary state of every applet player, and not a fault.</summary>
    NotEnrolled,

    /// <summary>Enrolled but the device has not declared anything yet.</summary>
    NotReported,

    /// <summary>The device is running what it was enrolled with.</summary>
    Matched,

    /// <summary>The device declares a flavour it was not enrolled with. A fault, for a human.</summary>
    Mismatched
}

public static class FlavourVerification
{
    /// <summary>
    /// Compares the flavour written at enrollment against the one the device last declared.
    ///
    /// A device with no enrolled flavour is never mismatched, whatever it declares. That is the
    /// load-bearing case: every applet player declares a platform on every heartbeat while having
    /// no enrolled flavour at all, so treating "declares something, enrolled nothing" as a
    /// contradiction would light up the entire existing fleet on its next beat.
    /// </summary>
    public static FlavourState Evaluate(string? enrolledPlatformName, string? declaredPlatformName)
    {
        if (string.IsNullOrWhiteSpace(enrolledPlatformName))
            return FlavourState.NotEnrolled;

        if (string.IsNullOrWhiteSpace(declaredPlatformName))
            return FlavourState.NotReported;

        return string.Equals(enrolledPlatformName, declaredPlatformName, StringComparison.OrdinalIgnoreCase)
            ? FlavourState.Matched
            : FlavourState.Mismatched;
    }

    /// <summary>
    /// Whether clearing a device's flavour is safe right now.
    ///
    /// Clearing is the last step of moving a player back to the applet, not the first. Null is
    /// never a mismatch, so a player cleared while it is still running the native flavour raises
    /// no flag and falls out of the update pipeline entirely, since ALL-scope resolution is guarded
    /// on the platform having a value. The device declares what it runs on every heartbeat, so this
    /// is a check rather than a caution somebody has to read.
    /// </summary>
    public static bool CanClearFlavour(string? enrolledPlatformName, string? declaredPlatformName)
        => Evaluate(enrolledPlatformName, declaredPlatformName) != FlavourState.Matched;
}
