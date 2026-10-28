# Locations

A location is the top level of the DXSuite inventory: it represents a site or content area, holds its displays, and controls how content is created and delivered there.

DXSuite organizes your signage estate as a three-level hierarchy:

- **Location** — a site or content area (for example, a concourse, a lobby, or a shared media pool).
- **Display** — a screen or content surface that belongs to a location. See [Displays](displays.md).
- **Device** — the player that connects to DXSuite and drives a display. See [Devices](devices.md).

Locations are grouped into **Location Groups** for easier browsing, and each location has a **Location Type** that determines how it behaves.

## Prerequisites

- You must have access to the **Configuration** area and permission to manage locations.

## Steps

To create a location:

1. Open **Configuration** and select the **Locations** tab. The **Location Management** panel lists existing locations with their group, type, and access.
2. Select **+ Create Location**.
3. Enter a **Location Name**.
4. Choose a **Location Group**, or select **+ Create New Group** and enter a name for a new one.
5. Choose a **Location Type**. Each type is described on the card, for example:
    - **Playlist** — sequential media playback.
    - **Card** — multiple displays with scheduling.
    - **Layout** — compose multiple locations into zones.
    - **Asset Library** — a shared content pool for other locations.
6. Complete any type-specific settings, then save.

## Result

The new location appears in the **Location Management** list. Location types that drive screens can now hold [Displays](displays.md), and you can expand the row to see them.

## Tips

- Use the search box to filter the list by location name, group, or type.
- Select **Edit** on a location to rename it, set content-source and content-tool options, and (for display-driven types) add displays or content constraints.
- Select **Manage** in the Access column to control which users and schedules can reach the location.
- Deleting a location also removes its displays and their schedules, so confirm carefully.

## What's next

- [Displays](displays.md)
- [Devices](devices.md)
