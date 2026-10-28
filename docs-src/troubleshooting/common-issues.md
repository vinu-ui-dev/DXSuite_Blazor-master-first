# Common Issues

Symptoms you may run into while using DXSuite, the most likely cause of each, and how to resolve them.

## A user can't see a location, page, or module

**Symptom** — A user reports that an expected work area, page, or set of locations is missing from their navigation.

**Cause** — Access in DXSuite is shaped by the user's role and by their location access. A missing page usually means the role does not grant it; missing locations usually mean the user is scoped to a subset of locations. A whole module may simply not be licensed for the current tenant.

**Resolution**

1. Open **Settings → Users** and confirm the user's **Current Role**.
2. If the role is correct but a page is still missing, open **Settings → Roles** and confirm that role grants the relevant tab, page, or feature permission.
3. Confirm the user has access to the expected locations.
4. If an entire module is absent, verify it is licensed for the tenant. See [Tenants and Locations](../getting-started/tenants-and-locations.md).

## A display shows no content

**Symptom** — A screen is powered on and reachable but shows nothing, or shows an old playlist.

**Cause** — No content is scheduled or approved for that display right now, or the device has not yet pulled its latest playlist.

**Resolution**

1. Confirm the display has content assigned and that any schedule covers the current time.
2. Check that the content is approved (see the pending-approval scenario below).
3. Confirm the device is online (see the offline-device scenario below) so it can receive updates.

## Content that was submitted isn't live

**Symptom** — New or edited content does not appear on displays even though it was uploaded.

**Cause** — The content is awaiting approval. Until it is approved, it is not published to displays.

**Resolution**

1. Have an approver review and approve the pending content.
2. Confirm the submitter's role is expected to route through approval — only roles with the approval-bypass feature permission skip moderation.
3. After approval, allow devices a short time to pull the update.

## Web content shows a "WWW" placeholder instead of a preview

**Symptom** — A web asset that previously showed a page preview now shows the blue **WWW**
placeholder tile, or **Refresh Preview** reports that no preview could be captured.

**Cause** — The server no longer previews addresses on its own network by default. Preview URLs
that point at an internal host (anything on `10.x`, `172.16–31.x`, `192.168.x`, `localhost`, or a
link-local address) are refused, because on a shared server that capability could be used to reach
systems the person entering the URL was never meant to see. Public URLs are unaffected.

**Resolution**

1. Confirm the asset's URL. If it is reachable from the public internet, the block is not the cause —
   check that the site is up and that the server can reach it.
2. For a single-tenant on-premises install whose signage content is hosted on the intranet, allow it
   in `appsettings.json`. Either permit internal addresses wholesale:

    ```json
    "WebPreview": { "AllowPrivateNetworks": true }
    ```

    or list only the hosts and ranges you use, which is preferred:

    ```json
    "WebPreview": {
      "OutboundAllowList": [ "signage.corp.local", "*.internal.example", "10.20.0.0/16" ]
    }
    ```

3. Restart the application. Missing thumbnails regenerate on the next startup pass.

The server log names the blocked URL for each refused asset, which is the quickest way to build the
allow list. Note that thumbnails are a display convenience only — blocked previews never stop the
content itself from playing on a device.

## A device appears offline

**Symptom** — A device shows as offline in DXSuite and is not updating.

**Cause** — The device has lost network connectivity or its agent is not running or checking in.

**Resolution**

1. Confirm the device has power and a working network connection.
2. Restart the device or its player agent and wait for it to check in again.
3. If it remains offline, review its recent status on its detail page to see when it last reported.

## Signed out unexpectedly / session expired

**Symptom** — A user is returned to the sign-in screen mid-task, or is prompted to sign in again after switching tenants.

**Cause** — Sessions are secured per tenant and time out after a period of inactivity. Switching tenants starts a new secure session.

**Resolution**

1. Sign in again to resume.
2. Note that after switching tenants you may be asked to re-authenticate — this is expected. See [First Login](../getting-started/first-login.md).

## A support operator gets an "elevation required" error

**Symptom** — A platform operator opening a tenant they don't belong to sees an *elevation required* message, or cannot save changes.

**Cause** — Operators have no standing access to tenant data. Viewing requires an approved live elevation; changing data additionally requires a **Read + Write** elevation.

**Resolution**

1. Request access from **Platform → Elevation** and have a second operator approve it.
2. If reads work but writes are blocked, request a **Read + Write (TenantSupportWrite)** elevation. See [Platform Elevation](../administration/elevation.md).

## The Platform area is missing

**Symptom** — An administrator cannot find the **Platform** navigation group (Tenants, Users, Audit Log, Elevation).

**Cause** — The Platform area appears only in multi-tenant deployments where a platform database is configured, and only for Platform Operators. Standalone and on-premises installations do not show it.

**Resolution**

1. Confirm the account is a Platform Operator.
2. Confirm the deployment is multi-tenant with the platform database configured. If it is a standalone install, the Platform area is intentionally absent.
