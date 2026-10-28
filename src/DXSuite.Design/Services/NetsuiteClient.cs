using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using DXSuite.Design.Entities;
using DXSuite.Design.Models;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;

namespace DXSuite.Design.Services;

/// <summary>
/// Typed HTTP client for the NetSuite REST Record API.
/// Credentials are loaded per-call from the DB via INetsuiteConnectionProvider.
/// OAuth 1.0a Token-Based Authentication (TBA) with HMAC-SHA256.
/// </summary>
public class NetsuiteClient : INetsuiteService
{
    private readonly HttpClient _http;
    private readonly INetsuiteConnectionProvider _connProvider;
    private readonly NetsuiteCredentialProtector _protector;
    private readonly ILogger<NetsuiteClient> _logger;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public NetsuiteClient(
        HttpClient http,
        INetsuiteConnectionProvider connProvider,
        NetsuiteCredentialProtector protector,
        ILogger<NetsuiteClient> logger)
    {
        _http = http;
        _connProvider = connProvider;
        _protector = protector;
        _logger = logger;
    }

    public async Task<bool> TestConnectionAsync(TblDesignNetsuiteConnection conn, CancellationToken ct = default)
        => (await TestConnectionDetailedAsync(conn, ct)).Success;

    public async Task<NetsuiteTestResult> TestConnectionDetailedAsync(TblDesignNetsuiteConnection conn, CancellationToken ct = default)
    {
        try
        {
            var decrypted = Decrypt(conn);
            if (string.IsNullOrWhiteSpace(decrypted.AccountId))
                return new NetsuiteTestResult(false, null, "Account ID is required.");
            if (string.IsNullOrWhiteSpace(decrypted.ConsumerKey) || string.IsNullOrWhiteSpace(decrypted.ConsumerSecret)
                || string.IsNullOrWhiteSpace(decrypted.TokenId) || string.IsNullOrWhiteSpace(decrypted.TokenSecret))
                return new NetsuiteTestResult(false, null, "Consumer Key/Secret and Token ID/Secret are all required.");

            // Use "employee" as the probe — it exists in every NetSuite account regardless of which
            // features (Multi-Currency, OneWorld, etc.) are enabled. "currency" only exists when the
            // Multi-Currency feature is on.
            var url = RecordUrl(decrypted.AccountId, "employee?limit=1");
            var req = BuildRequest(HttpMethod.Get, url, decrypted);
            var resp = await _http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
                return new NetsuiteTestResult(true, (int)resp.StatusCode, "OK");

            var body = await resp.Content.ReadAsStringAsync(ct);
            // Trim gigantic error bodies for UI display
            if (body.Length > 1000) body = body[..1000] + "…";
            _logger.LogWarning("NetSuite test connection failed: {Status} {Body}", (int)resp.StatusCode, body);
            return new NetsuiteTestResult(false, (int)resp.StatusCode, body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NetSuite test connection threw");
            return new NetsuiteTestResult(false, null, ex.Message);
        }
    }

    public async Task<List<NetsuiteOpportunity>> SearchOpportunitiesAsync(string term, CancellationToken ct = default)
    {
        var conn = await GetDecryptedConnAsync(ct);
        if (conn is null) return [];

        // SuiteQL with BUILTIN.DF() resolves reference field display names without joins.
        // The REST ?q= parameter uses full-text indexing that is unreliable for substring search.
        var safeTerm = term.Replace("'", "''").ToLower();
        var sql = "SELECT o.id, o.tranid, BUILTIN.DF(o.entity) AS entity, o.title, " +
                  "BUILTIN.DF(o.entitystatus) AS entitystatus, BUILTIN.DF(o.salesrep) AS salesrep " +
                  "FROM opportunity o " +
                  $"WHERE LOWER(o.tranid) LIKE '%{safeTerm}%' " +
                  $"OR LOWER(o.title) LIKE '%{safeTerm}%' " +
                  $"OR LOWER(BUILTIN.DF(o.entity)) LIKE '%{safeTerm}%' " +
                  "ORDER BY o.tranid DESC";

        var url = $"https://{NormalizeAccountId(conn.AccountId!)}.suitetalk.api.netsuite.com/services/rest/query/v1/suiteql?limit=50";
        var req = BuildRequest(HttpMethod.Post, url, conn);
        req.Headers.Add("Prefer", "transient");
        req.Content = JsonContent.Create(new { q = sql }, options: _json);

        try
        {
            var resp = await _http.SendAsync(req, ct);
            var rawBody = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("NetSuite SuiteQL opportunity search returned {Status}: {Body}",
                    (int)resp.StatusCode, rawBody[..Math.Min(rawBody.Length, 500)]);
                return [];
            }
            var body = JsonSerializer.Deserialize<NetsuiteListResponse<NetsuiteOpportunitySuiteQL>>(rawBody, _json);
            return body?.Items.Select(MapSuiteQL).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NetSuite opportunity search failed for term '{Term}'", term);
            return [];
        }
    }

    public async Task<NetsuiteOpportunity?> GetOpportunityAsync(string id, CancellationToken ct = default)
    {
        var conn = await GetDecryptedConnAsync(ct);
        if (conn is null) return null;

        var fields = "tranid,entity,title,entitystatus,salesrep";
        var url = RecordUrl(conn.AccountId!, $"opportunity/{id}?fields={fields}");
        var req = BuildRequest(HttpMethod.Get, url, conn);

        try
        {
            var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            var raw = await resp.Content.ReadFromJsonAsync<NetsuiteOpportunityRaw>(_json, ct);
            return raw is null ? null : Map(raw);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NetSuite get opportunity failed for id '{Id}'", id);
            return null;
        }
    }

    public async Task<string?> GetEstimateTranIdAsync(string internalId, CancellationToken ct = default)
    {
        var conn = await GetDecryptedConnAsync(ct);
        if (conn is null) return null;

        var url = RecordUrl(conn.AccountId!, $"estimate/{internalId}?fields=id,tranid");
        var req = BuildRequest(HttpMethod.Get, url, conn);

        try
        {
            var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            var doc = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(_json, ct);
            return doc.TryGetProperty("tranid", out var p) ? p.GetString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NetSuite get estimate tranId failed for id '{Id}'", internalId);
            return null;
        }
    }

    public async Task<List<NetsuiteItem>> SearchItemsAsync(string term, CancellationToken ct = default)
    {
        var conn = await GetDecryptedConnAsync(ct);
        if (conn is null) return [];

        // SuiteQL gives reliable LIKE-based substring search across itemid and description.
        // The REST Record API ?q= parameter relies on full-text indexing and does not reliably
        // match partial item numbers (e.g. "26" won't find "26LG30DC").
        // Only standard item table columns are selected here — custom fields (custitem_*)
        // and derived price columns (baseprice) are excluded because they are schema-dependent
        // and cause 400 errors if not present in the account.
        // The Prefer: transient header returns results directly without a server-side cursor.
        // SuiteQL is Oracle-based — it uses ROWNUM or the API's ?limit= URL param, not MySQL LIMIT.
        var safeTerm = term.Replace("'", "''").ToLower();
        // displayname mirrors itemid for most item types; selecting both as a fallback handles
        // subtypes where SuiteQL returns itemid as null but displayname is always populated.
        // The item table has no direct price column in SuiteQL — pricing lives in the pricing
        // table (one row per item/pricelevel/quantity break). pricelevel=1 is Base Price.
        // quantity=0 is omitted from the join condition: some accounts store the base break at
        // quantity=0, others at quantity=1 — GROUP BY + MIN picks the lowest break price
        // (which equals the base price when only one break exists, as is typical).
        var sql = "SELECT i.id, i.itemid, i.displayname, i.manufacturer, i.purchasedescription, i.cost, MIN(p.unitprice) AS unitprice " +
                  "FROM item i " +
                  "LEFT JOIN pricing p ON p.item = i.id AND p.pricelevel = 1 " +
                  $"WHERE (LOWER(i.itemid) LIKE '%{safeTerm}%' OR LOWER(i.purchasedescription) LIKE '%{safeTerm}%') " +
                  "GROUP BY i.id, i.itemid, i.displayname, i.manufacturer, i.purchasedescription, i.cost " +
                  "ORDER BY i.itemid";

        // Row cap goes in the URL, not the SQL — SuiteQL uses Oracle syntax which doesn't support LIMIT.
        var url = $"https://{NormalizeAccountId(conn.AccountId!)}.suitetalk.api.netsuite.com/services/rest/query/v1/suiteql?limit=50";
        var req = BuildRequest(HttpMethod.Post, url, conn);
        req.Headers.Add("Prefer", "transient");
        req.Content = JsonContent.Create(new { q = sql }, options: _json);

        try
        {
            var resp = await _http.SendAsync(req, ct);
            var rawBody = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("NetSuite SuiteQL item search returned {Status}: {Body}", (int)resp.StatusCode, rawBody);
                return [];
            }
            // Temporary diagnostic — log raw SuiteQL response to confirm field names.
            _logger.LogWarning("NetSuite SuiteQL raw response: {Body}", rawBody[..Math.Min(rawBody.Length, 1000)]);
            var body = JsonSerializer.Deserialize<NetsuiteListResponse<NetsuiteItemRaw>>(rawBody, _json);
            return body?.Items.Select(MapItem).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NetSuite item search failed for term '{Term}'", term);
            return [];
        }
    }

    public async Task<string> PushEstimateAsync(WorksheetPushRequest req, CancellationToken ct = default)
    {
        var conn = await GetDecryptedConnAsync(ct);
        if (conn is null) throw new InvalidOperationException("No active NetSuite connection configured.");

        var isNew = string.IsNullOrEmpty(req.EstimateId);
        var method = isNew ? HttpMethod.Post : new HttpMethod("PATCH");
        var path = isNew ? "estimate" : $"estimate/{req.EstimateId}";
        // ?replace=item instructs NetSuite to delete all existing line items before writing the new set.
        var url = RecordUrl(conn.AccountId!, path) + (isNew ? "" : "?replace=item");

        var lineItemPayloads = req.LineItems.Select(BuildLineItemPayload).ToArray();

        // custbody_acs_summary_view is a custom header-level flag required on all estimates
        // for this account's PDF export and summary view feature.
        var body = isNew ? (object)new
        {
            customForm = !string.IsNullOrEmpty(conn.CustomFormId) ? new { id = conn.CustomFormId } : null,
            entity = req.Config?.EntityId is { Length: > 0 } eid ? new { id = eid } : null,
            opportunity = string.IsNullOrEmpty(req.OpportunityId) ? null : new { id = req.OpportunityId },
            location = req.Config?.LocationId is { Length: > 0 } loc ? new { id = loc } : null,
            billaddresslist = req.Config?.BillAddressListId is { Length: > 0 } bill ? new { id = bill } : null,
            shipaddresslist = req.Config?.ShipAddressListId is { Length: > 0 } ship ? new { id = ship } : null,
            terms = req.Config?.TermsId is { Length: > 0 } trm ? new { id = trm } : null,
            custbodyopportunity_type = req.Config?.OpportunityTypeId is { Length: > 0 } ot ? new { id = ot } : null,
            leadsource = req.Config?.LeadSourceId is { Length: > 0 } ls ? new { id = ls } : null,
            custbody_acs_summary_view = true,
            item = new { items = lineItemPayloads }
        } : (object)new
        {
            custbody_acs_summary_view = true,
            item = new { items = lineItemPayloads }
        };

        var httpReq = BuildRequest(method, url, conn);
        httpReq.Content = JsonContent.Create(body, options: _json);

        var resp = await _http.SendAsync(httpReq, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"NetSuite estimate push failed ({(int)resp.StatusCode}): {EnrichPushError(err, req.LineItems)}");
        }

        if (isNew)
        {
            // NetSuite returns 204 with Location header on POST
            if (resp.Headers.Location is not null)
            {
                var segments = resp.Headers.Location.AbsolutePath.Split('/');
                return segments.Last();
            }
            // Fallback: try to read body for id field
            try
            {
                var bodyDoc = await resp.Content.ReadFromJsonAsync<JsonElement>(_json, ct);
                if (bodyDoc.TryGetProperty("id", out var idProp))
                    return idProp.GetString() ?? throw new InvalidOperationException("Estimate created but ID not returned.");
            }
            catch { /* ignore parse failure */ }
            throw new InvalidOperationException("Estimate created but could not extract ID from response.");
        }

        return req.EstimateId!;
    }

    public async Task<List<NetsuiteVendor>> GetVendorsAsync(string term, CancellationToken ct = default)
    {
        var conn = await GetDecryptedConnAsync(ct);
        if (conn is null) return [];

        var safeTerm = term.Replace("'", "''").ToLower();
        var sql = $"SELECT id, entityid FROM vendor WHERE LOWER(entityid) LIKE '%{safeTerm}%' ORDER BY entityid";
        var url = $"https://{NormalizeAccountId(conn.AccountId!)}.suitetalk.api.netsuite.com/services/rest/query/v1/suiteql?limit=30";
        var req = BuildRequest(HttpMethod.Post, url, conn);
        req.Headers.Add("Prefer", "transient");
        req.Content = JsonContent.Create(new { q = sql }, options: _json);

        try
        {
            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return [];
            var body = await resp.Content.ReadFromJsonAsync<NetsuiteListResponse<NetsuiteVendorRaw>>(_json, ct);
            return body?.Items
                .Where(v => !string.IsNullOrEmpty(v.Id))
                .Select(v => new NetsuiteVendor { Id = v.Id!, Name = v.EntityId ?? v.Id! })
                .ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NetSuite vendor search failed for term '{Term}'", term);
            return [];
        }
    }

    public async Task<List<NetsuiteUnitType>> GetUnitTypesAsync(CancellationToken ct = default)
    {
        var conn = await GetDecryptedConnAsync(ct);
        if (conn is null) return [];

        // The unitstype table only exists when the "Units of Measure" feature is enabled.
        // The name column in SuiteQL is "name", not "unittype".
        var sql = "SELECT id, name FROM unitstype WHERE isinactive = 'F' ORDER BY name";
        var url = $"https://{NormalizeAccountId(conn.AccountId!)}.suitetalk.api.netsuite.com/services/rest/query/v1/suiteql?limit=200";
        var req = BuildRequest(HttpMethod.Post, url, conn);
        req.Headers.Add("Prefer", "transient");
        req.Content = JsonContent.Create(new { q = sql }, options: _json);

        try
        {
            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return [];
            var body = await resp.Content.ReadFromJsonAsync<NetsuiteListResponse<NetsuiteUnitTypeRaw>>(_json, ct);
            return body?.Items
                .Where(u => !string.IsNullOrEmpty(u.Id))
                .Select(u => new NetsuiteUnitType { Id = u.Id!, Name = u.Name ?? u.Id! })
                .ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NetSuite unit type fetch failed");
            return [];
        }
    }

    public async Task<List<NetsuiteManufacturer>> GetManufacturersAsync(string term, CancellationToken ct = default)
    {
        var conn = await GetDecryptedConnAsync(ct);
        if (conn is null) return [];

        // The manufacturer table is a standard NetSuite list — it only exists when the account
        // has configured manufacturers. Returns empty without logging a warning on 400/404 so
        // the caller can gracefully hide the field rather than surfacing a spurious error.
        var safeTerm = term.Replace("'", "''").ToLower();
        var sql = $"SELECT id, name FROM manufacturer WHERE LOWER(name) LIKE '%{safeTerm}%' ORDER BY name";
        var url = $"https://{NormalizeAccountId(conn.AccountId!)}.suitetalk.api.netsuite.com/services/rest/query/v1/suiteql?limit=30";
        var req = BuildRequest(HttpMethod.Post, url, conn);
        req.Headers.Add("Prefer", "transient");
        req.Content = JsonContent.Create(new { q = sql }, options: _json);

        try
        {
            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return [];
            var body = await resp.Content.ReadFromJsonAsync<NetsuiteListResponse<NetsuiteManufacturerRaw>>(_json, ct);
            return body?.Items
                .Where(m => !string.IsNullOrEmpty(m.Id))
                .Select(m => new NetsuiteManufacturer { Id = m.Id!, Name = m.Name ?? m.Id! })
                .ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NetSuite manufacturer search failed for term '{Term}'", term);
            return [];
        }
    }

    public async Task<CreateNetsuiteItemResult> CreateItemAsync(CreateNetsuiteItemRequest req, CancellationToken ct = default)
    {
        var conn = await GetDecryptedConnAsync(ct);
        if (conn is null)
            return new CreateNetsuiteItemResult(false, null, "No active NetSuite connection configured.");

        var url = RecordUrl(conn.AccountId!, "inventoryitem");
        var body = BuildCreateItemBody(req);
        var httpReq = BuildRequest(HttpMethod.Post, url, conn);
        httpReq.Content = JsonContent.Create(body, options: _json);

        try
        {
            var resp = await _http.SendAsync(httpReq, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                if (err.Length > 600) err = err[..600] + "…";
                _logger.LogWarning("NetSuite item creation failed: {Status} {Body}", (int)resp.StatusCode, err);
                return new CreateNetsuiteItemResult(false, null, $"NetSuite error ({(int)resp.StatusCode}): {err}");
            }

            // NetSuite returns 204 with Location: .../inventoryitem/{id} on successful POST.
            string? itemId = null;
            if (resp.Headers.Location is not null)
            {
                var segments = resp.Headers.Location.AbsolutePath.Split('/');
                itemId = segments.Last();
            }

            if (string.IsNullOrEmpty(itemId))
            {
                // Fallback: some account configurations return 200 with a body containing "id".
                try
                {
                    var bodyDoc = await resp.Content.ReadFromJsonAsync<JsonElement>(_json, ct);
                    if (bodyDoc.TryGetProperty("id", out var idProp))
                        itemId = idProp.GetString();
                }
                catch { /* ignore */ }
            }

            if (string.IsNullOrEmpty(itemId))
                return new CreateNetsuiteItemResult(false, null, "Item may have been created but its ID could not be extracted from the response.");

            // Add vendor via a separate PATCH so the main item creation is not blocked by
            // vendor sublist field-name differences across NetSuite account configurations.
            if (!string.IsNullOrEmpty(req.VendorId))
                await PatchItemVendorAsync(itemId, req.VendorId, conn, ct);

            return new CreateNetsuiteItemResult(true, itemId, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NetSuite item creation threw an exception");
            return new CreateNetsuiteItemResult(false, null, ex.Message);
        }
    }

    public async Task<(bool Success, int? StatusCode, string Body)> FetchRawAsync(string path, CancellationToken ct = default)
    {
        var conn = await GetDecryptedConnAsync(ct);
        if (conn is null)
            return (false, null, "No active NetSuite connection configured.");

        var url = RecordUrl(conn.AccountId!, path.TrimStart('/'));
        var req = BuildRequest(HttpMethod.Get, url, conn);

        try
        {
            var resp = await _http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            return ((int)resp.StatusCode is >= 200 and < 300, (int)resp.StatusCode, body);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    public async Task<List<NetsuiteLocation>> GetLocationsAsync(CancellationToken ct = default)
    {
        var rows = await FetchSuiteQLLookupAsync("SELECT id, name FROM location WHERE isinactive = 'F' ORDER BY name", ct);
        return rows.Select(r => new NetsuiteLocation { Id = r.Id, Name = r.Name }).ToList();
    }

    public async Task<List<NetsuiteTermsOption>> GetTermsAsync(CancellationToken ct = default)
    {
        var rows = await FetchSuiteQLLookupAsync("SELECT id, name FROM term WHERE isinactive = 'F' ORDER BY name", ct);
        return rows.Select(r => new NetsuiteTermsOption { Id = r.Id, Name = r.Name }).ToList();
    }

    public async Task<List<NetsuiteLeadSource>> GetLeadSourcesAsync(CancellationToken ct = default)
    {
        // leadSource has no SuiteQL table and no navigable REST links in estimates — try the REST
        // collection endpoint with the camelCase record type name as it appears in the estimate JSON.
        var conn = await GetDecryptedConnAsync(ct);
        if (conn is null) return [];

        var url = RecordUrl(conn.AccountId!, "leadSource") + "?limit=1000";
        var req = BuildRequest(HttpMethod.Get, url, conn);
        try
        {
            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var errBody = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("GetLeadSourcesAsync REST failed ({Status}): {Body}",
                    (int)resp.StatusCode, errBody.Length > 600 ? errBody[..600] : errBody);
                // Fall back: derive distinct lead sources from existing opportunity records.
                return await GetLeadSourcesFromOpportunitiesAsync(conn, ct);
            }
            var body = await resp.Content.ReadFromJsonAsync<NetsuiteListResponse<NetsuiteLookupRaw>>(_json, ct);
            return body?.Items
                .Where(x => !string.IsNullOrEmpty(x.Id))
                .Select(r => new NetsuiteLeadSource { Id = r.Id!, Name = r.Name ?? r.Id! })
                .OrderBy(r => r.Name)
                .ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetLeadSourcesAsync threw");
            return [];
        }
    }

    /// <summary>
    /// Fallback: collects distinct lead source IDs from existing opportunities, then resolves
    /// each name via individual GET — used when the leadSource collection endpoint isn't navigable.
    /// </summary>
    private async Task<List<NetsuiteLeadSource>> GetLeadSourcesFromOpportunitiesAsync(DecryptedConn conn, CancellationToken ct)
    {
        // Pull distinct lead source IDs referenced by existing opportunities.
        var idSql = "SELECT DISTINCT leadsource AS id FROM opportunity WHERE leadsource IS NOT NULL ORDER BY leadsource";
        var url = $"https://{NormalizeAccountId(conn.AccountId!)}.suitetalk.api.netsuite.com/services/rest/query/v1/suiteql?limit=500";
        var req = BuildRequest(HttpMethod.Post, url, conn);
        req.Headers.Add("Prefer", "transient");
        req.Content = JsonContent.Create(new { q = idSql }, options: _json);

        var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return [];

        var body = await resp.Content.ReadFromJsonAsync<NetsuiteListResponse<NetsuiteLookupRaw>>(_json, ct);
        var ids = body?.Items.Where(x => !string.IsNullOrEmpty(x.Id)).Select(x => x.Id!).ToList() ?? [];
        if (ids.Count == 0) return [];

        // Resolve names by fetching each record individually.
        var results = new List<NetsuiteLeadSource>();
        foreach (var id in ids)
        {
            try
            {
                var getReq = BuildRequest(HttpMethod.Get, RecordUrl(conn.AccountId!, $"leadSource/{id}"), conn);
                var getResp = await _http.SendAsync(getReq, ct);
                if (!getResp.IsSuccessStatusCode) continue;
                var record = await getResp.Content.ReadFromJsonAsync<NetsuiteLookupRaw>(_json, ct);
                if (record?.Id is not null)
                    results.Add(new NetsuiteLeadSource { Id = record.Id, Name = record.Name ?? record.Id });
            }
            catch { /* skip individual failures */ }
        }

        _logger.LogInformation("GetLeadSourcesAsync fallback resolved {Count} lead sources from opportunities", results.Count);
        return results.OrderBy(r => r.Name).ToList();
    }

    public async Task<List<NetsuiteOpportunityType>> GetOpportunityTypesAsync(CancellationToken ct = default)
    {
        try
        {
            var rows = await FetchSuiteQLLookupAsync("SELECT id, name FROM customlist_opportunity_type WHERE isinactive = 'F' ORDER BY name", ct);
            return rows.Select(r => new NetsuiteOpportunityType { Id = r.Id, Name = r.Name }).ToList();
        }
        catch { return []; }
    }

    public async Task<string?> GetOpportunityEntityIdAsync(string opportunityId, CancellationToken ct = default)
    {
        var conn = await GetDecryptedConnAsync(ct);
        if (conn is null) return null;
        var safeId = opportunityId.Replace("'", "''");
        var sql = $"SELECT entity FROM opportunity WHERE id = {safeId}";
        var url = $"https://{NormalizeAccountId(conn.AccountId!)}.suitetalk.api.netsuite.com/services/rest/query/v1/suiteql?limit=1";
        var req = BuildRequest(HttpMethod.Post, url, conn);
        req.Headers.Add("Prefer", "transient");
        req.Content = JsonContent.Create(new { q = sql }, options: _json);
        try
        {
            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var body = await resp.Content.ReadFromJsonAsync<NetsuiteListResponse<NetsuiteEntityIdRaw>>(_json, ct);
            var raw = body?.Items.FirstOrDefault();
            if (raw is null) return null;
            return raw.EntityRaw.ValueKind == JsonValueKind.Number
                ? raw.EntityRaw.GetInt64().ToString()
                : raw.EntityRaw.GetString();
        }
        catch { return null; }
    }

    public async Task<List<NetsuiteAddressEntry>> GetCustomerAddressBookAsync(string customerId, CancellationToken ct = default)
    {
        var conn = await GetDecryptedConnAsync(ct);
        if (conn is null) return [];
        var url = RecordUrl(conn.AccountId!, $"customer/{customerId}/addressBook?expandSubResources=true");
        var req = BuildRequest(HttpMethod.Get, url, conn);
        try
        {
            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return [];
            var body = await resp.Content.ReadFromJsonAsync<NetsuiteAddressBookResponse>(_json, ct);
            return body?.Items?.Select(r => new NetsuiteAddressEntry
            {
                Id = r.Id.ValueKind == JsonValueKind.Number ? r.Id.GetInt64().ToString() : (r.Id.GetString() ?? ""),
                Label = r.Label ?? "",
                AddrText = r.AddressText ?? r.AddressBookAddress?.AddrText ?? "",
                DefaultBilling = r.DefaultBilling,
                DefaultShipping = r.DefaultShipping,
            }).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NetSuite address book fetch failed for customer {CustomerId}", customerId);
            return [];
        }
    }

    public async Task<List<NetsuiteDescriptionItem>> SearchDescriptionItemsAsync(string term, CancellationToken ct = default)
        => await QueryDescriptionItemsAsync(term, ct);

    public async Task<List<NetsuiteDescriptionItem>> GetDescriptionItemsAsync(CancellationToken ct = default)
        => await QueryDescriptionItemsAsync(null, ct);

    // Shared implementation: null term returns all description items (no LIKE filter).
    private async Task<List<NetsuiteDescriptionItem>> QueryDescriptionItemsAsync(string? term, CancellationToken ct)
    {
        var conn = await GetDecryptedConnAsync(ct);
        if (conn is null) return [];

        // descriptionitem is a subtype of item; SuiteQL table = "item" with itemtype = 'Description'.
        var sql = term is not null
            ? $"SELECT id, itemid, description FROM item WHERE itemtype = 'Description' AND LOWER(itemid) LIKE '%{term.Replace("'", "''").ToLower()}%' ORDER BY itemid"
            : "SELECT id, itemid, description FROM item WHERE itemtype = 'Description' ORDER BY itemid";

        var url = $"https://{NormalizeAccountId(conn.AccountId!)}.suitetalk.api.netsuite.com/services/rest/query/v1/suiteql?limit=100";
        var req = BuildRequest(HttpMethod.Post, url, conn);
        req.Headers.Add("Prefer", "transient");
        req.Content = JsonContent.Create(new { q = sql }, options: _json);

        try
        {
            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return [];
            var body = await resp.Content.ReadFromJsonAsync<NetsuiteListResponse<NetsuiteDescriptionItemRaw>>(_json, ct);
            return body?.Items
                .Where(r => !string.IsNullOrEmpty(r.Id))
                .Select(r => new NetsuiteDescriptionItem(r.Id!, r.ItemId ?? r.Id!, r.Description))
                .ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NetSuite description item query failed (term={Term})", term ?? "(all)");
            return [];
        }
    }

    public async Task<string> CreateDescriptionItemAsync(string name, CancellationToken ct = default)
    {
        var conn = await GetDecryptedConnAsync(ct);
        if (conn is null) throw new InvalidOperationException("No active NetSuite connection configured.");

        var url = RecordUrl(conn.AccountId!, "descriptionitem");
        var httpReq = BuildRequest(HttpMethod.Post, url, conn);
        // descriptionitem records only require itemid (the display name used in estimates).
        httpReq.Content = JsonContent.Create(new { itemid = name }, options: _json);

        var resp = await _http.SendAsync(httpReq, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            if (err.Length > 400) err = err[..400] + "…";
            throw new InvalidOperationException(
                $"Failed to create NS description item '{name}' ({(int)resp.StatusCode}): {err}");
        }

        // NS returns 204 with Location: .../descriptionitem/{id}
        if (resp.Headers.Location is not null)
        {
            var id = resp.Headers.Location.AbsolutePath.Split('/').Last();
            if (!string.IsNullOrEmpty(id)) return id;
        }

        // Fallback: some configurations return 200 with a body containing "id".
        try
        {
            var bodyDoc = await resp.Content.ReadFromJsonAsync<JsonElement>(_json, ct);
            if (bodyDoc.TryGetProperty("id", out var idProp))
            {
                var id = idProp.GetString();
                if (!string.IsNullOrEmpty(id)) return id;
            }
        }
        catch { /* ignore */ }

        throw new InvalidOperationException(
            $"NS description item '{name}' may have been created but its ID could not be extracted from the response.");
    }

    public async Task<CreateAddressResult> CreateCustomerAddressAsync(string customerId, CreateAddressRequest req, CancellationToken ct = default)
    {
        var conn = await GetDecryptedConnAsync(ct);
        if (conn is null) return new CreateAddressResult(false, null, "No active NetSuite connection.");
        var url = RecordUrl(conn.AccountId!, $"customer/{customerId}/addressBook");
        var body = new
        {
            label = req.Label,
            addressBookAddress = new
            {
                country = new { id = req.CountryId },
                attention = req.Attention,
                addressee = req.Addressee,
                addrphone = req.Phone,
                addr1 = req.Addr1,
                addr2 = req.Addr2,
                city = req.City,
                state = req.State,
                zip = req.Zip,
            }
        };
        var httpReq = BuildRequest(HttpMethod.Post, url, conn);
        httpReq.Content = JsonContent.Create(body, options: _json);
        try
        {
            var resp = await _http.SendAsync(httpReq, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                if (err.Length > 500) err = err[..500] + "…";
                return new CreateAddressResult(false, null, $"NetSuite error ({(int)resp.StatusCode}): {err}");
            }
            if (resp.Headers.Location is not null)
            {
                var segments = resp.Headers.Location.AbsolutePath.Split('/');
                return new CreateAddressResult(true, segments.Last(), null);
            }
            return new CreateAddressResult(false, null, "Address created but ID could not be extracted.");
        }
        catch (Exception ex)
        {
            return new CreateAddressResult(false, null, ex.Message);
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task<DecryptedConn?> GetDecryptedConnAsync(CancellationToken ct)
    {
        var conn = await _connProvider.GetActiveConnectionAsync(ct);
        if (conn is null)
        {
            _logger.LogWarning("No active NetSuite connection configured for this tenant");
            return null;
        }
        return Decrypt(conn);
    }

    private DecryptedConn Decrypt(TblDesignNetsuiteConnection conn) => new(
        conn.AccountId ?? string.Empty,
        conn.ConsumerKey ?? string.Empty,
        _protector.Decrypt(conn.ConsumerSecret) ?? string.Empty,
        conn.TokenId ?? string.Empty,
        _protector.Decrypt(conn.TokenSecret) ?? string.Empty,
        conn.CustomFormId);

    private static string RecordUrl(string accountId, string path)
        => $"https://{NormalizeAccountId(accountId)}.suitetalk.api.netsuite.com/services/rest/record/v1/{path}";

    /// <summary>
    /// NetSuite's REST hostname requires the account ID in lowercase with hyphens.
    /// Users commonly paste the form shown in NetSuite's UI (e.g. "1234567_SB1"),
    /// so we normalize underscores → hyphens and force lowercase.
    /// </summary>
    private static string NormalizeAccountId(string accountId)
        => accountId.Trim().Replace('_', '-').ToLowerInvariant();

    private static HttpRequestMessage BuildRequest(HttpMethod method, string url, DecryptedConn conn)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("Authorization", BuildOAuthHeader(method.Method, url, conn));
        return req;
    }

    /// <summary>
    /// Builds an OAuth 1.0a Authorization header using HMAC-SHA256 (NetSuite TBA requirement).
    /// NetSuite does not use SHA1 — HMAC-SHA256 is mandatory for TBA.
    /// </summary>
    private static string BuildOAuthHeader(string method, string fullUrl, DecryptedConn conn)
    {
        var uri = new Uri(fullUrl);
        var baseUrl = $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";

        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "").Replace("/", "").Replace("=", "");
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        // Collect all OAuth params plus any query string params for signature base string
        var oauthParams = new SortedDictionary<string, string>
        {
            ["oauth_consumer_key"] = conn.ConsumerKey,
            ["oauth_nonce"] = nonce,
            ["oauth_signature_method"] = "HMAC-SHA256",
            ["oauth_timestamp"] = timestamp,
            ["oauth_token"] = conn.TokenId,
            ["oauth_version"] = "1.0",
        };

        // Include query string params in the signature base string (raw — encoded once at paramString time)
        var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);
        foreach (string key in queryParams)
        {
            if (key is not null)
                oauthParams[key] = queryParams[key] ?? "";
        }

        var paramString = string.Join("&",
            oauthParams.Select(kvp => $"{PercentEncode(kvp.Key)}={PercentEncode(kvp.Value)}"));

        var baseString = $"{method}&{PercentEncode(baseUrl)}&{PercentEncode(paramString)}";

        // Signing key: PercentEncode(ConsumerSecret)&PercentEncode(TokenSecret)
        var signingKey = $"{PercentEncode(conn.ConsumerSecret)}&{PercentEncode(conn.TokenSecret)}";
        var keyBytes = Encoding.UTF8.GetBytes(signingKey);
        var dataBytes = Encoding.UTF8.GetBytes(baseString);

        using var hmac = new HMACSHA256(keyBytes);
        var signature = Convert.ToBase64String(hmac.ComputeHash(dataBytes));

        // Build Authorization header (query params are NOT in the header)
        var headerParams = new Dictionary<string, string>
        {
            ["realm"] = conn.AccountId,
            ["oauth_consumer_key"] = conn.ConsumerKey,
            ["oauth_nonce"] = nonce,
            ["oauth_signature"] = signature,
            ["oauth_signature_method"] = "HMAC-SHA256",
            ["oauth_timestamp"] = timestamp,
            ["oauth_token"] = conn.TokenId,
            ["oauth_version"] = "1.0",
        };

        var headerValue = "OAuth " + string.Join(", ",
            headerParams.Select(kvp => $"{kvp.Key}=\"{PercentEncode(kvp.Value)}\""));

        return headerValue;
    }

    private static string PercentEncode(string value)
        => Uri.EscapeDataString(value);

    /// <summary>
    /// Parses NetSuite's o:errorDetails array and maps each item index back to the
    /// item's description so the error message names the offending line item.
    /// Falls back to the raw error body when parsing fails.
    /// </summary>
    private static string EnrichPushError(string errorBody, IReadOnlyList<PushLineItem> items)
    {
        try
        {
            using var doc = JsonDocument.Parse(errorBody);
            if (!doc.RootElement.TryGetProperty("o:errorDetails", out var details))
                return Truncate(errorBody, 1000);

            var parts = new List<string>();
            foreach (var detail in details.EnumerateArray())
            {
                var message = detail.TryGetProperty("detail", out var d) ? d.GetString() ?? "" : "";
                var path    = detail.TryGetProperty("o:errorPath", out var p) ? p.GetString() ?? "" : "";

                // o:errorPath is typically "item.items[N]" — extract the index to name the item
                string? itemLabel = null;
                var match = System.Text.RegularExpressions.Regex.Match(path, @"items\[(\d+)\]");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var idx) && idx < items.Count)
                {
                    var item = items[idx];
                    itemLabel = !string.IsNullOrEmpty(item.Description)
                        ? $"{item.Description} (NS item {item.NetsuiteItemId})"
                        : $"NS item {item.NetsuiteItemId}";
                }

                parts.Add(itemLabel is not null ? $"{message} — {itemLabel}" : message);
            }

            return parts.Count > 0 ? string.Join("; ", parts) : Truncate(errorBody, 1000);
        }
        catch
        {
            return Truncate(errorBody, 1000);
        }
    }

    private static string Truncate(string s, int max) => s.Length > max ? s[..max] + "…" : s;

    /// <summary>
    /// Builds the NS REST API payload dictionary for a single estimate line item.
    /// Description items use a different field set: no quantity/cost/rate, always PDF-visible.
    /// Dictionary entries with null values are omitted by the serializer.
    /// </summary>
    private static Dictionary<string, object?> BuildLineItemPayload(PushLineItem li)
    {
        var d = new Dictionary<string, object?>
        {
            // NS REST API resolves the item record by id; refName on reference fields is read-only
            // and ignored by NS on writes. The name shown in the estimate comes from the item record itself.
            ["item"] = new { id = li.NetsuiteItemId },
        };

        if (!li.IsDescriptionItem)
        {
            d["quantity"] = li.Qty;
            d["costoverride"] = li.DiscCost;
            d["rate"] = li.SellPrice;
            d["costEstimateType"] = new { id = "CUSTOM" };
            d["costEstimateRate"] = li.DiscCost;
        }

        if (!string.IsNullOrEmpty(li.Description))
            d["description"] = li.Description;

        // custcol_acs_pdf_view controls whether the line appears in the PDF export.
        // Description items are always PDF-visible; regular items respect the per-row flag.
        if (li.PrintInPdf || li.IsDescriptionItem)
            d["custcol_acs_pdf_view"] = true;

        // Per-line location overrides the estimate-level location when present.
        if (!string.IsNullOrEmpty(li.LocationId))
            d["location"] = new { id = li.LocationId };

        return d;
    }

    // ── Raw API DTOs ─────────────────────────────────────────────────────────

    private sealed record DecryptedConn(
        string AccountId,
        string ConsumerKey,
        string ConsumerSecret,
        string TokenId,
        string TokenSecret,
        string? CustomFormId = null);

    private sealed class NetsuiteOpportunityRaw
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("tranid")] public string? TranId { get; set; }
        [JsonPropertyName("entity")] public JsonElement Entity { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("entitystatus")] public JsonElement EntityStatus { get; set; }
        [JsonPropertyName("salesrep")] public JsonElement SalesRep { get; set; }
    }

    // SuiteQL returns BUILTIN.DF() columns as plain strings, not reference objects.
    private sealed class NetsuiteOpportunitySuiteQL
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("tranid")] public string? TranId { get; set; }
        [JsonPropertyName("entity")] public string? Entity { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("entitystatus")] public string? EntityStatus { get; set; }
        [JsonPropertyName("salesrep")] public string? SalesRep { get; set; }
    }

    private sealed class NetsuiteItemRaw
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        // itemid and displayname may be returned as either a plain string or a reference object
        // {"id":"...","refName":"..."} depending on the NetSuite account configuration and item subtype.
        [JsonPropertyName("itemid")] public JsonElement ItemIdRaw { get; set; }
        [JsonPropertyName("displayname")] public JsonElement DisplayNameRaw { get; set; }
        [JsonPropertyName("manufacturer")] public JsonElement ManufacturerRaw { get; set; }
        [JsonPropertyName("custitem_manufacturer")] public string? ManufacturerCustom { get; set; }
        [JsonPropertyName("purchasedescription")] public string? PurchaseDescription { get; set; }
        [JsonPropertyName("cost")] public string? Cost { get; set; }
        // Joined from the pricing table (pricelevel=1, quantity=0 = Base Price).
        // NetSuite item form calls this field "rate"; SuiteQL returns it as "unitprice".
        [JsonPropertyName("unitprice")] public string? Rate { get; set; }
    }

    private static NetsuiteOpportunity Map(NetsuiteOpportunityRaw r) => new()
    {
        Id = r.Id ?? "",
        TranId = r.TranId ?? "",
        Entity = ExtractRefName(r.Entity),
        Title = r.Title ?? "",
        EntityStatus = ExtractRefName(r.EntityStatus),
        SalesRep = ExtractRefName(r.SalesRep),
    };

    private static NetsuiteOpportunity MapSuiteQL(NetsuiteOpportunitySuiteQL r) => new()
    {
        Id = r.Id ?? "",
        TranId = r.TranId ?? "",
        Entity = r.Entity ?? "",
        Title = r.Title ?? "",
        EntityStatus = r.EntityStatus ?? "",
        SalesRep = r.SalesRep ?? "",
    };

    private static NetsuiteItem MapItem(NetsuiteItemRaw r) => new()
    {
        Id = r.Id ?? "",
        // itemid and displayname may be strings or reference objects; try both, prefer itemid.
        ItemId = NullIfEmpty(ExtractRefName(r.ItemIdRaw))
              ?? NullIfEmpty(ExtractRefName(r.DisplayNameRaw))
              ?? "",
        // Prefer the custom text field; fall back to the built-in manufacturer reference.
        Manufacturer = r.ManufacturerCustom ?? ExtractRefName(r.ManufacturerRaw),
        PurchaseDescription = r.PurchaseDescription ?? "",
        Cost = decimal.TryParse(r.Cost, out var c) ? c : 0,
        Rate = decimal.TryParse(r.Rate, out var rt) ? rt : 0,
    };

    /// <summary>
    /// NetSuite reference fields can be either a plain string or a {"id":"x","refName":"y"} object.
    /// This extracts the display name from either form.
    /// </summary>
    private static string ExtractRefName(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString() ?? "",
        JsonValueKind.Object => el.TryGetProperty("refName", out var rn) ? rn.GetString() ?? "" : "",
        _ => "",
    };

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    /// <summary>
    /// Patches the itemVendor sublist on a newly created inventory item.
    /// Confirmed field names from the REST API: sublist = "itemVendor", preferred flag = "preferredVendor".
    /// Failures are logged as warnings rather than thrown — the item was already created.
    /// </summary>
    private async Task PatchItemVendorAsync(string itemId, string vendorId, DecryptedConn conn, CancellationToken ct)
    {
        try
        {
            var url = RecordUrl(conn.AccountId!, $"inventoryitem/{itemId}");
            var patchBody = new
            {
                itemVendor = new
                {
                    items = new[]
                    {
                        new { vendor = new { id = vendorId }, preferredVendor = true }
                    }
                }
            };

            var patchReq = BuildRequest(new HttpMethod("PATCH"), url, conn);
            patchReq.Content = JsonContent.Create(patchBody, options: _json);

            var resp = await _http.SendAsync(patchReq, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("itemVendor PATCH returned {Status} for item {ItemId}: {Body}",
                    (int)resp.StatusCode, itemId, err[..Math.Min(err.Length, 400)]);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "itemVendor PATCH threw for item {ItemId}", itemId);
        }
    }

    /// <summary>
    /// Builds the POST body for creating an inventory item.
    /// Null fields are omitted by the serializer (DefaultIgnoreCondition = WhenWritingNull),
    /// so optional reference fields like vendor, manufacturer, and UoM are only sent when selected.
    /// salesdescription is always included — NetSuite requires it on inventory items.
    /// </summary>
    private static object BuildCreateItemBody(CreateNetsuiteItemRequest req) => new
    {
        itemid = req.ItemId,
        purchasedescription = req.PurchaseDescription,
        // Always send salesdescription — NetSuite requires it. Defaults to purchasedescription when
        // the user hasn't set a separate sales description.
        salesdescription = string.IsNullOrWhiteSpace(req.SalesDescription)
            ? req.PurchaseDescription
            : req.SalesDescription,
        cost = req.Cost,
        rate = req.Rate,
        mpn = req.Mpn,
        manufacturer = req.Manufacturer,
        unitstype = req.UnitTypeId is not null ? new { id = req.UnitTypeId } : (object?)null,
        istaxable = true,
    };

    private sealed class NetsuiteDescriptionItemRaw
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("itemid")] public string? ItemId { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
    }

    private sealed class NetsuiteVendorRaw
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("entityid")] public string? EntityId { get; set; }
    }

    private sealed class NetsuiteUnitTypeRaw
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        // SuiteQL column is "name", not "unittype".
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private sealed class NetsuiteManufacturerRaw
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private async Task<List<(string Id, string Name)>> FetchSuiteQLLookupAsync(string sql, CancellationToken ct)
    {
        var conn = await GetDecryptedConnAsync(ct);
        if (conn is null) return [];
        var url = $"https://{NormalizeAccountId(conn.AccountId!)}.suitetalk.api.netsuite.com/services/rest/query/v1/suiteql?limit=500";
        var req = BuildRequest(HttpMethod.Post, url, conn);
        req.Headers.Add("Prefer", "transient");
        req.Content = JsonContent.Create(new { q = sql }, options: _json);
        var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var errBody = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("NetSuite SuiteQL lookup failed ({Status}) for SQL [{Sql}]: {Body}",
                (int)resp.StatusCode, sql, errBody.Length > 600 ? errBody[..600] : errBody);
            return [];
        }
        var body = await resp.Content.ReadFromJsonAsync<NetsuiteListResponse<NetsuiteLookupRaw>>(_json, ct);
        return body?.Items.Where(x => !string.IsNullOrEmpty(x.Id))
            .Select(x => (x.Id!, x.Name ?? x.Id!)).ToList() ?? [];
    }

    private sealed class NetsuiteLookupRaw
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private sealed class NetsuiteEntityIdRaw
    {
        [JsonPropertyName("entity")] public JsonElement EntityRaw { get; set; }
    }

    private sealed class NetsuiteAddressBookResponse
    {
        [JsonPropertyName("items")] public List<NetsuiteAddressEntryRaw>? Items { get; set; }
    }

    private sealed class NetsuiteAddressEntryRaw
    {
        [JsonPropertyName("id")] public JsonElement Id { get; set; }
        [JsonPropertyName("label")] public string? Label { get; set; }
        [JsonPropertyName("defaultBilling")] public bool DefaultBilling { get; set; }
        [JsonPropertyName("defaultShipping")] public bool DefaultShipping { get; set; }
        [JsonPropertyName("addressBookAddress_text")] public string? AddressText { get; set; }
        [JsonPropertyName("addressBookAddress")] public NetsuiteAddressBookAddressRaw? AddressBookAddress { get; set; }
    }

    private sealed class NetsuiteAddressBookAddressRaw
    {
        [JsonPropertyName("addrText")] public string? AddrText { get; set; }
    }
}
