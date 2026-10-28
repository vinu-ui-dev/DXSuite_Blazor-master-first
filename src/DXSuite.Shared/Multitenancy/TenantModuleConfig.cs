namespace DXSuite.Shared.Multitenancy;

public class TenantModuleConfig
{
    public bool Director { get; init; } = true;
    public bool CoreData { get; init; } = false;
    public bool Aviation { get; init; } = false;
    public bool Sentinel { get; init; } = false;
    public bool Pulse { get; init; } = false;
    public bool Design { get; init; } = false;
    public bool StyleGuide { get; init; } = false;
    public bool NetJets { get; init; } = false;
    public bool SLC { get; init; } = false;
}
