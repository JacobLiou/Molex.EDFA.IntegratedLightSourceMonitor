using System.IO.Ports;
using LightSourceMonitor.Models;
using Microsoft.Extensions.Logging;

namespace LightSourceMonitor.Drivers;

/// <summary>
/// WBA (Bridgewater环保监测) 串口驱动
/// 采集温度(4路)、电压(4路)、气压(1路) 共9个参数
/// </summary>
public class WbaDriver : IDisposable
{
    private readonly ILogger<WbaDriver> _logger;
    private SerialPort? _port;
    private bool _disposed;
    private readonly object _lockObject = new();

    public bool IsOpen => _port?.IsOpen ?? false;

    public WbaDriver(ILogger<WbaDriver> logger)
    {
        _logger = logger;
    }

    public bool Open(string comPort)
    {
        if (string.IsNullOrWhiteSpace(comPort))
        {
            _logger.LogWarning("WBA COM port not configured");
            return false;
        }

        try
        {
            lock (_lockObject)
            {
                if (_port?.IsOpen == true)
                    Close();

                _port = new SerialPort(comPort, 115200, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = 3000,
                    WriteTimeout = 3000
                };
                _port.Open();
                _logger.LogInformation("WBA port opened: {Port}", comPort);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open WBA port: {Port}", comPort);
            _port?.Dispose();
            _port = null;
            return false;
        }
    }

    public WbaTelemetrySnapshot? ReadTelemetry(string deviceSn)
    {
        if (!IsOpen || _port == null)
        {
            _logger.LogWarning("WBA port not open");
            return null;
        }

        try
        {
            lock (_lockObject)
            {
                var temps = ReadTemperatures();
                if (temps == null)
                    return null;

                var voltages = ReadVoltages();
                if (voltages == null)
                    return null;

                var pressure = ReadPressure();
                if (pressure == null)
                    return null;

                var (caseTemp, switchTemp, boardTemp, lidTemp) = temps.Value;
                var (vol1, vol2, vol3, vol4) = voltages.Value;

                return new WbaTelemetrySnapshot
                {
                    DeviceSN = deviceSn,
                    Timestamp = DateTime.Now,
                    Temperatures = [caseTemp, switchTemp, boardTemp, lidTemp],
                    Voltages = [vol1, vol2, vol3, vol4],
                    AtmospherePressure = pressure.Value
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read WBA telemetry from {Port}", _port?.PortName);
            return null;
        }
    }

    private (double caseTemp, double switchTemp, double boardTemp, double lidTemp)? ReadTemperatures()
    {
        // 按顺序读：Case, Switch, Board, Lid
        var commands = new[]
        {
            "dd 01 01 05 02 96 03 00 93 dd 02",  // Case
            "dd 01 01 05 02 96 01 00 91 dd 02",  // Switch
            "dd 01 01 05 02 96 02 00 92 dd 02",  // Board
            "dd 01 01 05 02 96 04 00 94 dd 02"   // Lid
        };

        var temps = new double[4];
        for (int i = 0; i < 4; i++)
        {
            if (!SendCommand(commands[i], out var response))
            {
                _logger.LogWarning("Failed to read temperature[{Index}]", i);
                return null;
            }

            // 解析响应：[10:14] 位置的16进制有符号整数，乘以0.1
            if (!ParseValue(response, out var value))
            {
                _logger.LogWarning("Failed to parse temperature response[{Index}]: {Response}", i, response);
                return null;
            }

            temps[i] = value * 0.1;
        }

        return (temps[0], temps[1], temps[2], temps[3]);
    }

    private (double vol1, double vol2, double vol3, double vol4)? ReadVoltages()
    {
        var commands = new[]
        {
            "dd 01 01 05 02 97 01 00 90 dd 02",
            "dd 01 01 05 02 97 02 00 93 dd 02",
            "dd 01 01 05 02 97 03 00 92 dd 02",
            "dd 01 01 05 02 97 04 00 95 dd 02"
        };

        var vols = new double[4];
        for (int i = 0; i < 4; i++)
        {
            if (!SendCommand(commands[i], out var response))
            {
                _logger.LogWarning("Failed to read voltage[{Index}]", i);
                return null;
            }

            if (!ParseValue(response, out var value))
            {
                _logger.LogWarning("Failed to parse voltage response[{Index}]: {Response}", i, response);
                return null;
            }

            vols[i] = value * 0.01;
        }

        return (vols[0], vols[1], vols[2], vols[3]);
    }

    private double? ReadPressure()
    {
        if (!SendCommand("dd 01 01 05 02 98 01 00 9f dd 02", out var response))
        {
            _logger.LogWarning("Failed to read pressure");
            return null;
        }

        // 检查错误状态 [8:10]
        if (response.Length < 10)
        {
            _logger.LogWarning("Invalid pressure response length: {Response}", response);
            return null;
        }

        string statusHex = response.Substring(8, 2);
        if (statusHex != "00")
        {
            _logger.LogWarning("Pressure error status: {Status}", statusHex);
            return null;
        }

        if (!ParseValue(response, out var value))
        {
            _logger.LogWarning("Failed to parse pressure response: {Response}", response);
            return null;
        }

        return value * 0.01;
    }

    private bool ParseValue(string hexResponse, out double value)
    {
        value = 0;

        // 从响应中提取 [10:14] 位置的值
        if (hexResponse.Length < 14)
            return false;

        string hexStr = hexResponse.Substring(10, 4);
        if (!int.TryParse(hexStr, System.Globalization.NumberStyles.HexNumber, null, out int rawValue))
            return false;

        value = ToSignedInt16(rawValue);
        return true;
    }

    private bool SendCommand(string hexCommand, out string response)
    {
        response = "";

        if (!IsOpen || _port == null)
            return false;

        try
        {
            var bytes = HexStringToBytes(hexCommand);
            _port.Write(bytes, 0, bytes.Length);

            System.Threading.Thread.Sleep(200);

            int available = _port.BytesToRead;
            if (available <= 0)
            {
                _logger.LogWarning("No data received from WBA");
                return false;
            }

            byte[] buffer = new byte[available];
            _port.Read(buffer, 0, available);
            response = BytesToHexString(buffer);

            return true;
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("WBA command timeout");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WBA command error");
            return false;
        }
    }

    private byte[] HexStringToBytes(string hexString)
    {
        var parts = hexString.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var bytes = new byte[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!byte.TryParse(parts[i], System.Globalization.NumberStyles.HexNumber, null, out bytes[i]))
            {
                _logger.LogWarning("Failed to parse hex byte: {Hex}", parts[i]);
                return new byte[0];
            }
        }
        return bytes;
    }

    private string BytesToHexString(byte[] bytes)
    {
        return string.Concat(bytes.Select(b => b.ToString("X2")));
    }

    private short ToSignedInt16(int value)
    {
        // 将无符号16位值转换为有符号
        if (value > 0x7FFF)
            return (short)(value - 0x10000);
        return (short)value;
    }

    public void Close()
    {
        try
        {
            lock (_lockObject)
            {
                _port?.Close();
                _port?.Dispose();
                _port = null;
            }
            _logger.LogInformation("WBA port closed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error closing WBA port");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Close();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~WbaDriver() => Dispose();
}
