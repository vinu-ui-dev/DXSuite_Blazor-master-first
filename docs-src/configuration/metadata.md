# Content Metadata

Metadata keys are custom fields you define once and then use to tag devices, content, and access rules — the basis for filtering content to the right locations.

## What a metadata key is

A metadata key is a reusable field with a name and a type. After you define a key, its values can be applied across DXSuite — for example, tagging a media asset with a **Department** or a **Region** — so content can be matched to the locations and audiences it belongs to.

Metadata keys are managed on the **Metadata** tab of the **Configuration** area, shown as a list of **Metadata Keys**.

## Prerequisites

- Permission to open **Configuration**.

## Steps: add a metadata key

1. Open **Configuration** and select the **Metadata** tab.
2. Select **+ Add Key**.
3. Enter the **Key** — the internal identifier (for example, `department`).
4. Enter a **Display Name** shown to users (for example, `Department`).
5. Choose a **Field Type**:
    - **String** — free text.
    - **Int** — a whole number.
    - **List** — a single choice from options you define.
    - **Multi-Select** — one or more choices from options you define.
6. Optionally add a **Description** to guide whoever fills in the value.
7. For **List** or **Multi-Select**, select **+ Add Option** to define each choice.
8. Select **Save**.

## Result

The key appears in the **Metadata Keys** list with its type and options. It is now available wherever metadata is applied — including media assets, devices, and access rules — and the values drive which content is filtered to each location.

## Tips

- The **Key** cannot be changed after creation, though its display name, description, and options can be edited.
- A key that is in use cannot be deleted. DXSuite lists where it is used (devices, users, groups, assets, or access rules) so you can clear those references first.

!!! info "Consistent choices"
    Prefer **List** or **Multi-Select** for values that should be picked from a fixed set. This keeps tagging consistent and makes location filtering reliable.

## What's next

- [Airlines and Cities](airlines-cities.md)
- [Scheduling and Media](../director/scheduling.md)
- [Devices](../infrastructure/devices.md)
