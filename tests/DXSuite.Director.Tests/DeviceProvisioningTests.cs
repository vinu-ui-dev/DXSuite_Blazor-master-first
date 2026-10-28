using DXSuite.Director.Entities;
using DXSuite.Director.Services;

namespace DXSuite.Director.Tests;

public class DeviceProvisioningTests
{
    [Fact]
    public void Seed_writes_both_zone_columns()
    {
        var device = DeviceProvisioning.Seed(new TblDevice());

        Assert.False(string.IsNullOrWhiteSpace(device.TimeZone));
        Assert.Equal("Seeded", device.TimeZoneSource);
    }

    // The eight tblDisplay rows holding "Eastern Standard Time" are what this guards against: the
    // host identifier is a Windows one and writing it verbatim has already happened once.
    [Fact]
    public void Seed_never_writes_the_hosts_windows_identifier()
    {
        var device = DeviceProvisioning.Seed(new TblDevice());

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var ianaId))
        {
            Assert.Equal(ianaId, device.TimeZone);
            Assert.NotEqual(TimeZoneInfo.Local.Id, device.TimeZone);
        }
        else
        {
            Assert.Equal(DeviceProvisioning.UnresolvedTimeZone, device.TimeZone);
        }
    }

    // The sentinel is load-bearing only while it fails to resolve. A value that started resolving
    // would turn a loud missing-backfill into silently-wrong-but-plausible content.
    [Fact]
    public void The_sentinel_does_not_resolve_to_a_time_zone()
    {
        Assert.Throws<TimeZoneNotFoundException>(
            () => TimeZoneInfo.FindSystemTimeZoneById(DeviceProvisioning.UnresolvedTimeZone));
    }
}
