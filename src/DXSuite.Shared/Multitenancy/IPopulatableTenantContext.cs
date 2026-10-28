namespace DXSuite.Shared.Multitenancy;

/// <summary>
/// Write-side interface for priming a freshly created DI scope with tenant context.
/// Worker-tier code resolves this from its scope and calls Populate before doing any
/// tenant-specific work. HTTP-tier code consumes only ITenantContext (read side only).
/// </summary>
public interface IPopulatableTenantContext : ITenantContext
{
    void Populate(TenantRecord record);
}
