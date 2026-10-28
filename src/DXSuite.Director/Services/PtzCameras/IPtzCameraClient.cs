namespace DXSuite.Director.Services.PtzCameras;

public interface IPtzCameraClient
{
    string Manufacturer { get; }
    Task<CameraCommandResult> PanAsync(string ipAddress, int speed, PanDirection direction);
    Task<CameraCommandResult> TiltAsync(string ipAddress, int speed, TiltDirection direction);
    Task<CameraCommandResult> ZoomAsync(string ipAddress, int speed, ZoomDirection direction);
    Task<CameraCommandResult> StopMovementAsync(string ipAddress);
    Task<CameraCommandResult> RecallPresetAsync(string ipAddress, int presetNumber);
    Task<CameraCommandResult> SavePresetAsync(string ipAddress, int presetNumber);
    Task<bool> TestConnectionAsync(string ipAddress);
}

public enum PanDirection { Left = -1, Stop = 0, Right = 1 }
public enum TiltDirection { Down = -1, Stop = 0, Up = 1 }
public enum ZoomDirection { Out = -1, Stop = 0, In = 1 }

public class CameraCommandResult
{
    public bool Success { get; set; }
    public string Command { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}
