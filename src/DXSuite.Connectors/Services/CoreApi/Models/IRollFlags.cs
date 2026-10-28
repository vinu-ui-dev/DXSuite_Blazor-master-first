namespace DXSuite.Connectors.Services.CoreApi.Models
{
    /// <summary>
    /// The four Display Category flags CoreAPI carries on a flight and on a Flight Edit: Public, Baggage, Staff and
    /// Throw. Read and written through <see cref="RollCategories"/>, which owns their encoding.
    /// </summary>
    public interface IRollFlags
    {
        string? ROP { get; set; }
        string? ROB { get; set; }
        string? ROS { get; set; }
        string? ROT { get; set; }
    }
}
