# Connector Overview

Connectors let DXSuite pull devices and content from an external cloud platform into Director, so you can manage them alongside your own displays.

## What a connector does

A connector links DXSuite to a third-party cloud CMS or device-management platform (such as Omnivex Ink). Once configured, DXSuite can:

- **Import devices** from the platform, which then appear in Device Management.
- **Import content folders** from the platform, which then appear as Content Channels.
- **Report device status** back into DXSuite so cloud-managed displays sit next to your own.

Connectors are managed on the **Cloud Connectors** tab of the **Configuration** area. Each tenant keeps its own connectors, and you can configure more than one.

## Prerequisites

- Permission to open **Configuration**.
- An account on the external platform, its base URL, and an API key with the access you need.

## Steps: add a connector

1. Open **Configuration** and select the **Cloud Connectors** tab.
2. Select **+ Add Connector**.
3. Choose the platform from the **Platform** list. The **Display Name** fills in automatically — edit it to something recognizable.
4. Enter the **Base URL** and **API Key** for the platform.
5. If the platform supports content import, choose a **Content Mode** and tick the resources to bring in under **Import Resources**.
6. Optionally set a **Credential Expiration** date so DXSuite can warn you before the API key lapses.
7. Select **SAVE**.

## Result

The connector appears as a card showing its platform, status, base URL, and linked device count. Use **TEST CONNECTION** to confirm the credentials work, then **SYNC INVENTORY** to import devices and folders.

!!! info "Re-sync any time"
    Running **SYNC INVENTORY** again refreshes the imported devices and folders. Items removed on the external platform are cleared from DXSuite on the next sync.

## Tips

- Use **Enable** and **Disable** to pause a connector without losing its configuration.
- **Delete** removes the connector. You are prompted first, with the option to also remove its imported devices and Content Channel folders.
- A connector whose credential has expired, or expires within two weeks, is flagged on its card.

## What's next

- [Omnivex Ink Integration](omnivex.md)
- [Content Metadata](../configuration/metadata.md)
- [Devices](../infrastructure/devices.md)
