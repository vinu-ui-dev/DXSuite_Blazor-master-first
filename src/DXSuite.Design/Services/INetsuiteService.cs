using DXSuite.Design.Entities;
using DXSuite.Design.Models;

namespace DXSuite.Design.Services;

public interface INetsuiteService
{
    Task<bool> TestConnectionAsync(TblDesignNetsuiteConnection conn, CancellationToken ct = default);

    /// <summary>
    /// Like TestConnectionAsync but returns the HTTP status and response body so the UI can show
    /// the actual NetSuite error (e.g. "USER_ERROR: Invalid login attempt", "INSUFFICIENT_PERMISSION").
    /// </summary>
    Task<NetsuiteTestResult> TestConnectionDetailedAsync(TblDesignNetsuiteConnection conn, CancellationToken ct = default);
    Task<List<NetsuiteOpportunity>> SearchOpportunitiesAsync(string term, CancellationToken ct = default);
    Task<NetsuiteOpportunity?> GetOpportunityAsync(string id, CancellationToken ct = default);
    Task<List<NetsuiteItem>> SearchItemsAsync(string term, CancellationToken ct = default);

    /// <summary>
    /// Creates or updates a NetSuite Estimate. Returns the estimate's internal NS ID.
    /// Pass null estimateId to create; pass an existing ID to update via PATCH.
    /// </summary>
    Task<string> PushEstimateAsync(WorksheetPushRequest req, CancellationToken ct = default);

    /// <summary>Searches for vendors by name (SuiteQL LIKE on entityid).</summary>
    Task<List<NetsuiteVendor>> GetVendorsAsync(string term, CancellationToken ct = default);

    /// <summary>
    /// Searches the NetSuite Manufacturer list by name.
    /// Returns an empty list when the account does not have a Manufacturer list configured —
    /// callers should hide or disable the Manufacturer field in that case.
    /// </summary>
    Task<List<NetsuiteManufacturer>> GetManufacturersAsync(string term, CancellationToken ct = default);

    /// <summary>
    /// Returns all configured Units Types. Returns an empty list when the account does not
    /// have the Units of Measure feature enabled — callers should hide the UoM field in that case.
    /// </summary>
    Task<List<NetsuiteUnitType>> GetUnitTypesAsync(CancellationToken ct = default);

    /// <summary>Creates a new Inventory Item record in NetSuite and returns the result.</summary>
    Task<CreateNetsuiteItemResult> CreateItemAsync(CreateNetsuiteItemRequest req, CancellationToken ct = default);

    /// <summary>
    /// Fires a signed GET request to the given REST Record API path and returns the raw response
    /// body as a string. Intended for ad-hoc API exploration from the configuration tab.
    /// Examples: "inventoryitem/123?expandSubResources=true", "vendor?limit=5"
    /// </summary>
    Task<(bool Success, int? StatusCode, string Body)> FetchRawAsync(string path, CancellationToken ct = default);

    Task<List<NetsuiteLocation>> GetLocationsAsync(CancellationToken ct = default);
    Task<List<NetsuiteTermsOption>> GetTermsAsync(CancellationToken ct = default);
    Task<List<NetsuiteLeadSource>> GetLeadSourcesAsync(CancellationToken ct = default);
    Task<List<NetsuiteOpportunityType>> GetOpportunityTypesAsync(CancellationToken ct = default);
    /// <summary>Returns the NetSuite customer (entity) internal ID linked to an opportunity.</summary>
    Task<string?> GetOpportunityEntityIdAsync(string opportunityId, CancellationToken ct = default);
    Task<List<NetsuiteAddressEntry>> GetCustomerAddressBookAsync(string customerId, CancellationToken ct = default);
    Task<CreateAddressResult> CreateCustomerAddressAsync(string customerId, CreateAddressRequest req, CancellationToken ct = default);

    /// <summary>
    /// Searches the NetSuite descriptionitem catalog by name.
    /// Description items are used as line-item separators/headers in NetSuite estimates.
    /// </summary>
    Task<List<NetsuiteDescriptionItem>> SearchDescriptionItemsAsync(string term, CancellationToken ct = default);

    /// <summary>
    /// Returns all description items in the account (no filter). Used to auto-select the
    /// default description item when pushing estimate description rows, so users never need
    /// to pick one manually.
    /// </summary>
    Task<List<NetsuiteDescriptionItem>> GetDescriptionItemsAsync(CancellationToken ct = default);

    /// <summary>
    /// Creates a new descriptionitem record in NetSuite with the given name and returns its ID.
    /// Called at push time to ensure a description item exists for each section/group header.
    /// </summary>
    Task<string> CreateDescriptionItemAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Fetches the human-readable tranId (e.g. "QT-00123") for an estimate by its internal NS ID.
    /// Returns null when the record cannot be found or the request fails.
    /// </summary>
    Task<string?> GetEstimateTranIdAsync(string internalId, CancellationToken ct = default);
}

public sealed record NetsuiteTestResult(bool Success, int? StatusCode, string? Message);
