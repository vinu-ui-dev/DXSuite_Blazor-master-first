using System.Reflection.Metadata;

namespace DXSuite.Director.Models.Messages;

public class MessageTab()
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public Type ComponentType { get; set; } = typeof(object);
    public int Order { get; set; }
    public IDictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
    
}
