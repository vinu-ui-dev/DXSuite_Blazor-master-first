namespace DXSuite.Director.Models.TouchPanel;

public class PinCodeResponse
{
    public string ResponseMessage { get; set; } = "";
    public bool IsPinCodeValid { get; set; }
    public bool IsAdminUser { get; set; }
    public AirlineInfo? Airline { get; set; }
    public List<AirlineInfo>? AirlineList { get; set; }
}
