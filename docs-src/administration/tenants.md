# Tenants

Create and manage the tenants (organizations) that share a multi-tenant DXSuite deployment.

## Prerequisites

- You must be a **Platform Operator**. Other users cannot see the Platform area.
- A tenant registry database must be configured for the deployment. On standalone or on-premises installations the Platform area is not shown, and provisioning is unavailable.

## View tenants

1. In the left navigation, open **Platform → Tenants**.
2. The list shows each tenant's **Tenant ID**, **Display Name**, **Subdomain**, and **Status** (**Active** or **Disabled**).
3. Select **View** on a row to open the tenant's detail page.

The detail page shows the tenant's core properties along with its **Users**, **Recent Elevations**, and **Recent Audit Events**.

## Steps: create a tenant

1. On the **Tenants** page, select **+ New Tenant**.
2. Complete the form:
    - **Tenant ID** — a unique slug (for example, `acme`).
    - **Display Name** — the organization's name as shown in DXSuite.
    - **Subdomain** — the subdomain used to reach the tenant.
    - **Customer Code** — your internal customer identifier.
    - **Director Connection String** — the connection to the tenant's Director database.
    - **Design Connection String** — optional; leave blank if Design is not licensed for this tenant.
3. Select **Create Tenant**.
4. In the **Confirm Privileged Action** dialog, select **Confirm**.

## Result

The new tenant is provisioned and you are taken to its detail page. Tenant ID, Display Name, Subdomain, and Customer Code are all required — if any is missing, DXSuite reports an error and no tenant is created.

## Tips

- To change a tenant's properties later, open its detail page and select **Edit**.
- A tenant only exposes the modules and features it is licensed for. See [Tenants and Locations](../getting-started/tenants-and-locations.md) for how tenants scope everything a user sees.

!!! warning "Connection strings point to real databases"
    The Director and Design connection strings determine which databases the tenant reads and writes. Double-check them before confirming — an incorrect value can leave the tenant unable to load its data.

## What's next

- [Users and Roles](users-and-roles.md)
- [Platform Elevation](elevation.md)
- [Audit Log](audit-log.md)
