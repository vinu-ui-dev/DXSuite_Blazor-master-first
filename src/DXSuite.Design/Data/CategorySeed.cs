using DXSuite.Design.Entities;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Design.Data;

public static class CategorySeed
{
    private static readonly string[] DefaultCategories =
    [
        "PC",
        "Control Processor",
        "Audio DSP",
        "User Interface",
        "Display",
        "Projector",
        "Camera",
        "Microphone",
        "Speaker",
        "Wireless Presentation",
        "Video Codec",
        "AV Switcher",
        "AV Expansion",
        "Network Switch",
        "Recording System",
        "Sensor",
        "AV Streaming End Point",
        "Wireless Mic Receiver",
        "Amplifier",
        "Control Expansion",
        "USB Expansion",
        "Digital Whiteboard",
        "Projector Screen",
        "Sound Masking",
        "Conferencing Appliance",
        "Signage Appliance",
        "Media Player",
        "Room Scheduler",
        "Video Wall",
        "LED Video Wall",
        "Video Processor",
        "UC System Kit",
        "Equipment Rack",
        "AV Furniture",
        "Audio Mixing Console",
        "Camera Controller",
        "Lighting Controller",
        "Lighting Fixture",
        "Blind & Shade Controller",
        "Blind & Shade Fixture",
        "Server",
        "Docking/Charging Station",
        "Wireless Access Point",
        "AV Chassis",
        "Wireless Presentation Interface",
        "Signage Server",
        "Power Management",
        "Cell Phone",
        "Laptop Computer",
        "Printer",
        "Network Router",
        "Software License",
        "Virtual Machine",
        "Input Plate",
        "Cable Box",
        "Surge Protector / UPS",
        "Cable & Connectivity",
        "Intercom System",
        "Streaming Encoder",
        "KVM Switch",
    ];

    /// <summary>
    /// Idempotent seed: inserts default categories for a tenant if none exist yet.
    /// Safe to call on every startup for a given CustomerId.
    /// </summary>
    public static async Task SeedAsync(DesignDbContext db, string customerId, CancellationToken ct = default)
    {
        var anyExist = await db.TblDesignCategories
            .AsNoTracking()
            .AnyAsync(c => c.CustomerId == customerId, ct);

        if (anyExist) return;

        var categories = DefaultCategories
            .Select((name, i) => new TblDesignCategory
            {
                CustomerId = customerId,
                Name = name,
                SortOrder = (i + 1) * 10,
            })
            .ToList();

        db.TblDesignCategories.AddRange(categories);
        await db.SaveChangesAsync(ct);
    }
}
