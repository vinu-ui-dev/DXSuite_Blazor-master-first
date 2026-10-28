using DXSuite.Director.Services;
using static DXSuite.Director.Services.DeclinedContentCleanupService;

namespace DXSuite.Director.Tests;

/// <summary>
/// The hourly declined-content purge reads retention for a whole batch of locations at once, so one
/// location with an unexpected chain shape must not stop the purge everywhere.
/// </summary>
public class DeclinedContentRetentionTests
{
    [Fact]
    public void Each_location_takes_retention_from_its_default_chain()
    {
        var retention = RetentionDaysByLocation(
        [
            new DefaultChainRetention(LocationId: 22, ApprovalChainId: 5, DeclinedContentRetentionDays: 7),
            new DefaultChainRetention(LocationId: 23, ApprovalChainId: 6, DeclinedContentRetentionDays: 30),
        ]);

        Assert.Equal(7, retention[22]);
        Assert.Equal(30, retention[23]);
    }

    [Fact]
    public void A_duplicated_default_chain_resolves_to_the_oldest_instead_of_throwing()
    {
        var retention = RetentionDaysByLocation(
        [
            new DefaultChainRetention(LocationId: 22, ApprovalChainId: 41, DeclinedContentRetentionDays: 14),
            new DefaultChainRetention(LocationId: 22, ApprovalChainId: 5, DeclinedContentRetentionDays: 7),
        ]);

        Assert.Equal(7, Assert.Single(retention).Value);
    }

    [Fact]
    public void A_location_without_a_default_chain_is_absent_so_the_caller_default_applies()
        => Assert.Empty(RetentionDaysByLocation([]));
}
