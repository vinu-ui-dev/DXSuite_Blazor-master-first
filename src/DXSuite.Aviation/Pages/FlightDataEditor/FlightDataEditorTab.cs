namespace DXSuite.Aviation.Pages.FlightDataEditor;

/// <summary>One tab of the Flight Data Editor's tab shell.</summary>
public class FlightDataEditorTab()
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public Type ComponentType { get; set; } = typeof(object);
    public int Order { get; set; }
}
