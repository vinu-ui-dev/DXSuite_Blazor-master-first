# Screenshots

The Screenshots view shows live thumbnails captured from your players, so you can confirm at a glance what each screen is actually displaying.

Each tile is a recent snapshot from one [device](../infrastructure/devices.md). A colored border indicates whether the device has checked in recently, and you can open any tile to browse its snapshot history.

## Prerequisites

- Your players must be reporting screenshots to DXSuite.
- You must have access to the Screenshots view.

## Steps

1. Open the Screenshots view (**Display View**). Tiles load for each reporting player, and a **Player Count** shows the total.
2. Scan the borders to spot problems:
    - A **green** border means the device checked in recently.
    - A **red** border means the device has not checked in recently and may be offline.
3. Select a tile to open its **Snapshot History**.
4. In the history window, set the **From** and **To** dates and select **Search** to load snapshots for that period.
5. Use **Previous** and **Next** to step through the snapshots. Each is labelled with its capture time.

## Result

You can visually verify what each screen is showing and review how a screen looked over time to investigate a reported issue.

!!! info "Automatic refresh"
    The Screenshots grid refreshes on its own every few seconds, so the thumbnails stay current without reloading the page.

!!! note
    A snapshot search covers a limited time range (up to 24 hours per search). Narrow the **From** and **To** dates to find a specific moment.

## Tips

- A tile also links to the device's address, so you can open the player directly when it is reachable.
- Use the red-bordered tiles as a quick triage list, then confirm details in [Devices](../infrastructure/devices.md).

## What's next

- [Devices](../infrastructure/devices.md)
- [Pulse](../pulse/overview.md)
