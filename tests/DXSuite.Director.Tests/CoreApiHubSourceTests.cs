namespace DXSuite.Director.Tests;

/// <summary>
/// CAPI-05 and G-58 in the source: Director opens CoreAPI hub connections in one place only (the watcher's and
/// each instance's listener both use it), and no hub address, header or key is written into the code (G-08).
/// </summary>
public class CoreApiHubSourceTests
{
    private static readonly string Root = FindRoot();

    [Fact]
    public void Only_the_shared_hub_connection_opens_a_CoreAPI_hub_connection()
    {
        var shared = Path.Combine(Root, "src", "DXSuite.Connectors", "Services", "CoreApi", "Hub", "CoreApiHubConnection.cs");

        var offenders = SourceFiles()
            .Where(f => !string.Equals(f, shared, StringComparison.OrdinalIgnoreCase))
            .Where(f => File.ReadAllText(f).Contains("HubConnectionBuilder"))
            .Select(f => Path.GetRelativePath(Root, f))
            .ToList();

        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData("xxJordanxxDevxx")]
    [InlineData("localhost:5176")]
    [InlineData("\"FEWEB\"")]
    [InlineData("\"DeviceId\", \"FEWEB\"")]
    public void No_hub_address_header_or_key_is_hard_coded(string hardCoded)
    {
        var offenders = SourceFiles()
            .Where(f => File.ReadAllText(f).Contains(hardCoded))
            .Select(f => Path.GetRelativePath(Root, f))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_flight_list_is_fed_by_the_instances_listener()
    {
        var page = File.ReadAllText(Path.Combine(Root, "src", "DXSuite.Aviation", "Pages", "FlightDataEditor", "FlightManagement.razor"));

        Assert.Contains("@inject ICoreApiLiveFeed", page);
        Assert.Contains("liveFeed.SubscribeAsync(this)", page);
        Assert.DoesNotContain("WindowUpdate", page);
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs") || f.EndsWith(".razor") || f.EndsWith(".json"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static string FindRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "DXSuite.sln"))) root = root.Parent;
        return root?.FullName ?? throw new InvalidOperationException("DXSuite.sln not found above the test output.");
    }
}
