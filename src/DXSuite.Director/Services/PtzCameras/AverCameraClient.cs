using System.Net;
using System.Net.Sockets;

namespace DXSuite.Director.Services.PtzCameras;

public class AverCameraClient : IPtzCameraClient
{
    private const int DefaultPort = 52381;
    private const int ViscaAddress = 1;
    private const int UdpTimeout = 1000; // 1 second timeout for responses

    private uint _sequenceNumber = 1;
    private readonly ILogger<AverCameraClient> _logger;

    public string Manufacturer => "Aver";

    public AverCameraClient(ILogger<AverCameraClient> logger)
    {
        _logger = logger;
    }

    public async Task<CameraCommandResult> PanAsync(string ipAddress, int speed, PanDirection direction)
    {
        // Pan-only movement requires tilt to be stopped (0x03 0x03 for stop)
        byte panSpeed = ClampSpeed(speed, 1, 24);
        byte tiltSpeed = 0x01; // Minimal tilt speed when not tilting

        byte[] viscaCommand = direction switch
        {
            PanDirection.Right => new byte[] { 0x81, 0x01, 0x06, 0x01, panSpeed, tiltSpeed, 0x02, 0x03, 0xFF },
            PanDirection.Left => new byte[] { 0x81, 0x01, 0x06, 0x01, panSpeed, tiltSpeed, 0x01, 0x03, 0xFF },
            _ => new byte[] { 0x81, 0x01, 0x06, 0x01, panSpeed, tiltSpeed, 0x03, 0x03, 0xFF }
        };

        return await SendViscaCommandAsync(ipAddress, viscaCommand, $"Pan {direction}");
    }

    public async Task<CameraCommandResult> TiltAsync(string ipAddress, int speed, TiltDirection direction)
    {
        // Tilt-only movement requires pan to be stopped
        byte panSpeed = 0x01; // Minimal pan speed when not panning
        byte tiltSpeed = ClampSpeed(speed, 1, 24);

        byte[] viscaCommand = direction switch
        {
            TiltDirection.Up => new byte[] { 0x81, 0x01, 0x06, 0x01, panSpeed, tiltSpeed, 0x03, 0x01, 0xFF },
            TiltDirection.Down => new byte[] { 0x81, 0x01, 0x06, 0x01, panSpeed, tiltSpeed, 0x03, 0x02, 0xFF },
            _ => new byte[] { 0x81, 0x01, 0x06, 0x01, panSpeed, tiltSpeed, 0x03, 0x03, 0xFF }
        };

        return await SendViscaCommandAsync(ipAddress, viscaCommand, $"Tilt {direction}");
    }

    public async Task<CameraCommandResult> ZoomAsync(string ipAddress, int speed, ZoomDirection direction)
    {
        // VISCA zoom commands: 0x2p where p is speed (0-7), or use standard variable zoom
        byte zoomSpeed = ClampSpeed(speed, 0, 7);

        byte[] viscaCommand = direction switch
        {
            ZoomDirection.In => new byte[] { 0x81, 0x01, 0x04, 0x07, (byte)(0x20 | zoomSpeed), 0xFF },
            ZoomDirection.Out => new byte[] { 0x81, 0x01, 0x04, 0x07, (byte)(0x30 | zoomSpeed), 0xFF },
            _ => new byte[] { 0x81, 0x01, 0x04, 0x07, 0x00, 0xFF }
        };

        return await SendViscaCommandAsync(ipAddress, viscaCommand, $"Zoom {direction}");
    }

    public async Task<CameraCommandResult> StopMovementAsync(string ipAddress)
    {
        // Stop all pan/tilt movement
        byte[] viscaCommand = { 0x81, 0x01, 0x06, 0x01, 0x01, 0x01, 0x03, 0x03, 0xFF };
        return await SendViscaCommandAsync(ipAddress, viscaCommand, "Stop Movement");
    }

    public async Task<CameraCommandResult> RecallPresetAsync(string ipAddress, int presetNumber)
    {
        if (presetNumber < 1 || presetNumber > 100)
        {
            return new CameraCommandResult
            {
                Success = false,
                Command = $"Recall Preset {presetNumber}",
                ErrorMessage = "Preset number must be between 1 and 100"
            };
        }

        // VISCA preset recall: 81 01 04 3F 02 0p FF (p = preset 0-indexed)
        byte presetIndex = (byte)(presetNumber - 1);
        byte[] viscaCommand = { 0x81, 0x01, 0x04, 0x3F, 0x02, presetIndex, 0xFF };

        return await SendViscaCommandAsync(ipAddress, viscaCommand, $"Recall Preset {presetNumber}");
    }

    public async Task<CameraCommandResult> SavePresetAsync(string ipAddress, int presetNumber)
    {
        if (presetNumber < 1 || presetNumber > 100)
        {
            return new CameraCommandResult
            {
                Success = false,
                Command = $"Save Preset {presetNumber}",
                ErrorMessage = "Preset number must be between 1 and 100"
            };
        }

        // VISCA preset save: 81 01 04 3F 01 0p FF (p = preset 0-indexed)
        byte presetIndex = (byte)(presetNumber - 1);
        byte[] viscaCommand = { 0x81, 0x01, 0x04, 0x3F, 0x01, presetIndex, 0xFF };

        return await SendViscaCommandAsync(ipAddress, viscaCommand, $"Save Preset {presetNumber}");
    }

    public async Task<bool> TestConnectionAsync(string ipAddress)
    {
        try
        {
            // Send a basic inquiry command (power status) to test connectivity
            byte[] viscaCommand = { 0x81, 0x09, 0x04, 0x00, 0xFF };
            var result = await SendViscaCommandAsync(ipAddress, viscaCommand, "Test Connection", waitForResponse: true);
            return result.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connection test failed for camera at {IpAddress}", ipAddress);
            return false;
        }
    }

    /// <summary>
    /// Builds a VISCA over IP packet by wrapping the VISCA command in an 8-byte UDP header
    /// </summary>
    private byte[] BuildViscaPacket(byte[] viscaCommand, uint sequenceNumber)
    {
        byte[] packet = new byte[8 + viscaCommand.Length];

        // Payload type: 0x01 0x00 for VISCA command
        packet[0] = 0x01;
        packet[1] = 0x00;

        // Payload length (big-endian)
        ushort length = (ushort)viscaCommand.Length;
        packet[2] = (byte)((length >> 8) & 0xFF);
        packet[3] = (byte)(length & 0xFF);

        // Sequence number (big-endian, 32-bit)
        packet[4] = (byte)((sequenceNumber >> 24) & 0xFF);
        packet[5] = (byte)((sequenceNumber >> 16) & 0xFF);
        packet[6] = (byte)((sequenceNumber >> 8) & 0xFF);
        packet[7] = (byte)(sequenceNumber & 0xFF);

        // VISCA command payload
        Array.Copy(viscaCommand, 0, packet, 8, viscaCommand.Length);

        return packet;
    }

    /// <summary>
    /// Sends a VISCA command via UDP and optionally waits for response
    /// </summary>
    private async Task<CameraCommandResult> SendViscaCommandAsync(
        string ipAddress,
        byte[] viscaCommand,
        string commandName,
        bool waitForResponse = false)
    {
        UdpClient? udpClient = null;

        try
        {
            // Build VISCA over IP packet
            uint currentSeq = _sequenceNumber++;
            byte[] packet = BuildViscaPacket(viscaCommand, currentSeq);

            // Log the command for debugging
            string hexCommand = BitConverter.ToString(packet).Replace("-", " ");
            _logger.LogDebug("Sending {Command} to {IpAddress}: {HexCommand}",
                commandName, ipAddress, hexCommand);

            // Send UDP packet
            udpClient = new UdpClient();
            udpClient.Client.ReceiveTimeout = UdpTimeout;

            IPEndPoint endpoint = new IPEndPoint(IPAddress.Parse(ipAddress), DefaultPort);
            await udpClient.SendAsync(packet, packet.Length, endpoint);

            // Optionally wait for ACK/response
            if (waitForResponse)
            {
                udpClient.Client.ReceiveTimeout = UdpTimeout;
                var receiveTask = udpClient.ReceiveAsync();
                var timeoutTask = Task.Delay(UdpTimeout);

                var completedTask = await Task.WhenAny(receiveTask, timeoutTask);

                if (completedTask == receiveTask)
                {
                    var response = await receiveTask;
                    string hexResponse = BitConverter.ToString(response.Buffer).Replace("-", " ");
                    _logger.LogDebug("Received response: {HexResponse}", hexResponse);
                }
                else
                {
                    _logger.LogWarning("No response received within timeout for {Command}", commandName);
                }
            }

            return new CameraCommandResult
            {
                Success = true,
                Command = commandName
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send {Command} to camera at {IpAddress}",
                commandName, ipAddress);

            return new CameraCommandResult
            {
                Success = false,
                Command = commandName,
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            udpClient?.Dispose();
        }
    }

    /// <summary>
    /// Clamps speed value to valid VISCA range
    /// </summary>
    private static byte ClampSpeed(int speed, int min, int max)
    {
        if (speed < min) return (byte)min;
        if (speed > max) return (byte)max;
        return (byte)speed;
    }
}
