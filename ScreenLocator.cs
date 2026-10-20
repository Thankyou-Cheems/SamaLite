using System.IO.Ports;
using System.Management;

namespace SamaLite;

internal static class ScreenLocator
{
    private static string? Find(string hardwareId)
    {
        using var query = new ManagementObjectSearcher("SELECT DeviceID,PNPDeviceID FROM Win32_SerialPort");
        var matches = new List<string>();
        using var results = query.Get();
        foreach (ManagementObject device in results)
        {
            using (device)
            {
                var id = device["PNPDeviceID"] as string;
                var name = device["DeviceID"] as string;
                if (id?.Contains(hardwareId, StringComparison.OrdinalIgnoreCase) == true && name?.StartsWith("COM", StringComparison.OrdinalIgnoreCase) == true) matches.Add(name);
            }
        }
        return matches.Count switch { 0 => null, 1 => matches[0], _ => throw new IOException("发现多个同型号屏幕，请只连接目标屏幕后重试。") };
    }

    public static string? FindWorkingPort() => Find("VID_1D6B&PID_0121");

    public static void WakeIfPresent()
    {
        var portName = Find("VID_1A86&PID_CA21");
        if (portName is null) return;
        using var port = new SerialPort(portName, 115200) { DtrEnable = true, RtsEnable = true, ReadTimeout = 500, WriteTimeout = 500 };
        port.Open();
        port.Close();
    }
}
