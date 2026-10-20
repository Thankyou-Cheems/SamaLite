using System.Text;
using System.Text.Json;
using TuringSmartScreenLib;

namespace SamaLite;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (Argument(args, "--render-ui") is string uiOutput)
        {
            var data = JsonSerializer.Deserialize<MonitorStatus>(File.ReadAllText(Path.Combine(Settings.DataDirectory, "status.json")));
            if (data?.Metrics is not Metrics metrics) throw new IOException("No recorded metrics to render.");
            using var renderer = new DisplayRenderer();
            renderer.Draw(metrics);
            using var form = new StatusForm();
            form.UpdateStatus(data, new Bitmap(renderer.Canvas));
            form.Show(); Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
            bitmap.Save(uiOutput);
            form.Close();
            return;
        }
        if (int.TryParse(Argument(args, "--wait-for-exit"), out var previousPid))
        {
            try { using var previous = System.Diagnostics.Process.GetProcessById(previousPid); previous.WaitForExit(20000); }
            catch (ArgumentException) { }
        }
        if (!args.Any(argument => argument is "--sensors" or "--test-image" or "--live-test" or "--handshake" or "--port"))
        {
            var settings = Settings.Load();
            var user = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
            using var mutex = new Mutex(true, @"Local\SamaLite-Screen-" + user, out var first);
            if (!first) { MessageBox.Show("SamaLite 已在运行，请双击托盘图标查看状态。", "SamaLite"); return; }
            try
            {
                Application.Run(new TrayContext(settings, int.TryParse(Argument(args, "--duration"), out var seconds) ? seconds : null,
                    Argument(args, "--smoke-output"), args.Contains("--show")));
            }
            catch (Exception error) { LimitedLog.Write(error.ToString()); MessageBox.Show(error.Message, "SamaLite 启动失败"); }
            finally { mutex.ReleaseMutex(); }
            return;
        }
        var output = Argument(args, "--output") ?? Path.Combine(AppContext.BaseDirectory, "probe-result.json");
        try
        {
            if (args.Contains("--sensors"))
            {
                using var sensors = new SensorReader();
                var samples = new List<Metrics>();
                for (var index = 0; index < 4; index++) { samples.Add(sensors.Read()); Thread.Sleep(1000); }
                File.WriteAllText(output, JsonSerializer.Serialize(samples, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
                File.WriteAllText(output + ".hardware.txt", sensors.HardwareReport(), new UTF8Encoding(false));
                return;
            }
            using var screen = new TuringSmartScreenRevisionE(Argument(args, "--port") ?? "COM3", 480, 480);
            var started = DateTimeOffset.Now;
            screen.Open();
            if (args.Contains("--test-image"))
            {
                using var renderer = new DisplayRenderer();
                renderer.DrawTest();
                renderer.Canvas.Save(output + ".png");
                screen.StopMedia();
                screen.DisplayBitmap(0, 0, renderer.Pixels(new Rectangle(0, 0, 480, 480)), 480, 480, RotateOption.Rotate270);
            }
            if (args.Contains("--live-test"))
            {
                using var renderer = new DisplayRenderer();
                using var sensors = new SensorReader();
                var statuses = new List<object>();
                for (var index = 0; index < 10; index++)
                {
                    var metrics = sensors.Read();
                    if (index == 0)
                    {
                        renderer.Draw(metrics);
                        screen.StopMedia();
                        screen.DisplayBitmap(0, 0, renderer.Pixels(new Rectangle(0, 0, 480, 480)), 480, 480, RotateOption.Rotate270);
                    }
                    else
                    {
                        renderer.DrawDynamic(metrics);
                        foreach (var region in DisplayRenderer.DynamicRegions)
                            screen.DisplayBitmap(region.X, region.Y, renderer.Pixels(region), region.Width, region.Height, RotateOption.Rotate270);
                    }
                    statuses.Add(new { Frame = index, Metrics = metrics, screen.LastStatus, screen.BytesSent, screen.BytesReceived });
                    Thread.Sleep(1000);
                }
                renderer.Canvas.Save(output + ".png");
                File.WriteAllText(output + ".samples.json", JsonSerializer.Serialize(statuses, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            }
            File.WriteAllText(output, JsonSerializer.Serialize(new { Started = started, Finished = DateTimeOffset.Now, Success = true, Hello = screen.Identity, Width = screen.Width, Height = screen.Height, screen.LastStatus, screen.BytesSent, screen.BytesReceived }, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        }
        catch (Exception exception)
        {
            File.WriteAllText(output, JsonSerializer.Serialize(new { Success = false, Error = exception.ToString() }, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            Environment.ExitCode = 1;
        }
    }

    internal static string? Argument(string[] args, string key)
    {
        var index = Array.IndexOf(args, key);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
