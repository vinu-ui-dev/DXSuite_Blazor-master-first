using System.Reflection;
using System.Reflection.Emit;
using DXSuite.Aviation;
using DXSuite.Aviation.Pages.FlightDataEditor;
using DXSuite.Connectors.Services.CoreApi;
using DXSuite.Host.Multitenancy;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.Module;
using DXSuite.Shared.Multitenancy;
using DXSuite.Shared.Services.Identity;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace DXSuite.Director.Tests;

// Section numbers in this file refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// B-20 (FDE-01): the Flight Data Editor opens only for a tenant with its flag on and a role holding
/// page:flight-data-editor, whether reached from the menu, the Home page or its URL; that one key covers editing,
/// Add Flight and the return review; 1.1 keeps the tab shell but no counter allocation stub and no test buttons.
/// The URL gate and the pages' own refusal are proved in TenantFeatureRouteGateTests and PermissionGatedPagesTests.
/// </summary>
public class FlightDataEditorHomeTests
{
    private const string EditorHref = "/Flight-Data-Editor";

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public async Task The_menu_offers_the_editor_only_with_the_flag_and_the_key(bool flagOn, bool holdsKey, bool offered)
    {
        var session = holdsKey ? new KeySession(PermissionKeys.PageFlightDataEditor) : new KeySession(PermissionKeys.ContentUpload);

        var items = TenantNav.Items([new AviationModule()], Tenant(flagOn), await session.GetPermissionsAsync(), session);

        Assert.Equal(offered, items.Any(n => n.Href == EditorHref));
    }

    // The real Admin role bypasses permission checks (null permissions), but not the tenant's flag (DEV-06).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_menu_offers_the_editor_to_Admin_exactly_when_the_flag_is_on(bool flagOn)
    {
        var admin = new KeySession((int)Roles.Role.Admin);

        var items = TenantNav.Items([new AviationModule()], Tenant(flagOn), permissions: null, admin);

        Assert.Equal(flagOn, items.Any(n => n.Href == EditorHref));
    }

    [Fact]
    public void A_tenant_without_the_Aviation_module_is_not_offered_the_editor()
    {
        var items = TenantNav.Items([new AviationModule()], Tenant(flagOn: true, aviation: false), permissions: null,
            new KeySession((int)Roles.Role.Admin));

        Assert.Empty(items);
    }

    // The Home page's quick link is a way in too: it is the module's first entry the menu would offer.
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Home_does_not_link_to_the_editor_without_the_flag_and_the_key(bool flagOn, bool holdsKey)
    {
        var session = holdsKey ? new KeySession(PermissionKeys.PageFlightDataEditor) : new KeySession(PermissionKeys.ContentUpload);

        var link = TenantNav.QuickLink(new AviationModule(), Tenant(flagOn), await session.GetPermissionsAsync(), session);

        Assert.Null(link);
    }

    [Fact]
    public async Task Home_links_to_the_editor_with_the_flag_and_the_key()
    {
        var session = new KeySession(PermissionKeys.PageFlightDataEditor);

        var link = TenantNav.QuickLink(new AviationModule(), Tenant(flagOn: true), await session.GetPermissionsAsync(), session);

        Assert.Equal(EditorHref, link?.Href);
    }

    // Every URL into the editor carries both gates, so a page added under it later cannot skip either.
    [Fact]
    public void Every_editor_URL_is_behind_the_flag_and_the_key()
    {
        var routed = EditorTypes().Where(t => t.GetCustomAttributes<RouteAttribute>().Any()).OrderBy(t => t.Name).ToList();
        var keyChecked = PermissionGatedPagesTests.RoutedPages
            .Where(row => (string)row[1] == PermissionKeys.PageFlightDataEditor)
            .Select(row => (Type)row[0])
            .OrderBy(t => t.Name);

        Assert.Equal([typeof(FlightDataEditor), typeof(FlightManagement)], routed);
        Assert.All(routed, page => Assert.Contains(page.GetCustomAttributes<RequiresTenantFeatureAttribute>(),
            gate => gate.Feature == AviationFeatureFlags.FlightDataEditorFeature));
        Assert.Equal(routed, keyChecked);
    }

    // A-17: editing, Add Flight and the return review are parts of the gated page, with no URL of their own.
    [Theory]
    [InlineData(typeof(ModifyArrivalFlightModal))]
    [InlineData(typeof(ModifyDepartureFlightModal))]
    [InlineData(typeof(AddArrivalFlightModal))]
    [InlineData(typeof(AddDepartureFlightModal))]
    [InlineData(typeof(OverrideFlightsModal))]
    [InlineData(typeof(OverrideArrivalFlightModal))]
    [InlineData(typeof(OverrideDepartureFlightModal))]
    public void Editing_Add_Flight_and_the_review_are_reached_only_through_the_gated_page(Type part)
    {
        Assert.Empty(part.GetCustomAttributes<RouteAttribute>());
        Assert.Contains(part, IlReferences.TypesUsedBy(typeof(FlightManagement)));
    }

    [Fact]
    public void The_one_key_the_editor_gates_on_is_page_flight_data_editor()
    {
        var defined = RolePermissionService.EnumerateDefinedPermissions(HostModules()).Select(p => p.Key).ToHashSet();

        var checkedKeys = EditorTypes().SelectMany(IlReferences.StringsIn).Where(defined.Contains).ToHashSet();

        // config-tab:coreapi-connection gates nothing in the editor: it only decides whether an unconfigured connection
        // sends the user to its Settings tab (CAPI-10) or tells them to ask an administrator.
        Assert.Equal(new HashSet<string> { PermissionKeys.PageFlightDataEditor, PermissionKeys.ConfigTabCoreApiConnection }, checkedKeys);
    }

    [Fact]
    public void No_view_only_key_for_the_editor_exists()
    {
        var editorKeys = RolePermissionService.EnumerateDefinedPermissions(HostModules())
            .Select(p => p.Key)
            .Where(k => k.Contains("flight", StringComparison.OrdinalIgnoreCase));

        Assert.Equal([PermissionKeys.PageFlightDataEditor], editorKeys);
    }

    [Fact]
    public void No_DELETE_LATER_button_remains_in_the_editor()
    {
        var sources = Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src", "DXSuite.Aviation"), "*.razor", SearchOption.AllDirectories);

        Assert.DoesNotContain(sources, f => File.ReadAllText(f).Contains("DELETE LATER", StringComparison.OrdinalIgnoreCase));
    }

    // G-56: the counter allocation page is 1.2's (B-132); 1.1 has neither the stub nor anything left of it.
    [Fact]
    public void No_counter_allocation_stub_remains()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var sep = Path.DirectorySeparatorChar;
        var leftovers = Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}bin{sep}") && !f.Contains($"{sep}obj{sep}"))
            .Where(f => f.EndsWith(".razor") || f.EndsWith(".cs") || f.EndsWith(".css"))
            .Where(f => Path.GetFileName(f).Contains("CounterAllocation", StringComparison.OrdinalIgnoreCase)
                        || File.ReadAllText(f).Replace(" ", "").Contains("CounterAllocation", StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(src, f));

        Assert.Empty(leftovers);
    }

    // G-56: the shell stays so 1.2 can add its tabs; in 1.1 Flight Management is its only tab.
    [Fact]
    public void The_tab_shell_stays_with_Flight_Management_as_its_one_tab()
    {
        var tab = Assert.Single(FlightDataEditor.Tabs);

        Assert.Equal("Flight Management", tab.Label);
        Assert.Equal(typeof(FlightManagement), tab.ComponentType);
    }

    // CAPI-01: the editor and its field settings send no HTTP request of their own; CoreApiClient sends them all.
    // Live updates arrive over CoreAPI's hub, which B-11's watcher owns (CAPI-05), not over HTTP.
    [Fact]
    public void The_editor_calls_CoreAPI_only_through_CoreApiClient()
    {
        var editorParts = EditorTypes()
            .Concat(typeof(CoreApiClient).Assembly.GetTypes().Where(t => t.Namespace == "DXSuite.Connectors.Components.CoreApi"))
            .ToList();

        var http = editorParts
            .SelectMany(t => IlReferences.TypesUsedBy(t).Select(used => (t, used)))
            .Where(p => p.used.Namespace == "System.Net.Http")
            .Select(p => $"{p.t.Name} uses {p.used.Name}");

        Assert.Empty(http);
        Assert.Contains(typeof(CoreApiClient), IlReferences.TypesUsedBy(typeof(FlightManagement)));
    }

    private static TenantContext Tenant(bool flagOn, bool aviation = true)
    {
        var tenant = new TenantContext();
        tenant.Populate(new TenantRecord
        {
            TenantId = "t1",
            Subdomain = "acme",
            CustomerCode = "ACME",
            DisplayName = "Acme",
            Modules = new TenantModuleConfig { Aviation = aviation },
            Features = new TenantFeatureFlags { Aviation = new AviationFeatureFlags { FlightDataEditor = flagOn } },
        });
        return tenant;
    }

    /// <summary>The editor's own types, the compiler's nested types (async methods, lambdas) included.</summary>
    private static IEnumerable<Type> EditorTypes() =>
        typeof(FlightDataEditor).Assembly.GetTypes().Where(t => t.Namespace == typeof(FlightDataEditor).Namespace);

    private static IEnumerable<IBlazorModule> HostModules()
    {
        var host = Assembly.Load("DXSuite.Host");
        return host.GetReferencedAssemblies()
            .Select(Assembly.Load)
            .Append(host)
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(IBlazorModule).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })
            .Where(t => t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (IBlazorModule)Activator.CreateInstance(t)!)
            .ToList();
    }

    private static string RepoRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "DXSuite.sln"))) root = root.Parent;
        return root?.FullName ?? throw new InvalidOperationException("DXSuite.sln not found above the test output folder.");
    }

    /// <summary>
    /// What a type's compiled code refers to: the types of its fields and properties, and every type and string its
    /// method bodies name. Reading the IL catches a use inside a method body (a local HttpClient, a key passed
    /// inline) that the declared members alone would not show.
    /// </summary>
    private static class IlReferences
    {
        private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                              BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(o => o.Value);

        public static IEnumerable<string> StringsIn(Type type) =>
            Operands(type).Where(o => o.Op.OperandType == OperandType.InlineString)
                .Select(o => o.Method.Module.ResolveString(o.Token));

        public static IReadOnlySet<Type> TypesUsedBy(Type type)
        {
            var used = new HashSet<Type>();
            foreach (var f in type.GetFields(Declared)) used.Add(f.FieldType);
            foreach (var p in type.GetProperties(Declared)) used.Add(p.PropertyType);
            foreach (var o in Operands(type).Where(o => o.Op.OperandType is OperandType.InlineMethod
                         or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok))
            {
                var member = o.Method.Module.ResolveMember(o.Token, type.IsGenericType ? type.GetGenericArguments() : null,
                    o.Method.IsGenericMethod ? o.Method.GetGenericArguments() : null);
                if (member is Type named) used.Add(named);
                else if (member?.DeclaringType is { } declaring) used.Add(declaring);
            }
            return used;
        }

        private static IEnumerable<(OpCode Op, int Token, MethodBase Method)> Operands(Type type)
        {
            foreach (var method in type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared)))
            {
                var il = method.GetMethodBody()?.GetILAsByteArray();
                if (il is null) continue;
                for (var i = 0; i < il.Length;)
                {
                    var op = OpCodesByValue[il[i] == 0xFE ? unchecked((short)(0xFE00 | il[i + 1])) : il[i]];
                    i += op.Size;
                    var size = OperandSize(op.OperandType, il, i);
                    if (op.OperandType is OperandType.InlineString or OperandType.InlineMethod or OperandType.InlineField
                        or OperandType.InlineType or OperandType.InlineTok)
                        yield return (op, BitConverter.ToInt32(il, i), method);
                    i += size;
                }
            }
        }

        private static int OperandSize(OperandType type, byte[] il, int at) => type switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, at),
            _ => 4,
        };
    }
}
