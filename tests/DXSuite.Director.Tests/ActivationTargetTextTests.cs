using DXSuite.Director.Entities;
using DXSuite.Director.Services.Messaging;

namespace DXSuite.Director.Tests;

// Clause ids refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>The "Target" text of an Activation, read from its frozen screen rows (ZONE-06, ZONE-16).</summary>
public sealed class ActivationTargetTextTests
{
    [Fact]
    public void A_one_display_send_is_named_by_the_display_name_frozen_at_send()
    {
        // No Display is loaded or exists: the display may since have been hard-deleted.
        var text = ActivationTargetText.Describe([
            new TblMessageActivationTarget { DisplayId = 12, DisplayName = "Gate B12", LocationName = "Terminal 1" },
        ]);

        Assert.Equal("Gate B12", text);
    }

    [Fact]
    public void A_Display_Zone_send_is_named_by_its_zones()
    {
        var gates = new TblZone { ZoneId = 2, ZoneName = "Gates B", SortOrder = 2 };
        var lounges = new TblZone { ZoneId = 1, ZoneName = "Lounges", SortOrder = 1 };

        var text = ActivationTargetText.Describe([
            new TblMessageActivationTarget { DisplayId = 12, ZoneId = 2, Zone = gates, DisplayName = "Gate B12", LocationName = "T1" },
            new TblMessageActivationTarget { DisplayId = 14, ZoneId = 2, Zone = gates, DisplayName = "Gate B14", LocationName = "T1" },
            new TblMessageActivationTarget { DisplayId = 30, ZoneId = 1, Zone = lounges, DisplayName = "Lounge", LocationName = "T1" },
        ]);

        Assert.Equal("Lounges, Gates B", text);
    }
}
