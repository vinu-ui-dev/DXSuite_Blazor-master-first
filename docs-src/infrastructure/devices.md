# Devices

A device is a media player that connects to DXSuite, reports its status, and drives a [display](displays.md) by rendering that display's scheduled content on the physical screen.

Devices are the bottom level of the DXSuite inventory. Each device registers with Director, sends a regular heartbeat, and — once assigned to a display — plays the content scheduled for it. A device must be paired before it can connect.

## Prerequisites

- The player must be able to reach your DXSuite server.
- You must have permission to approve device pairing and manage devices.

## Pair a device

1. Start the player. It shows a **6-digit pairing code** and appears under pending requests.
2. In **Configuration**, open **Device Pairing**.
3. Enter the code in **Approve Device** and select **Approve**, or approve the matching entry under **Pending Requests**.
4. The device receives its token on its next poll and begins connecting.

!!! warning
    Selecting **Deny** on a pairing request blocklists the device. Use **Unblock** under **Denied Devices** to let it request pairing again.

## Monitor devices

1. Open **Configuration** and select the **Devices** tab to open **Device Management**.
2. Each row shows the device and its health, including:
    - **Status** — online or offline, based on whether a recent heartbeat was received.
    - **Heartbeat** — how long ago the device last checked in.
    - **Content** — content health, shown as **Healthy**, **Unresponsive**, or **Failed**.
    - Identifying details such as **Device ID**, **Model**, **Serial**, and **IP Address**.
3. Use the search box to filter, and the **Columns** control to choose and reorder the columns shown.

## Result

A paired, online device that is assigned to a display plays that display's content. Its status and content health appear in **Device Management**, and you can view its live output under [Screenshots](../slc/screenshots.md).

## Tips

- Select a device's gear icon to open its settings, where you can enable console logging or flush its local media cache.
- Click a device's **Name** or **Description** cell to edit it inline.
- Active connections are managed under **Device Pairing**, where a token can be revoked, reactivated, or deleted.

## What's next

- [Screenshots](../slc/screenshots.md)
- [Pulse](../pulse/overview.md)
