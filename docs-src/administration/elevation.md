# Platform Elevation

Request temporary, approved access to a tenant you do not normally belong to, so support work is time-bound, justified, and fully audited.

## Prerequisites

- You must be a **Platform Operator**.
- A second operator is required to approve your request — you cannot approve your own.

## How elevation works

Platform operators do not have standing access to tenant data. To work inside a tenant they are not a member of, an operator requests an **elevation**: a time-limited grant for a specific tenant at a chosen access level. Another operator must approve it before it becomes active.

Once active, the elevation is enforced automatically for that tenant:

- **Read Only (TenantSupport)** allows viewing tenant data.
- **Read + Write (TenantSupportWrite)** is required for any action that changes data. A read-only elevation cannot perform write actions.

Access ends when the elevation expires, and every use of a live elevation is written to the [Audit Log](audit-log.md).

## Steps: request an elevation

1. In the left navigation, open **Platform → Elevation**.
2. Choose the **Target Tenant**.
3. Choose the **Access Level** — **Read Only (TenantSupport)** or **Read + Write (TenantSupportWrite)**.
4. Set the **Duration (hours)** (1–8).
5. Optionally enter a **Ticket Reference** (for example, `SUPPORT-1234`).
6. Enter a **Reason**. This is required.
7. Select **Submit Request**.

## Result

Your request is submitted and waits for approval. A second operator reviews pending requests and approves them; they cannot approve a request they made themselves. After approval, your access to the target tenant is live until the duration you requested elapses.

!!! warning "Least privilege and shortest duration"
    Request **Read Only** unless you genuinely need to make changes, and choose the shortest duration that covers the task. Always record a reason (and a ticket reference where one exists) so the access is traceable.

!!! info "Operators who are tenant members"
    If an operator already has an active membership in a tenant, they access it as a normal user and do not need an elevation for that tenant.

## What's next

- [Audit Log](audit-log.md)
- [Tenants](tenants.md)
- [Users and Roles](users-and-roles.md)
