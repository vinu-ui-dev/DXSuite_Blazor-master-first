using DXSuite.Design.Entities;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Design.Data;

public static class MaterialsSeed
{
    private record ItemDef(string Name, string? ModelNumber, decimal DefaultCost, decimal DefaultSellPrice);

    private static readonly (string CategoryName, int SortOrder, ItemDef[] Items)[] DefaultData =
    [
        (
            "Bulk Cabling & Connectors", 10,
            [
                new("Network Cable Plenum CAT6 (per foot)",    "CABLE : CAT6-P",      0.45m,  0.90m),
                new("Network Cable Non-Plenum CAT6 (per foot)","CABLE : CAT6-NP",     0.25m,  0.50m),
                new("CAT6A Plenum Cable (per foot)",           "CABLE : CAT6A-P",     0.75m,  1.50m),
                new("CAT6 Connector",                          "CONNECTOR : CAT6",    0.75m,  1.50m),
                new("CAT6 Keystone Jack",                      "JACK : CAT6",         3.00m,  6.00m),
                new("RG6 Coax Cable (per foot)",               "CABLE : RG6",         0.35m,  0.70m),
                new("RG6 F-Connector",                         "CONNECTOR : RG6-F",   0.50m,  1.00m),
                new("Speaker Wire 16AWG (per foot)",           "CABLE : SPK-16",      0.18m,  0.36m),
                new("Speaker Wire 14AWG (per foot)",           "CABLE : SPK-14",      0.25m,  0.50m),
                new("Control Cable 22AWG 2-Conductor (per ft)","CABLE : CTRL-22",     0.20m,  0.40m),
                new("RS-232 Bulk Cable (per foot)",            "CABLE : RS232",       0.30m,  0.60m),
                new("XLR Bulk Cable (per foot)",               "CABLE : XLR",         0.40m,  0.80m),
                new("Fiber OM3 Multimode (per foot)",          "CABLE : OM3",         0.80m,  1.60m),
                new("HDMI Bulk Cable (per foot)",              "CABLE : HDMI-BULK",   1.20m,  2.40m),
                new("Velcro / Hook-and-Loop (per foot)",       "MISC : VELCRO",       0.30m,  0.60m),
                new("Cable Tie Pack (100ct)",                  "MISC : CABLETIE",     3.00m,  6.00m),
                new("Heat Shrink Tubing Pack",                 "MISC : HEATSHRINK",   4.00m,  8.00m),
            ]
        ),
        (
            "Premade Cables", 20,
            [
                new("HDMI Cable 3ft",              "CABLE : HDMI-3",    5.00m,  10.00m),
                new("HDMI Cable 6ft",              "CABLE : HDMI-6",    7.00m,  14.00m),
                new("HDMI Cable 10ft",             "CABLE : HDMI-10",  10.00m,  20.00m),
                new("HDMI Cable 15ft",             "CABLE : HDMI-15",  15.00m,  30.00m),
                new("HDMI Cable 25ft",             "CABLE : HDMI-25",  25.00m,  50.00m),
                new("DisplayPort Cable 6ft",       "CABLE : DP-6",      8.00m,  16.00m),
                new("USB-A to USB-B Cable 6ft",    "CABLE : USB-AB-6",  5.00m,  10.00m),
                new("USB-C Cable 6ft",             "CABLE : USBC-6",    8.00m,  16.00m),
                new("USB-A Extension Cable 10ft",  "CABLE : USB-EXT",   7.00m,  14.00m),
                new("3.5mm Audio Cable 6ft",       "CABLE : AUDIO-35",  4.00m,   8.00m),
                new("3.5mm to RCA Stereo 6ft",     "CABLE : AUDIO-RCA", 5.00m,  10.00m),
                new("XLR Cable 10ft",              "CABLE : XLR-10",    8.00m,  16.00m),
                new("XLR Cable 25ft",              "CABLE : XLR-25",   12.00m,  24.00m),
                new("NL2 Speaker Cable 10ft",      "CABLE : NL2-10",   10.00m,  20.00m),
                new("NL4 Speaker Cable 10ft",      "CABLE : NL4-10",   12.00m,  24.00m),
                new("RJ45 Patch Cable 1ft",        "CABLE : RJ45-1",    2.00m,   4.00m),
                new("RJ45 Patch Cable 3ft",        "CABLE : RJ45-3",    3.00m,   6.00m),
                new("RJ45 Patch Cable 6ft",        "CABLE : RJ45-6",    4.00m,   8.00m),
                new("RJ45 Patch Cable 10ft",       "CABLE : RJ45-10",   5.00m,  10.00m),
                new("Fiber LC-LC OM3 3ft",         "CABLE : FIBER-LC3", 8.00m,  16.00m),
                new("IEC C13 Power Cable 6ft",     "CABLE : IEC-6",     5.00m,  10.00m),
                new("DB9 Serial Cable 6ft",        "CABLE : DB9-6",     6.00m,  12.00m),
            ]
        ),
        (
            "Installation Materials", 30,
            [
                new("Single Gang Low-Voltage Mud Ring",  "PLATE : 1G-MUD",      1.50m,   3.00m),
                new("Double Gang Low-Voltage Mud Ring",  "PLATE : 2G-MUD",      2.00m,   4.00m),
                new("Single Gang Decora Faceplate",      "PLATE : 1G-DEC",      2.00m,   4.00m),
                new("Double Gang Decora Faceplate",      "PLATE : 2G-DEC",      3.00m,   6.00m),
                new("Grommet Plate 2\" Round",           "PLATE : GROMMET-2",   4.00m,   8.00m),
                new("Grommet Plate 4\" Round",           "PLATE : GROMMET-4",   6.00m,  12.00m),
                new("Surface Box Single Gang",           "BOX : 1G-SURF",       5.00m,  10.00m),
                new("Wire Loom 1/2\" Split (per foot)",  "MISC : LOOM-50",      0.50m,   1.00m),
                new("Wire Loom 1\" Split (per foot)",    "MISC : LOOM-100",     0.75m,   1.50m),
                new("J-Hook 1-1/2\" (Box of 25)",        "MISC : JHOOK",       12.00m,  24.00m),
                new("Bridle Ring 1-1/2\" (Box of 25)",   "MISC : BRIDLE",      10.00m,  20.00m),
                new("Wiremold Raceway 5ft",              "MISC : WIREMOLD-5",  15.00m,  30.00m),
                new("3M Double-Sided Foam Tape",         "MISC : 3M-FOAM",      5.00m,  10.00m),
                new("Low-Voltage Nail Plate (Pack 10)",  "MISC : NAILPLATE",    8.00m,  16.00m),
            ]
        ),
        (
            "Rack Parts", 40,
            [
                new("Middle Atlantic Blank Panel 1U",    "RACK : BLANK-1U",     8.00m,  16.00m),
                new("Middle Atlantic Blank Panel 2U",    "RACK : BLANK-2U",    12.00m,  24.00m),
                new("Middle Atlantic Vented Blank 1U",   "RACK : VBLANK-1U",   10.00m,  20.00m),
                new("Middle Atlantic Sliding Shelf 1U",  "RACK : SHELF-SLD",   65.00m, 130.00m),
                new("Middle Atlantic Fixed Shelf 1U",    "RACK : SHELF-FXD",   40.00m,  80.00m),
                new("Middle Atlantic Lace Bar 1U",       "RACK : LACEBAR-1U",  15.00m,  30.00m),
                new("Middle Atlantic Lace Bar 2U",       "RACK : LACEBAR-2U",  20.00m,  40.00m),
                new("Middle Atlantic Drawer 2U",         "RACK : DRAWER-2U",  120.00m, 240.00m),
                new("Middle Atlantic Cable Manager 1U",  "RACK : CABLEMGR",    25.00m,  50.00m),
                new("Rack Screws & Cage Nuts (Pack 50)", "RACK : SCREWS-50",    8.00m,  16.00m),
                new("Rack Fan Panel 1U 2-Fan",           "RACK : FAN-1U",      45.00m,  90.00m),
            ]
        ),
    ];

    /// <summary>
    /// Idempotent seed: inserts the default materials categories and items for a tenant
    /// if no categories exist yet for that customer. Safe to call on every startup.
    /// </summary>
    public static async Task SeedAsync(DesignDbContext db, string customerId, CancellationToken ct = default)
    {
        var anyExist = await db.TblDesignMaterialsCategories
            .AsNoTracking()
            .AnyAsync(c => c.CustomerId == customerId, ct);

        if (anyExist) return;

        foreach (var (categoryName, sortOrder, items) in DefaultData)
        {
            var category = new TblDesignMaterialsCategory
            {
                CustomerId = customerId,
                Name = categoryName,
                SortOrder = sortOrder,
                IsActive = true,
            };
            db.TblDesignMaterialsCategories.Add(category);
            await db.SaveChangesAsync(ct); // get real CategoryId

            var itemEntities = items.Select((item, i) => new TblDesignMaterialsItem
            {
                CustomerId = customerId,
                CategoryId = category.CategoryId,
                Name = item.Name,
                ModelNumber = item.ModelNumber,
                DefaultCost = item.DefaultCost,
                DefaultSellPrice = item.DefaultSellPrice,
                SortOrder = (i + 1) * 10,
                IsActive = true,
            });
            db.TblDesignMaterialsItems.AddRange(itemEntities);
        }

        await db.SaveChangesAsync(ct);
    }
}
