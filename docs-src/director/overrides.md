# Overrides

An override pushes urgent or special-event content to many devices or displays at once, taking over whatever they are currently showing until you turn it off.

## Prerequisites

- Permission to access the Overrides page. The **Overrides** menu item appears only when the feature is enabled for your site and you are authorized.

## Steps

### Create an override

1. Select **Overrides** in the Director navigation to open **OVERRIDE CONTROL**.
2. Select **+ CREATE OVERRIDE**. The override editor opens.
3. Under **Basic Information**, enter an **Override Name** (required) and an optional **Description**.
4. Choose the targets. In **Target Selection**, switch between **Displays** and **Devices**, then search and tick the items to include. Use **Select All Filtered**, **Unselect All**, or **Show Selected** to manage the list.
5. Assign content by resolution. Select **Detect Resolutions** to list the resolutions of your selected targets, then upload an image or video for each. Use **+ Add Custom Resolution** to add one manually.
6. (Optional) Under **Schedule Settings**, tick **Enable Scheduling** and set the **Start Date**, **End Date**, **Start Time**, **End Time**, and **Days of Week**. Shortcut buttons set **All**, **Weekdays**, **Weekends**, or **Clear**.
7. Select **SAVE**.

### Activate and manage overrides

1. On **OVERRIDE CONTROL**, each override appears as a card in the **Override Priority List** with its device count and status.
2. Use the toggle switch on a card to activate or deactivate it manually.
3. Drag cards by the handle to reorder them. Items higher in the list take priority when more than one override is active for the same device.
4. Use the edit and delete icons to change or remove an override.

## Result

While an override is active, its assigned content plays on the targeted displays and devices at the matching resolution. The card shows **ACTIVE**, and the source of activation is noted as **(Manual)** or **(Schedule)**.

!!! warning "Overrides interrupt normal content"
    An active override replaces scheduled and default content on its targets. Deactivate it (or let its schedule end) to return displays to their normal programming.

## Tips

- An override can be activated three ways: the manual toggle, a schedule you set, or an external trigger via the platform API.
- Deleting an override also removes its device and display associations and its content assignments.

## What's next

- [Scheduling and Media](scheduling.md)
- [Displays](../infrastructure/displays.md)
