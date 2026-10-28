namespace DXSuite.Director.Models;

public class CameraConfiguration
{
    public string IpAddress { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string FirmwareVersion { get; set; } = string.Empty;
    public string Protocol { get; set; } = "ViscaOverIP";
    public int Port { get; set; } = 52381;
    public int ViscaAddress { get; set; } = 1;
    public string? RtspUrl { get; set; }
    public List<CameraPreset> Presets { get; set; } = new();
}

public class CameraPreset
{
    public int PresetNumber { get; set; }
    public string PresetName { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class ControlSettings
{
    public int PanSpeed { get; set; } = 12;
    public int TiltSpeed { get; set; } = 12;
    public int ZoomSpeed { get; set; } = 3;
}

public class PtzCameraExtendedData
{
    public CameraConfiguration CameraConfig { get; set; } = new();
    public ControlSettings ControlSettings { get; set; } = new();
}
