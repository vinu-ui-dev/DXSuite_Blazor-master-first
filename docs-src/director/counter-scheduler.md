# Counter Scheduler

The Counter Scheduler assigns airlines to ticket-counter positions on a video wall across the day, and sets the background, mask, and logo content each airline shows at its counters.

## Prerequisites

- The **Counter Scheduler** feature must be enabled for your site, and you must have permission to access it.
- A Video Wall location configured with counter groups that allow airline allocation.

## Steps

### Schedule airlines on counters

1. Select **Counter Scheduler** in the Director navigation. If prompted, choose a Video Wall location from the picker.
2. Use the date controls at the top (the arrows or the date box) to move to the day you want to schedule.
3. On the **Counter Schedule** tab, select an open time slot on the calendar to create a schedule block. To change an existing block, select it; you can also drag it to move it or drag its edge to resize it.
4. In the block dialog, set:
    - **Airline Group** — the airline assigned to that counter time.
    - **Default Asset** (optional) — content that plays automatically when the block becomes active.
    - **All Day**, or a **Start** and **End** time.
    - **From date** / **To date** (optional) and **Recurs on** days. Leave all days unchecked to apply every day.
5. Select **SAVE**. If the block overlaps another, a warning appears; select **Save Anyway** to confirm, or adjust the times.

### Set an airline's counter content

1. Open the **Airline Content** tab.
2. For each airline card, set the layers you need:
    - **Counter Content** — the default content package for that airline's counters.
    - **Background Layer**, **Mask Layer**, and **Logo** — provide a web URL or upload a file, and choose a render mode where offered.
3. Select **SAVE** on the card.

## Result

Scheduled blocks appear on the calendar for the selected day, colored by airline. When a block is active, its airline (and any default asset) drives the assigned counter displays, using the content configured on the Airline Content tab.

!!! info "Which airlines appear"
    Only groups assigned to this video wall are listed as schedulable airlines. Assignments are configured per group in **Settings → Groups**. If an airline is missing, check its group assignment.

## Tips

- A block with no airline set is treated as **Unassigned**.
- Overlapping blocks on the same display are blocked when you move or resize them, and warned about when you save.

## What's next

- [Scheduling and Media](scheduling.md)
- [Locations](../infrastructure/locations.md)
