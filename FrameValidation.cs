using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace SamaLite;

// An explicitly requested diagnostic: never makes its native HWND visible.
// Its controlled application Presents are not a real game benchmark.
internal static class FrameValidation
{
    public static void Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var records = new List<DetailDevice>();
        var presents = 0; var visible = false; var result = ""; var started = DateTimeOffset.Now;
        IntPtr swap = IntPtr.Zero, device = IntPtr.Zero, context = IntPtr.Zero;
        var window = new NativeWindow();
        try
        {
            window.CreateHandle(new CreateParams { Caption = "SamaLite hidden frame diagnostic", Width = 16, Height = 16, Style = unchecked((int)0x80000000) });
            var descriptor = new SwapDescriptor { Mode = new() { Width = 16, Height = 16, RefreshNumerator = 60, RefreshDenominator = 1, Format = 28 }, SampleCount = 1, BufferUsage = 0x20, BufferCount = 1, OutputWindow = window.Handle, Windowed = 1 };
            Marshal.ThrowExceptionForHR(D3D11CreateDeviceAndSwapChain(IntPtr.Zero, 1, IntPtr.Zero, 0, IntPtr.Zero, 0, 7, ref descriptor, out swap, out device, out _, out context));
            var present = Marshal.GetDelegateForFunctionPointer<Present>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(swap), 8 * IntPtr.Size));
            var config = new Settings().Capture(); var cache = new DetailCache();
            using (var reader = new FrameReader(() => (Environment.ProcessId, "hidden DXGI diagnostic")))
            {
                var clock = Stopwatch.StartNew(); var nextSample = TimeSpan.Zero;
                while (clock.Elapsed.TotalSeconds < 16)
                {
                    var hr = present(swap, 0, 0); if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                    presents++; visible |= IsWindowVisible(window.Handle);
                    if (clock.Elapsed >= nextSample) { records.Add(reader.Read(cache, config)); nextSample += TimeSpan.FromSeconds(1); }
                    Thread.Sleep(16);
                }
                records.Add(reader.Read(cache, config));
            }
            result = records.Any(r => r.Metrics.Any(m => m.Id == "game/low" && m.Availability == DetailAvailability.Available)) && !visible ? "Live hidden Presents captured with FPS, frame time and 1% Low" : "No complete live statistics captured";
            if (visible || !records.Any(r => r.Metrics.Any(m => m.Id == "game/low" && m.Availability == DetailAvailability.Available))) Environment.ExitCode = 1;
        }
        catch (Exception error) { result = error.ToString(); Environment.ExitCode = 1; }
        finally
        {
            if (context != IntPtr.Zero) Marshal.Release(context); if (device != IntPtr.Zero) Marshal.Release(device); if (swap != IntPtr.Zero) Marshal.Release(swap);
            window.DestroyHandle();
            File.WriteAllText(Path.Combine(directory, "live-frames.json"), JsonSerializer.Serialize(new { Started = started, Result = result, ProcessId = Environment.ProcessId, Presents = presents, WindowWasVisible = visible, Records = records }, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Mode { public uint Width, Height, RefreshNumerator, RefreshDenominator, Format, Scanline, Scaling; }
    [StructLayout(LayoutKind.Sequential)] private struct SwapDescriptor { public Mode Mode; public uint SampleCount, SampleQuality, BufferUsage, BufferCount; public IntPtr OutputWindow; public int Windowed; public uint SwapEffect, Flags; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Present(IntPtr swap, uint interval, uint flags);
    [DllImport("d3d11.dll")] private static extern int D3D11CreateDeviceAndSwapChain(IntPtr adapter, uint driverType, IntPtr software, uint flags, IntPtr levels, uint count, uint sdk, ref SwapDescriptor descriptor, out IntPtr swap, out IntPtr device, out uint level, out IntPtr context);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr window);
}
