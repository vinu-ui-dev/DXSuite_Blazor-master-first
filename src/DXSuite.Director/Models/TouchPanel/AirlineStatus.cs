namespace DXSuite.Director.Models.TouchPanel;

public class AirlineStatus
{
    public string? AirlineCode { get; set; }
    public string? CarrierName { get; set; }
    public CarrierStatus? Status1 { get; set; }
    public CarrierStatus? Status2 { get; set; }

    public AirlineStatus()
    {
    }

    public AirlineStatus(AirlineInfo airlineRecord)
    {
        AirlineCode = airlineRecord.AirlineCode;
        CarrierName = airlineRecord.CarrierName;
        Status1 = GetCarrierStatus(airlineRecord.CarrierStatus1);
        Status2 = GetCarrierStatus(airlineRecord.CarrierStatus2);
    }

    private CarrierStatus? GetCarrierStatus(int? carrierStatus)
    {
        if (carrierStatus == null)
            return null;
        else if (carrierStatus == 1)
            return CarrierStatus.On;
        else if (carrierStatus == 2)
            return CarrierStatus.Off;
        else
            return CarrierStatus.Disabled;
    }

    public enum CarrierStatus
    {
        Disabled = 0,
        On = 1,
        Off = 2
    }
}
