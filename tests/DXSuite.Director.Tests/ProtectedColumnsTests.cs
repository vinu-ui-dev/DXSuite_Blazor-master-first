using System.Reflection;
using DXSuite.Director.Entities;
using DXSuite.Director.Services;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace DXSuite.Director.Tests;

/// <summary>
/// The audit section 9.8 asks for, as a test rather than a checklist.
///
/// Every secret this flavour introduced is protected at rest, and the naming convention is what
/// makes that checkable: a column holding a secret ends in "Protected", so a new one that does not
/// is visible in review rather than only in a breach.
/// </summary>
public class ProtectedColumnsTests
{
    private static readonly ProvisioningSecretProtector Protector =
        new(new EphemeralDataProtectionProvider());

    [Theory]
    // Section 9.8's list, one entry per bullet.
    [InlineData(typeof(TblDevice), "DwsPasswordProtected")]
    [InlineData(typeof(TblProvisioningPackage), "BootstrapDwsPasswordProtected")]
    [InlineData(typeof(TblProvisioningPackage), "WifiPskProtected")]
    [InlineData(typeof(TblDevicePairingRequest), "DeliveryTokenProtected")]
    public void EverySecretColumnExistsAndIsNamedForWhatItHolds(Type entity, string propertyName)
    {
        var property = entity.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(property);
        Assert.Equal(typeof(string), Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType);
    }

    [Theory]
    [InlineData(typeof(TblProvisioningPackage))]
    [InlineData(typeof(TblProvisioningAuthorization))]
    [InlineData(typeof(TblFirmwareImage))]
    public void NoEntityCarriesAPlainlyNamedSecret(Type entity)
    {
        // Catches the next column called Password, Psk or Secret that nobody protected.
        var suspects = entity.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string) || p.PropertyType == typeof(string))
            .Where(p => (p.Name.Contains("Password", StringComparison.OrdinalIgnoreCase)
                      || p.Name.Contains("Psk", StringComparison.OrdinalIgnoreCase)
                      || p.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase))
                     && !p.Name.EndsWith("Protected", StringComparison.Ordinal))
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(suspects);
    }

    [Fact]
    public void AProtectedValueDoesNotContainItsPlaintext()
    {
        var cipher = Protector.Encrypt("bootstrap-password");

        Assert.NotNull(cipher);
        Assert.DoesNotContain("bootstrap-password", cipher);
        Assert.StartsWith("ENC:", cipher);
    }

    [Fact]
    public void ARoundTripReturnsTheOriginal()
    {
        Assert.Equal("bootstrap-password", Protector.Decrypt(Protector.Encrypt("bootstrap-password")));
    }

    [Theory]
    [InlineData("plaintext-left-by-a-bug")]
    [InlineData("ENC:not-really-ciphertext")]
    public void AValueThatWillNotUnprotectIsNullAndNeverPassedThrough(string stored)
    {
        // The deliberate difference from SmtpCredentialProtector, which tolerates legacy
        // plaintext: every column this guards was created protected, so an unprefixed value is
        // corruption rather than history and must not be handed back as a secret.
        Assert.Null(Protector.Decrypt(stored));
    }

    [Fact]
    public void APurposeStringKeepsTheTwoProtectorsApart()
    {
        // A value protected for SMTP must not unprotect as a provisioning secret, so revoking one
        // purpose cannot reach the other.
        var provider = new EphemeralDataProtectionProvider();
        var smtp = new SmtpCredentialProtector(provider);
        var provisioning = new ProvisioningSecretProtector(provider);

        Assert.Null(provisioning.Decrypt(smtp.Encrypt("shared-value")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AnAbsentSecretStaysAbsentRatherThanBecomingCiphertext(string? value)
    {
        Assert.Equal(value, Protector.Encrypt(value));
        Assert.Equal(value, Protector.Decrypt(value));
    }
}
