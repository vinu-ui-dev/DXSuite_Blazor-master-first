using DXSuite.Design.Entities;

namespace DXSuite.Design.Models;

public class NetsuiteOpportunity
{
    public string Id { get; set; } = string.Empty;
    public string TranId { get; set; } = string.Empty;
    public string Entity { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string EntityStatus { get; set; } = string.Empty;
    public string SalesRep { get; set; } = string.Empty;
}

public class NetsuiteItem
{
    public string Id { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string PurchaseDescription { get; set; } = string.Empty;
    public decimal Cost { get; set; }
    public decimal Rate { get; set; }
}

public class NetsuiteVendor
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class NetsuiteManufacturer
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class NetsuiteUnitType
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class CreateNetsuiteItemRequest
{
    public string ItemId { get; set; } = string.Empty;
    public string PurchaseDescription { get; set; } = string.Empty;
    public decimal Cost { get; set; }
    public decimal Rate { get; set; }
    /// <summary>Always sent; defaults to PurchaseDescription when the user doesn't override it.</summary>
    public string SalesDescription { get; set; } = string.Empty;
    public string? Mpn { get; set; }
    /// <summary>Plain-text manufacturer name sent directly to the NS "manufacturer" field.</summary>
    public string? Manufacturer { get; set; }
    public string? VendorId { get; set; }
    public string? UnitTypeId { get; set; }
}

public sealed record CreateNetsuiteItemResult(bool Success, string? NetsuiteItemId, string? Error);

public class NetsuiteLocation  { public string Id { get; set; } = ""; public string Name { get; set; } = ""; }
public class NetsuiteTermsOption { public string Id { get; set; } = ""; public string Name { get; set; } = ""; }
public class NetsuiteLeadSource { public string Id { get; set; } = ""; public string Name { get; set; } = ""; }
public class NetsuiteOpportunityType { public string Id { get; set; } = ""; public string Name { get; set; } = ""; }
public class NetsuiteAddressEntry
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string AddrText { get; set; } = "";
    public bool DefaultBilling { get; set; }
    public bool DefaultShipping { get; set; }
}
public class CreateAddressRequest
{
    public string Label { get; set; } = "";
    public string CountryId { get; set; } = "US";
    public string? Attention { get; set; }
    public string? Addressee { get; set; }
    public string? Phone { get; set; }
    public string? Addr1 { get; set; }
    public string? Addr2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Zip { get; set; }
}
public sealed record CreateAddressResult(bool Success, string? NewAddressId, string? Error);

/// <summary>Returned by CreateNetsuiteItemModal after a successful item creation.</summary>
public sealed record CreatedItemInfo(NetsuiteItem NetsuiteItem, int CatalogItemId);

/// <summary>Response envelope for NetSuite REST list endpoints.</summary>
internal class NetsuiteListResponse<T>
{
    [System.Text.Json.Serialization.JsonPropertyName("items")]
    public List<T> Items { get; set; } = [];

    [System.Text.Json.Serialization.JsonPropertyName("count")]
    public int Count { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("hasMore")]
    public bool HasMore { get; set; }
}
