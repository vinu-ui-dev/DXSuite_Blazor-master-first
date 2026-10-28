# Users and Roles

Manage who can sign in to DXSuite and what each person is allowed to do, using roles, groups, and — in multi-tenant deployments — platform-level operator accounts.

## Prerequisites

- To manage users and groups, you need access to the **Users** and **Groups** settings tabs.
- To create or edit **roles**, you must have the **Admin** role. Only administrators can manage roles.
- To manage **platform users** and their tenant memberships, you must be a **Platform Operator**.

## Roles

Every user is assigned one role, which controls the tabs, pages, and features they can reach. DXSuite ships with these built-in roles:

- **Admin** — full access to everything, including role management. Admin bypasses all permission checks.
- **Architect** — broad configuration access.
- **Contributor** — day-to-day content work.
- **New User** — minimal default access.

You can also create **custom roles**. A custom role inherits from a built-in **base role**, then adds or removes access through explicit grants for settings tabs, configuration tabs, and feature permissions (such as **Content Upload**, **Content Edit**, and **Content Delete**).

### Steps: create a custom role

1. Open **Settings → Roles**.
2. Select **Add Role**.
3. Enter a **Role Name** and **Description**, and choose a **Base Role**.
4. Check the **Settings Tabs**, **Configuration Tabs**, and **Feature Permissions** the role should grant.
5. Select **Save**.

!!! note "Content still respects location access"
    Content feature permissions are additionally limited by each user's location access level, so granting a permission does not override the locations a user is scoped to.

## Users

1. Open **Settings → Users**.
2. Search for and select a user to open their details.
3. From here you can:
    - Set the user's **Current Role** and select **Save**.
    - Add or remove **group** assignments.
    - Set a default **Landing Page**.
    - **Disable**/**Enable** or **Delete** the account, or **Reset Password** for local accounts.

Where local accounts are enabled, use **+ Create Local Account** to add a user with an email and password.

## Groups

Open **Settings → Groups** to create groups and manage their members. Groups let you organize users; directory-synced groups are managed by the sync process and are marked accordingly.

## Platform users (multi-tenant)

In multi-tenant deployments, **Platform → Users** lists platform-level accounts. For each user you can see whether they are a **Platform Operator** and how many tenants they belong to. Open a user to manage their **Tenant Memberships** — activate or deactivate a membership, or grant or revoke **Tenant Admin**. These changes require confirming a privileged-action prompt.

## What's next

- [Tenants](tenants.md)
- [Platform Elevation](elevation.md)
- [First Login](../getting-started/first-login.md)
