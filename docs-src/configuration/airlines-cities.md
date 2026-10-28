# Airlines and Cities

Maintain the airline and city reference data that aviation displays rely on, using the **Airlines** and **Cities** tabs in the **Configuration** area.

## Prerequisites

- Permission to open **Configuration**.
- The Aviation module enabled for your tenant. If you do not see the **Airlines** and **Cities** tabs, the module is not available in this tenant.

## Airlines

The **Airlines** tab links each airline to the airport locations it operates from, and manages the wayfinding PIN for airlines that use one.

### Steps: assign locations to an airline

1. Open **Configuration** and select the **Airlines** tab.
2. In **Search Airline**, start typing an airline name or code, then pick the airline. Its **Airline Code** is shown alongside.
3. In **Search IATA Locations**, start typing a location name or code and pick a match.
4. Select **Add**. The location appears under **Assigned Locations**.
5. To remove a link, select **Remove** next to a location.

### Steps: update a wayfinding PIN

1. Select an airline that uses a PIN. The **Wayfinding PIN** field shows the current value.
2. In **Update PIN**, enter a new 4-digit PIN.
3. Select **Save**.

!!! note
    The PIN fields appear only for airlines configured to use a wayfinding PIN.

## Cities

The **Cities** tab links each city (airport location) to the languages its content should be available in.

### Steps: assign languages to a city

1. Open **Configuration** and select the **Cities** tab.
2. In **Search City**, start typing a city name or IATA code (at least three characters), then pick the city. Its **Airport Code** is shown alongside.
3. In **Search Languages**, start typing a language name and pick a match.
4. Select **Add**. The language appears under **Assigned Languages**.
5. To remove a link, select **Remove** next to a language.

## Result

Airlines carry their assigned locations and PIN, and cities carry their assigned languages. This reference data is shared across the tenant and used by aviation displays and scheduling.

## What's next

- [Content Metadata](metadata.md)
- [Locations](../infrastructure/locations.md)
