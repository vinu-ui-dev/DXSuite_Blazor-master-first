# Scheduling and Media

Schedule what plays on a display: set its always-on default content and add timed schedule entries that take over on chosen dates, times, and days of the week.

## Prerequisites

- Access to a location and at least one display within it. Open a display's schedule from its location page — scheduling is reached from within a display, not from the top navigation.

## Steps

1. From a location, open the display you want to schedule. The **MANAGE SCHEDULE** page opens.
2. Review the status banner at the top. It shows whether the display is currently showing override, scheduled, or default content, along with a preview.
3. To add or replace content, select **NEW SCHEDULE ENTRY**. The **Schedule Editor** opens.
4. Add the media you want to use with the buttons in **Add Media Content**:
    - **ADD MEDIA +** — upload an image or video.
    - **ADD WEB +** — add a web page URL.
    - **ADD STREAM +** — add a live stream URL.
    - **CREATE MEDIA +** — build an image in the Media Creator (see below).
    - **CREATE TEMPLATE +** — generate content from a template.
5. Under **Available Assets**, drag the assets you want onto the **Schedule Content** area. Each item plays in the order added.
6. For each content item, use **Edit Duration** to set how long it stays on screen (where applicable).
7. Under **Schedule Configuration**, choose how the entry applies:
    - Tick **Default Content** to make this the display's always-on content instead of a timed entry.
    - For a timed entry, set the **Start** and **End** date and time. Leave a date blank for no limit; leave a time blank for all-day.
    - Under **Active Days**, tick the days it should run. Leave all unticked to run every day.
8. Select **SAVE SCHEDULE ENTRY** (or **SAVE SCHEDULE EDIT** when editing).

## Result

The entry appears under **Current Schedule Entries**, showing its content, date range, time range, and active days. The display shows scheduled content whenever an entry's date, time, and day conditions all match; otherwise it falls back to the default content.

!!! info "Using the Media Creator"
    **CREATE MEDIA +** opens the Media Creator, a canvas editor for building an image sized to the display. Add text and elements, position them, then generate the image as a new asset that appears in **Available Assets**.

## Tips

- Edit an existing entry with its **Edit** button, or remove it with **Delete**.
- If content approval is enabled for the location, newly added assets may show an **Awaiting Approval** or **Declined** overlay and cannot be scheduled until approved. See [Approvals](approvals.md).
- Use **Edit Name** near the top of the page to rename the display.

## What's next

- [Approvals](approvals.md)
- [Overrides](overrides.md)
- [Displays](../infrastructure/displays.md)
