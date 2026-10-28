# Catalog

Browse and maintain the shared reference data that designs draw on when you build worksheets and estimates.

## Prerequisites

- The **Designs** area must be enabled for your tenant.
- Catalog data is shared across all designs for your tenant, so changes here affect every design.

## Open the Catalog

Select **Catalog** from the top navigation. The page opens on the **Items** tab, with the remaining data organized across the tabs below.

## Steps

1. Select a tab to browse that part of the catalog:
    - **Items** — Equipment and products pulled from NetSuite, each with a description, manufacturer, and category.
    - **Categories** — The categories used to group items, optionally organized into category groups.
    - **Labor** — Labor types (roles) and the columns that drive labor calculations.
    - **Attributes** — Reusable attributes that describe items and categories.
    - **Misc** — Miscellaneous line items.
    - **Travel** — Travel cost settings.
    - **Materials** — Materials categories and items.
2. Use the actions within a tab — typically **Add**, **Edit**, **Refresh**, and **Delete** — to maintain its records.

## Result

The catalog reflects your changes immediately, and the updated items, categories, and labor become available when building worksheets in a [design](designs.md).

## Add a catalog item

1. On the **Items** tab, select **+ Add Item**.
2. Search for and select the matching NetSuite item.
3. Save the item. Use **Refresh** on any existing row later to pull the latest details from NetSuite.

!!! note "Items come from NetSuite"
    Catalog items are sourced from the connected NetSuite account. If an item cannot be found when adding or refreshing, confirm it exists in NetSuite first.

## Tips

- A category cannot be deleted while items are still assigned to it; reassign or remove those items first.
- On the **Labor** tab, drag column rows to set their left-to-right order in the equipment table.

## What's next

- [Designs](designs.md)
- [Requirements](requirements.md)
