using DXSuite.Director.Entities;

namespace DXSuite.Director.Services;

/// <summary>
/// CRUD and query operations for floor plan elements.
/// </summary>
public interface IFloorPlanService
{
    Task<List<TblFloorPlanElement>> GetElementsForLocationAsync(int locationId);
    Task<TblFloorPlanElement> CreateElementAsync(TblFloorPlanElement element);
    Task UpdateElementAsync(TblFloorPlanElement element);
    Task UpdateElementPositionAsync(int elementId, double top, double left, double width, double height);
    Task DeleteElementAsync(int elementId);

    /// <summary>
    /// Returns the active icon path for each element at the given time.
    /// Key = FloorPlanElementId, Value = resolved icon asset path.
    /// </summary>
    Task<Dictionary<int, string>> GetActiveIconsAsync(int locationId, DateTime now);
}
