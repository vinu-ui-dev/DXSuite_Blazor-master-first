# Audit Log

Review a chronological record of platform-level actions, including elevation requests, approvals, and the access operators make to tenants.

## Prerequisites

You must be a **Platform Operator** or hold the **Auditor** role. Auditors can read the audit log across all tenants but have no write access.

## Steps

1. In the left navigation, open **Platform → Audit Log**.
2. Review the entries. Each row shows:
    - **Time (UTC)** — when the event occurred.
    - **Event** — the type of action (for example, an elevation being requested, approved, or used).
    - **Tenant** — the tenant the event relates to, if any.
    - **User** — a short identifier for the operator involved.
    - **Reason** — the reason or ticket context recorded with the event.
3. To narrow the list, type into **Filter by tenant** to show only events for a matching tenant.

## Result

You see the most recent platform events, newest first. The page loads a capped number of the latest entries, so use the tenant filter to focus on a specific organization.

!!! info "Where else audit events appear"
    A tenant's detail page (**Platform → Tenants → View**) shows the **Recent Audit Events** for just that tenant, which is useful when investigating a single organization.

## Tips

- Elevation activity is recorded automatically: requesting, approving, and every use of a live elevation against a tenant each write their own entry. See [Platform Elevation](elevation.md) for how that flow works.
- Times are shown in UTC to keep records consistent across regions.

## What's next

- [Platform Elevation](elevation.md)
- [Tenants](tenants.md)
