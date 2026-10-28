namespace DXSuite.Director.Services.DistributedLock
{
    /// <summary>
    /// Re-exports the shared distributed lock interface so existing Director code
    /// that uses 'using DXSuite.Director.Services.DistributedLock' continues to compile.
    /// New code should reference DXSuite.Shared.Services.IDistributedLockService directly.
    /// </summary>
    public interface IDistributedLockService : DXSuite.Shared.Services.IDistributedLockService
    {
    }
}
