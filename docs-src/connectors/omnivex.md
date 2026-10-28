# Omnivex Ink Integration

Connect DXSuite to an Omnivex Ink platform so its displays and content folders sync into Director as managed devices and Content Channels.

## Prerequisites

- Permission to open **Configuration**.
- The base URL of your Omnivex Ink environment.
- An Omnivex Ink API key with access to the displays and content you want to import.

## Steps: connect Omnivex Ink

1. Open **Configuration** and select the **Cloud Connectors** tab.
2. Select **+ Add Connector**.
3. Choose **Omnivex Ink** from the **Platform** list.
4. In **Display Name**, enter a name that identifies this environment (for example, the site or region).
5. Enter the **Base URL** of your Omnivex Ink environment and paste your **API Key**.
6. Under **Content Mode**, choose whether to import content, track device status, or both.
7. Under **Import Resources**, tick **Displays**, **Folders**, or both.
8. Optionally set a **Credential Expiration** date.
9. Select **SAVE**.

## Steps: verify and sync

1. On the connector card, select **TEST CONNECTION** and confirm it reports **Connected**.
2. Select **SYNC INVENTORY** to import from Omnivex Ink.
3. Review the result message showing how many displays and folders were brought in.

## Result

Omnivex Ink displays appear in Device Management, and imported folders appear as Content Channels you can schedule against. The card shows the last sync time and status each time you run a sync.

!!! warning "Test before syncing"
    If **TEST CONNECTION** reports **Failed**, check the base URL and API key before syncing. A failed sync is recorded on the card so you can see what went wrong.

## Tips

- If folders do not import, confirm the API key includes access to content, then sync again.
- Re-run **SYNC INVENTORY** after adding displays or folders in Omnivex Ink to bring them into DXSuite.

## What's next

- [Connector Overview](overview.md)
- [Content Metadata](../configuration/metadata.md)
- [Scheduling and Media](../director/scheduling.md)
