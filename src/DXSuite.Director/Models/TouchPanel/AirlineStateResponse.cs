namespace DXSuite.Director.Models.TouchPanel;

public class AirlineStateResponse
{
    public string ResponseMessage { get; set; } = "";
    public bool IsPinCodeValid { get; set; }
    public bool IsAdminUser { get; set; }
    public AirlineInfo? Airline { get; set; }
    public int OriginalState { get; set; }
    public int NewState { get; set; }
}

/// <summary>
/// Lightweight airline DTO used by Director touch panel responses.
/// Maps from the Aviation module's TblAirline entity.
/// </summary>
public class AirlineInfo
{
    public int RecordId { get; set; }
    public string AirlineCode { get; set; } = null!;
    public string? CarrierName { get; set; }
    public int? CarrierStatus1 { get; set; }
    public int? CarrierStatus2 { get; set; }
    public bool? Tpenabled1 { get; set; }
    public bool? Tpenabled2 { get; set; }
    public int? Pincode { get; set; }

    public static AirlineInfo FromEntity(DXSuite.Aviation.Entities.TblAirline e) => new()
    {
        RecordId = e.RecordId,
        AirlineCode = e.AirlineCode,
        CarrierName = e.CarrierName,
        CarrierStatus1 = e.CarrierStatus1,
        CarrierStatus2 = e.CarrierStatus2,
        Tpenabled1 = e.Tpenabled1,
        Tpenabled2 = e.Tpenabled2,
        Pincode = e.Pincode,
    };
}
