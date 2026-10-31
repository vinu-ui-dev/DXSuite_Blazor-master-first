using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using DXSuite.Shared.Entities;
using DXSuite.Shared.Entities.Identity;
using DXSuite.Shared.Services.Identity;
using DXSuite.Shared.Models;
using DXSuite.Shared.Services;
using DXSuite.Shared.Multitenancy;

namespace DXSuite.Shared.Authorization
{
    public class SessionState : ISessionState
    {
        private readonly IIdentityDbContext _dbContext;
        private readonly ILogger<SessionState> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly IOptions<FeatureFlags> _featureFlags;
        private readonly ITenantContext? _tenantContext;
        private readonly IRolePermissionService _rolePermissionService;

        public SessionState(
            IIdentityDbContext dbContext,
            ILogger<SessionState> logger,
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            IOptions<FeatureFlags> featureFlags,
            IRolePermissionService rolePermissionService,
            ITenantContext? tenantContext = null)
        {
            _dbContext = dbContext;
            _logger = logger;
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _featureFlags = featureFlags;
            _rolePermissionService = rolePermissionService;
            _tenantContext = tenantContext;
        }

        // SessionState stores TblUser internally but exposes ICurrentUser via the interface
        public TblUser? UserInternal { get; set; }
        public ICurrentUser? User { get => UserInternal; set => UserInternal = value as TblUser; }
        public string? UserName => UserInternal?.UserPrincipalName;
        public string? Role => RoleEnum.ToString();
        public Roles.Role RoleEnum { get; set; } = Roles.Role.NewUser;
        public int ActualRoleId { get; private set; } = (int)Roles.Role.NewUser;
        public string? ActualRoleName { get; private set; }
        public List<int> AssignedLocationIds { get; set; } = new List<int>();
        public bool IsDisabled { get; private set; }
        public Dictionary<int, ResolvedAccess?> LocationAccessMap { get; set; } = new();
        public string? PreferredLanguage { get; set; }
        public string? ProfilePhotoDataUri { get; set; }
        public string UserInitials { get; set; } = "";
        public TaskCompletionSource<bool> SessionLoaded { get; private set; } = new();
        public Guid? PlatformUserId { get; private set; }
        private IReadOnlySet<string>? _bridgePermissions;
        public IReadOnlyList<TenantMembershipInfo> TenantMemberships { get; private set; } = Array.Empty<TenantMembershipInfo>();
        public bool IsPlatformOperator { get; private set; }
        public bool IsTenantAdmin { get; private set; }
        public bool IsAuditor { get; private set; }

        public async Task LoadUserAsync(string username, ClaimsPrincipal? claimsPrincipal = null)
        {
            string firstName = "";
            string lastName = "";
            string friendlyName = "";
            if (claimsPrincipal is not null)
            {
                firstName = claimsPrincipal.FindFirst(ClaimTypes.GivenName)?.Value
                            ?? claimsPrincipal.FindFirst("given_name")?.Value ?? "";
                lastName = claimsPrincipal.FindFirst(ClaimTypes.Surname)?.Value
                           ?? claimsPrincipal.FindFirst("family_name")?.Value ?? "";
                friendlyName = claimsPrincipal.FindFirst("name")?.Value
                               ?? claimsPrincipal.FindFirst(ClaimTypes.Name)?.Value ?? "";
            }

            if (string.IsNullOrEmpty(firstName) && string.IsNullOrEmpty(lastName))
            {
                if (username.Contains('\\'))
                {
                    (firstName, lastName) = SplitNameTokens(username.Split('\\')[1]);
                }
                else if (username.Contains("@"))
                {
                    (firstName, lastName) = SplitNameTokens(username.Split('@')[0]);
                }
                else if (username.Contains(" "))
                {
                    var nameParts = username.Split(" ", StringSplitOptions.RemoveEmptyEntries);

                    if (nameParts.Length >= 2)
                    {
                        firstName = CapitalizeFirstLetter(nameParts[0]);
                        lastName = CapitalizeFirstLetter(nameParts[nameParts.Length - 1]);
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(friendlyName))
                friendlyName = $"{firstName} {lastName}".Trim();
            if (string.IsNullOrWhiteSpace(friendlyName) && username.Contains("@"))
                friendlyName = string.Join(" ",
                    username.Split('@')[0]
                            .Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(CapitalizeFirstLetter));

            // Use a scoped DbContext for user lookup and upsert — the circuit-lifetime _dbContext
            // can have a closed SQL connection if the circuit idles past the pool timeout.
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

            var user = await db.TblUsers.AsNoTracking().FirstOrDefaultAsync(x => x.UserPrincipalName == username);

            if (user == null)
            {
                var newUser = new TblUser
                {
                    UserPrincipalName = username,
                    GivenName = firstName,
                    Surname = lastName,
                    FriendlyName = friendlyName,
                    RoleId = (int)Roles.Role.NewUser
                };

                db.TblUsers.Add(newUser);
                try
                {
                    await db.SaveChangesAsync();

                    await NotifyAdminsOfNewUserAsync(newUser);

                    UserInternal = newUser;
                    await ApplyRoleAsync(newUser, db);
                    UserInitials = DeriveInitials(newUser.GivenName, newUser.Surname, newUser.UserPrincipalName);
                    await TryLoadProfilePhotoAsync();
                    await TrySyncDomainGroupsAsync(newUser.UserId, newUser.UserPrincipalName);
                    await TrySyncAdGroupsAsync(newUser.UserId, claimsPrincipal);
                    await LoadLocationAccessMapAsync(newUser.UserId);
                    await TryBootstrapPlatformIdentityAsync(username, newUser.UserId);
                    SessionLoaded.SetResult(true);
                    return;
                }
                catch (DbUpdateException ex) when (ex.IsUniqueViolation())
                {
                    // A concurrent circuit (Blazor prerender + interactive both run LoadUserAsync
                    // on first visit) created the row between our existence check and the insert.
                    // Removing the Added entity detaches it so a later save doesn't retry the insert.
                    db.TblUsers.Remove(newUser);
                    user = await db.TblUsers.AsNoTracking().FirstAsync(x => x.UserPrincipalName == username);
                }
            }

            bool updated = false;
            if (string.IsNullOrEmpty(user.GivenName)) { user.GivenName = firstName; updated = true; }
            if (string.IsNullOrEmpty(user.Surname)) { user.Surname = lastName; updated = true; }
            if (string.IsNullOrEmpty(user.FriendlyName)) { user.FriendlyName = friendlyName; updated = true; }

            if (updated)
            {
                db.TblUsers.Update(user);
                await db.SaveChangesAsync();
            }

            UserInternal = user;
            await ApplyRoleAsync(user, db);

            if (user.IsDisabled)
            {
                IsDisabled = true;
                UserInitials = DeriveInitials(user.GivenName, user.Surname, user.UserPrincipalName);
                SessionLoaded.SetResult(true);
                return;
            }

            // Parse language preference from ExtendedData JSON
            if (!string.IsNullOrEmpty(user.ExtendedData))
            {
                try
                {
                    var extData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(user.ExtendedData);
                    if (extData?.TryGetValue("preferredLanguage", out var langEl) == true)
                    {
                        PreferredLanguage = langEl.GetString();
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse ExtendedData for user {UserId}", user.UserId);
                }
            }

            // Domain-based group auto-join (must run before access map so new groups inform access)
            await TrySyncDomainGroupsAsync(user.UserId, user.UserPrincipalName);

            // Resolve per-location access — sole source of AssignedLocationIds
            await LoadLocationAccessMapAsync(user.UserId);

            UserInitials = DeriveInitials(user.GivenName, user.Surname, user.UserPrincipalName);
            await TryLoadProfilePhotoAsync();
            await TrySyncAdGroupsAsync(user.UserId, claimsPrincipal);
            await TryBootstrapPlatformIdentityAsync(username, user.UserId);
            SessionLoaded.SetResult(true);
        }

        /// <summary>
        /// Populates the session from a validated Editor bridge identity: no TblUser row is created
        /// or looked up, and permissions come straight from <paramref name="identity"/> rather than
        /// IRolePermissionService (see GetPermissionsAsync/HasPermissionAsync below). RoleEnum is set
        /// only so legacy role checks elsewhere see a sane, least-privilege-leaning value; the real
        /// gate for bridge users is the permission set itself, not RoleEnum.
        /// </summary>
        public Task LoadBridgeIdentityAsync(BridgeSessionIdentity identity)
        {
            UserInternal = null;
            _bridgePermissions = identity.Permissions;
            ActualRoleName = identity.RoleName;
            RoleEnum = identity.RoleName switch
            {
                "CustomerAdmin" => Roles.Role.Architect,
                "CustomerUser" => Roles.Role.Contributor,
                _ => Roles.Role.NewUser,
            };
            ActualRoleId = (int)RoleEnum;
            UserInitials = DeriveInitials(null, null, identity.DisplayName);
            SessionLoaded.TrySetResult(true);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Sets RoleEnum to the effective built-in role: the user's own role when built-in,
        /// otherwise the custom role's BaseRoleId. Missing/invalid base roles fall back to
        /// NewUser (least privilege) rather than failing open.
        /// </summary>
        private async Task ApplyRoleAsync(TblUser user, IIdentityDbContext db)
        {
            // Development switch: every signed-in user gets the built-in Admin role for this session, which
            // every permission, menu and location-access check already treats as "allowed". Nothing is stored.
            if (_configuration.GetValue<bool>("Authorization:BypassPermissionChecks"))
            {
                ActualRoleId = (int)Roles.Role.Admin;
                RoleEnum = Roles.Role.Admin;
                ActualRoleName = RoleEnum.ToString();
                return;
            }

            ActualRoleId = user.RoleId;

            if (user.RoleId is >= (int)Roles.Role.Admin and <= (int)Roles.Role.NewUser)
            {
                RoleEnum = (Roles.Role)user.RoleId;
                ActualRoleName = RoleEnum.ToString();
                return;
            }

            var role = await db.TblUserRoles.AsNoTracking().FirstOrDefaultAsync(r => r.RoleId == user.RoleId);
            ActualRoleName = role?.RoleName;

            if (role?.BaseRoleId is >= (int)Roles.Role.Admin and <= (int)Roles.Role.NewUser)
            {
                RoleEnum = (Roles.Role)role.BaseRoleId.Value;
            }
            else
            {
                _logger.LogWarning("User {User} has custom role {RoleId} with missing or invalid base role; treating as NewUser",
                    user.UserPrincipalName, user.RoleId);
                RoleEnum = Roles.Role.NewUser;
            }
        }

        public event Action? LocationAccessChanged;

        public async Task RefreshLocationAccessAsync()
        {
            if (UserInternal == null) return;
            await LoadLocationAccessMapAsync(UserInternal.UserId);
            LocationAccessChanged?.Invoke();
        }

        public Task<IReadOnlySet<string>> GetPermissionsAsync()
            => _bridgePermissions is not null ? Task.FromResult(_bridgePermissions) : _rolePermissionService.GetPermissionsForRoleAsync(ActualRoleId);

        public Task<bool> HasPermissionAsync(string permissionKey)
            => _bridgePermissions is not null ? Task.FromResult(_bridgePermissions.Contains(permissionKey)) : _rolePermissionService.HasPermissionAsync(ActualRoleId, permissionKey);

        /// <summary>
        /// Finds or creates the platform identity and tenant membership for the logged-in user.
        /// No-op when the Platform DB is not configured. Non-blocking on failure.
        /// </summary>
        private async Task TryBootstrapPlatformIdentityAsync(string email, int tenantUserId)
        {
            if (_tenantContext == null) return;
            // Non-email principals (Windows domain\user, display-name format) cannot be federated
            // cross-tenant — the platform identity model uses email as the sole federation key.
            if (!email.Contains('@')) return;

            // Capture TenantId from the already-populated circuit-scoped context before opening a
            // sub-scope. Sub-scopes created via IServiceScopeFactory receive a brand-new TenantContext
            // instance that TenantResolutionMiddleware never populates — reading it would throw.
            var tenantId = _tenantContext.TenantId;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var platformService = scope.ServiceProvider.GetService<IPlatformIdentityService>();
                if (platformService == null) return;

                var result = await platformService.BootstrapAsync(email, tenantId, tenantUserId);
                if (result == null) return;

                PlatformUserId = result.PlatformUserId;
                IsPlatformOperator = result.IsPlatformOperator;
                TenantMemberships = await platformService.GetTenantMembershipsAsync(result.PlatformUserId);
                IsTenantAdmin = await platformService.IsTenantAdminAsync(result.PlatformUserId, tenantId);
                IsAuditor = await platformService.HasPlatformRoleAsync(result.PlatformUserId, PlatformRoles.Auditor);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Platform identity bootstrap failed for {Email} — login continues normally", email);
            }
        }

        /// <summary>
        /// Extracts Azure AD group OIDs from claims and syncs membership + discovery.
        /// Wrapped in try-catch so login never fails due to group sync errors.
        /// </summary>
        private async Task TrySyncAdGroupsAsync(int userId, ClaimsPrincipal? claimsPrincipal)
        {
            var authType = _configuration["Authentication:AuthenticationType"]?.ToUpper();
            if (authType != "AZURE" || claimsPrincipal == null) return;

            try
            {
                // Extract group OIDs from the "groups" claim, validating each is a GUID
                var groupOids = claimsPrincipal.Claims
                    .Where(c => c.Type == "groups")
                    .Select(c => c.Value)
                    .Where(v => Guid.TryParse(v, out _))
                    .ToList();

                // Overage handling: when too many groups exist, Azure AD omits the groups claim
                // and signals via _claim_names that groups must be fetched from Graph
                if (groupOids.Count == 0 && claimsPrincipal.HasClaim(c => c.Type == "_claim_names"))
                {
                    var claimNamesValue = claimsPrincipal.FindFirstValue("_claim_names");
                    if (claimNamesValue?.Contains("groups", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        groupOids = await FetchGroupOidsFromGraphAsync();
                    }
                }

                if (groupOids.Count == 0) return;

                using var scope = _scopeFactory.CreateScope();
                var adGroupSync = scope.ServiceProvider.GetRequiredService<IAdGroupSyncService>();

                // Always sync membership for existing ad-sync groups
                await adGroupSync.SyncMembershipAsync(userId, groupOids);

                // Discovery is feature-flagged — the service checks internally.
                // Graph scope must stay alive for the duration of the discovery call.
                if (_featureFlags.Value.GroupDiscovery)
                {
                    try
                    {
                        using var graphScope = _scopeFactory.CreateScope();
                        var graphClient = graphScope.ServiceProvider.GetService<GraphServiceClient>();
                        await adGroupSync.DiscoverGroupsAsync(groupOids, graphClient);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Could not acquire Graph client for group discovery");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AD group sync failed for user {UserId} — login continues normally", userId);
            }
        }

        /// <summary>
        /// Fetches group membership OIDs from Microsoft Graph when the token groups claim
        /// exceeds the overage limit (200+ groups).
        /// </summary>
        private async Task<List<string>> FetchGroupOidsFromGraphAsync()
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var graphClient = scope.ServiceProvider.GetService<GraphServiceClient>();
                if (graphClient == null) return new List<string>();

                var memberOf = await graphClient.Me.MemberOf.Request().Select("id").Top(999).GetAsync();
                var oids = new List<string>();

                var page = memberOf;
                while (page != null)
                {
                    foreach (var obj in page.CurrentPage)
                    {
                        if (obj is Group group && Guid.TryParse(group.Id, out _))
                        {
                            oids.Add(group.Id);
                        }
                    }

                    page = page.NextPageRequest != null
                        ? await page.NextPageRequest.GetAsync()
                        : null;
                }

                return oids;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch group OIDs from Graph (overage fallback)");
                return new List<string>();
            }
        }

        /// <summary>
        /// Notifies all Admin-role users when a new user registers.
        /// Failure is non-blocking — notification errors must never prevent login.
        /// </summary>
        private async Task NotifyAdminsOfNewUserAsync(TblUser newUser)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var notificationService = scope.ServiceProvider.GetService<INotificationService>();
                var emailService = scope.ServiceProvider.GetService<IEmailService>();
                if (notificationService == null || emailService == null) return;
                var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

                var adminUsers = await db.TblUsers
                    .AsNoTracking()
                    .Where(u => u.RoleId == (int)Roles.Role.Admin)
                    .ToListAsync();

                if (adminUsers.Count == 0) return;

                var displayName = !string.IsNullOrEmpty(newUser.FriendlyName)
                    ? newUser.FriendlyName
                    : newUser.UserPrincipalName;

                var adminUserIds = adminUsers.Select(u => u.UserId).ToList();
                await notificationService.CreateBulkNotificationsAsync(
                    adminUserIds,
                    "New user registered",
                    $"{displayName} ({newUser.UserPrincipalName}) has registered and needs a role assignment.",
                    "new-user",
                    null,
                    "/Settings");

                if (await emailService.IsConfiguredAsync())
                {
                    foreach (var admin in adminUsers)
                    {
                        if (!string.IsNullOrEmpty(admin.UserPrincipalName))
                        {
                            await emailService.SendAsync(
                                admin.UserPrincipalName,
                                "New user registration — action required",
                                $"<p>{displayName} (<strong>{newUser.UserPrincipalName}</strong>) has registered in Director and needs a role assignment.</p><p>Go to Settings to assign a role.</p>");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to notify admins of new user {UPN} — login continues normally", newUser.UserPrincipalName);
            }
        }

        private async Task LoadLocationAccessMapAsync(int userId)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var identityDb = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();
                var accessService = scope.ServiceProvider.GetService<ILocationAccessService>();

                LocationAccessMap = new Dictionary<int, ResolvedAccess?>();
                AssignedLocationIds = new List<int>();

                if (accessService == null) return;

                // Admins get implicit full access to every location, matching the Admin
                // short-circuit in LocationAccessService.ResolveAccessAsync. Deliberately not
                // conditional on having zero explicit rules — the creator auto-grant rule would
                // otherwise collapse an Admin's view to only their explicit rules on refresh.
                if (RoleEnum == Roles.Role.Admin)
                {
                    var allLocationIds = await accessService.GetAllLocationIdsAsync();
                    foreach (var locationId in allLocationIds)
                    {
                        LocationAccessMap[locationId] = new ResolvedAccess { AccessLevel = "full" };
                        AssignedLocationIds.Add(locationId);
                    }
                    return;
                }

                // Discover user's group memberships
                var userGroupIds = await identityDb.TblUserGroupMemberships
                    .Where(m => m.UserId == userId)
                    .Select(m => m.UserGroupId)
                    .ToListAsync();

                // Discover all locations this user has access to via direct + group rules,
                // then resolve the access level for each
                var ruleLocationIds = await accessService.GetAccessibleLocationIdsAsync(userId, userGroupIds);
                foreach (var locationId in ruleLocationIds)
                {
                    var access = await accessService.ResolveAccessAsync(userId, locationId);
                    LocationAccessMap[locationId] = access;
                    AssignedLocationIds.Add(locationId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load location access map for user {UserId}", userId);
                LocationAccessMap = new Dictionary<int, ResolvedAccess?>();
            }
        }

        /// <summary>
        /// Syncs domain-based group membership for the user during login.
        /// Groups with an "EmailDomain" metadata key matching the user's UPN domain are auto-joined.
        /// </summary>
        private async Task TrySyncDomainGroupsAsync(int userId, string? userPrincipalName)
        {
            if (!_featureFlags.Value.DomainGroupAssignment) return;
            if (string.IsNullOrEmpty(userPrincipalName) || !userPrincipalName.Contains('@')) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var domainGroupService = scope.ServiceProvider.GetRequiredService<IDomainGroupAssignmentService>();
                await domainGroupService.SyncDomainGroupsAsync(userId, userPrincipalName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Domain group sync failed for user {UserId} — login continues normally", userId);
            }
        }

        public string CapitalizeFirstLetter(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            return $"{input[0].ToString().ToUpper()}{input.Substring(1).ToLower()}";
        }

        // Splits an identity local part (e.g. "john.smith", "john_q_smith") into a first and
        // last name on common separators. Single-token inputs ("jsmith") yield first name only.
        private (string firstName, string lastName) SplitNameTokens(string localPart)
        {
            var tokens = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                return (string.Empty, string.Empty);
            }
            if (tokens.Length == 1)
            {
                return (CapitalizeFirstLetter(tokens[0]), string.Empty);
            }
            return (CapitalizeFirstLetter(tokens[0]), CapitalizeFirstLetter(tokens[^1]));
        }

        private static string DeriveInitials(string? givenName, string? surname, string? upn)
        {
            var first = givenName?.Trim();
            var last = surname?.Trim();

            if (!string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(last))
                return $"{char.ToUpper(first[0])}{char.ToUpper(last[0])}";

            if (!string.IsNullOrEmpty(first))
                return $"{char.ToUpper(first[0])}";

            if (!string.IsNullOrEmpty(upn))
            {
                var local = upn.Split('@')[0].Split('.')[0].Split('\\').Last();
                return !string.IsNullOrEmpty(local) ? $"{char.ToUpper(local[0])}" : "?";
            }

            return "?";
        }

        private async Task TryLoadProfilePhotoAsync()
        {
            var authType = _configuration["Authentication:AuthenticationType"]?.ToUpper();
            if (authType != "AZURE") return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var graphClient = scope.ServiceProvider.GetService<GraphServiceClient>();
                if (graphClient == null) return;

                using var photoStream = await graphClient.Me.Photo.Content.Request().GetAsync();
                if (photoStream != null)
                {
                    using var ms = new MemoryStream();
                    await photoStream.CopyToAsync(ms);
                    var base64 = Convert.ToBase64String(ms.ToArray());
                    ProfilePhotoDataUri = $"data:image/jpeg;base64,{base64}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not fetch profile photo from Microsoft Graph");
            }
        }
    }
}
