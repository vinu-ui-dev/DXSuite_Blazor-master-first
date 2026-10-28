using DXSuite.Director.Services;

namespace DXSuite.Director.Tests;

/// <summary>
/// Group metadata is multi-value, but a group's video wall assignment is one value. A second row must
/// fail loudly rather than silently change which walls an airline is schedulable on.
/// </summary>
public class GroupVideoWallAssignmentsTests
{
    [Fact]
    public void A_group_with_no_assignment_reads_as_none()
        => Assert.Null(GroupVideoWallAssignments.SingleValue(7, []));

    [Fact]
    public void A_group_with_one_assignment_reads_it()
        => Assert.Equal("12,14", GroupVideoWallAssignments.SingleValue(7, ["12,14"]));

    [Fact]
    public void A_group_with_two_assignments_throws_naming_the_group()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => GroupVideoWallAssignments.SingleValue(7, ["all", "12"]));

        Assert.Contains("User group 7", ex.Message);
    }
}
