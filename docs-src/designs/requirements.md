# Requirements

Capture, organize, review, and export the project requirements for a design.

## Prerequisites

- An existing design. See [Designs](designs.md).
- Open the design and select the **Requirements** tab.

Every new design starts with a default set of requirement categories. Categories belong to a single design, so you can tailor them freely without affecting other designs.

## Add a requirement

1. On the **Requirements** tab, select **Add Requirement**.
2. Choose a **Category**.
3. Set a **Priority** — **Must Have**, **Should Have**, or **Nice to Have**.
4. Enter a **Description**, and optional **Client Notes**.
5. Optionally set **Assign To** to give the requirement an owner.
6. Select **Add Requirement**.

## Result

The requirement appears in its category, numbered as **category.item** (for example **2.1**), and starts with a **Pending Review** status. To change it later, select it and choose **Edit**.

## Organize categories and ordering

Requirements are numbered automatically. Categories that contain at least one requirement are numbered `1`, `2`, `3`… in order, and requirements within a category are sub-numbered `1.1`, `1.2`, `2.1`, and so on.

- **Reorder requirements** — drag a card by its handle to move it within its category.
- **Manage categories** — select **Manage Categories** to add, rename, or delete categories, and drag them to reorder. The category order sets the numbering used both on screen and in the export.

!!! note "Deleting a category"
    A category can only be deleted once it has no requirements. Move or delete its requirements first.

## Review requirements

Expand a requirement card to record a designer decision using **Accept**, **Needs Clarification**, or **Challenge**, and to add comments in the discussion thread. The toolbar shows how many requirements have been accepted, alongside a progress bar. Use the category chips to filter the list to a single category.

## Export to Excel

1. Select **Export**.
2. Choose a version:
    - **Customer version** — the requirement number, description, priority, and client notes.
    - **Internal version** — the same columns plus status and assignee.
3. The workbook downloads as `Requirements - <design name>.xlsx`.

The export mirrors the on-screen numbering and includes a header block with the design name, opportunity, customer, sales rep, and date.

## Tips

- Only categories that contain requirements are numbered and exported; empty categories are skipped.

## What's next

- [Designs](designs.md)
- [Catalog](catalog.md)
