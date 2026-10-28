# Displays

A display is a single screen or content surface that belongs to a [location](locations.md) and holds its own schedule of content.

Displays are the middle level of the DXSuite inventory. A location of a display-driven type (such as **Card**) can hold one or more displays, and each display is driven by a [device](devices.md) — the player that connects to DXSuite and renders content on the physical screen.

## Prerequisites

- A location of a display-driven type must already exist. See [Locations](locations.md).
- You must have permission to manage locations and displays.

## Steps

To add a display to a location:

1. Open **Configuration** and select the **Locations** tab.
2. Find the location and select **Edit**.
3. In the **Displays** section, select **+ Add Display**.
4. Enter a **Display Name** and its **Width** and **Height** in pixels.
5. Select **Add**, then save the location.

To adjust an existing display:

1. In the **Location Management** list, expand the location row to reveal its displays.
2. Select **Edit** on the display. The **Edit Display** panel opens.
3. Review or change:
    - **Resolution** — the display's width and height.
    - **Constraint** — an optional content group or asset library that limits which assets appear in this display's schedule.
    - **Workflow** — **Schedule** for a normal timeline, or **Status Selector** for button-driven content.
4. Select **Save**.

## Result

The display appears nested under its location and can be scheduled. Select the display name to open its schedule and manage content.

!!! note
    A display's resolution should match the physical screen it will drive so that content is sized correctly.

## Tips

- A constraint keeps a display's content on-spec by restricting it to assets of a matching resolution.
- A display must have no assigned device before it can be deleted.

## What's next

- [Devices](devices.md)
- [Screenshots](../slc/screenshots.md)
