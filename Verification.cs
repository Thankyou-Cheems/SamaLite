using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;

namespace SamaLite;

// Explicit offline fixtures. This mode never opens a serial port or hardware sensor.
internal static class Verification
{
    public static Metrics Fixture(double? usage = 78, double? heat = 72, double used = 24.8, double total = 47.7)
    {
        var tick = Stopwatch.GetTimestamp(); var now = DateTimeOffset.Now;
        var readings = Enum.GetValues<MetricId>().ToDictionary(id => id, id => new MetricReading(id switch
        {
            MetricId.CpuUsage => usage, MetricId.CpuTemperature => heat, MetricId.GpuUsage => usage.HasValue ? 96 : null,
            MetricId.GpuTemperature => heat.HasValue ? 64 : null, MetricId.MemoryUsed => used, _ => total
        }, "离线验证样本（非实机读数）", now, tick, usage is null && id == MetricId.CpuUsage ? "CPU负载未读取" : null));
        var history = Enumerable.Range(0, 61).Select(i => new HistoryPoint(now.AddSeconds((i - 60) * 2), tick + (i - 60) * 2 * Stopwatch.Frequency,
            i is >= 26 and <= 30 ? null : 30d + i / 2, 70d + i / 3, 50d + i / 3, 60d + i / 10)).ToArray();
        return new(now, readings, history);
    }
    private static Metrics RichFixture(Metrics? basis = null, bool extreme = false)
    {
        var cache = new DetailCache();
        DetailMetric M(string id, string label, string unit, double value) => cache.Capture(id, label, unit, value, "离线验证样本");
        return (basis ?? Fixture()) with { Details = new(DateTimeOffset.Now, Stopwatch.GetTimestamp(),
        [new("cpu", "cpu", "CPU", "离线样本", [M("cpu/power", "CPU Package", "W", extreme ? 999 : 126), M("cpu/clock", "CPU Core #1", "MHz", 5500)]),
         new("nvidia", "nvidia", "GPU", "离线样本", [M("nvidia/vram-used", "显存已用", "GiB", extreme ? 16 : 5.9), M("nvidia/vram-total", "显存总量", "GiB", 16), M("nvidia/vram-reserved", "系统保留", "GiB", .3), M("nvidia/gpu-power", "GPU功耗", "W", 86), M("nvidia/gpu-clock", "GPU时钟", "MHz", 1500)]),
         new("dimm/0", "memory", "DIMM #0", "离线样本", [M("dimm/0/temp", "温度", "°C", extreme ? 125 : 46)]),
         new("dimm/2", "memory", "DIMM #2", "离线样本", [M("dimm/2/temp", "温度", "°C", extreme ? 125 : 45)]),
         new("board", "cooling", "主板", "离线样本", [M("fan/cpu", "CPU Fan", "RPM", extreme ? 60000 : 1314), M("fan/pump", "Pump Fan", "RPM", extreme ? 60000 : 2575)]),
         new("nvme/0", "storage", "NVMe", "离线样本", [M("nvme/0/temperature", "温度", "°C", extreme ? 150 : 48)]),
         new("io", "network", "IO", "离线样本", [M("io/download", "下载", "MiB/s", extreme ? 99999 : 2.4), M("io/upload", "上传", "MiB/s", extreme ? 99999 : .1), M("io/disk-read", "读取", "MiB/s", extreme ? 99999 : 82), M("io/disk-write", "写入", "MiB/s", extreme ? 99999 : .3), M("io/disk-busy", "活动", "%", extreme ? 100 : 23)]),
         new("frames", "game", "应用帧", "离线样本", [M("game/fps", "FPS", "FPS", extreme ? 99999 : 144), M("game/frametime", "帧时间", "ms", extreme ? 60000 : 6.9), M("game/low", "1% Low", "FPS", extreme ? 99999 : 112)]),
         new("igpu", "igpu", "UHD 770", "离线样本", [M("igpu/0", "3D引擎 #0", "%", extreme ? 100 : 0), M("igpu/shared", "共享内存已用", "GiB", .05), M("igpu/clock", "GPU Core", "MHz", 700)])]) };
    }
    public static void Run(string directory, bool renderWindows = false)
    {
        Directory.CreateDirectory(directory); var results = new List<string>();
        var resultPath = Path.Combine(directory, renderWindows ? "results.json" : "model-results.json");
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); results.Add(name); }
        try
        {
            var sample = Fixture(); var tick = Stopwatch.GetTimestamp();
            Check(sample.MemoryPercent is > 51 and < 53, "Memory percentage uses actual physical total");
            Check(sample.Fresh(MetricId.CpuUsage, 1, tick + 6 * Stopwatch.Frequency) is null, "1s interval expires after 5s");
            Check(sample.Fresh(MetricId.CpuUsage, 2, tick + 7 * Stopwatch.Frequency) is null, "2s interval expires after 6s");
            Check(sample.Fresh(MetricId.CpuUsage, 5, tick + 14 * Stopwatch.Frequency) is not null, "5s interval remains fresh before 15s");
            Check(Fixture(0, 0).CpuUsage == 0, "Valid zero is preserved");
            var mixed = sample.Readings.ToDictionary(p => p.Key, p => p.Value);
            mixed[MetricId.CpuTemperature] = mixed[MetricId.CpuTemperature] with { SampleTick = tick - 20 * Stopwatch.Frequency };
            var partial = sample with { Readings = mixed };
            Check(partial.Fresh(MetricId.CpuTemperature, 1, tick) is null && partial.Fresh(MetricId.GpuTemperature, 1, tick) is not null, "Fresh GPU cannot refresh stale CPU timestamp");
            Check((sample with { Readings = mixed }).Summary(1, tick) == "CPU温度过期", "Stale field is named");
            Check(sample.Summary(1, tick + 20 * Stopwatch.Frequency) == "读数已过期", "All-stale state has an explicit page warning");
            var history = new TelemetryHistory(); HistoryPoint[] points = [];
            for (var i = 0; i < 140; i++) points = history.Add(sample, tick + i * Stopwatch.Frequency);
            Check(points.Length == 121, "Session history is bounded to 120 seconds");
            var tracker = new AttentionTracker(); var config = new Settings().Capture(); var hot = Fixture(50, 95);
            string? attention = null;
            for (var i = 0; i <= 15; i++) attention = tracker.Update(hot, config, tick + i * Stopwatch.Frequency);
            Check(attention?.Contains("CPU") == true, "CPU attention enters after 15 real seconds");
            var cool = Fixture(50, 80);
            for (var i = 16; i <= 46; i++) attention = tracker.Update(cool, config, tick + i * Stopwatch.Frequency);
            Check(attention is null, "Attention recovers after 30 seconds with hysteresis");
            var interrupted = new AttentionTracker();
            interrupted.Update(hot, config, tick); interrupted.Update(hot, config, tick + 30 * Stopwatch.Frequency);
            Check(interrupted.Update(hot, config, tick + 31 * Stopwatch.Frequency) is null, "Sampling gap interrupts sustained conditions");
            VerifyDetails(directory, Check);
            VerifyExtended(Check);
            VerifyGauges(Check);
            VerifyGameActivity(Check);
            RenderRasters(directory, Check);
            RenderIcons(directory, Check);
            if (renderWindows) RenderWindows(directory, Check);
            VerifyWorker(Check);
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new { Success = true, Checks = results }, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        }
        catch (Exception error)
        {
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new { Success = false, Checks = results, Error = error.ToString() }, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            Environment.ExitCode = 1;
        }
    }
    private static void RenderRasters(string directory, Action<bool, string> check)
    {
        using var renderer = new DisplayRenderer();
        foreach (var (name, sample) in new[] { ("normal", RichFixture()), ("extreme", RichFixture(Fixture(100, 100, 47.7), true)), ("zero", Fixture(0, 0, 0)), ("missing", Fixture(null, null)) })
        {
            renderer.Draw(sample); renderer.Canvas.Save(Path.Combine(directory, name + ".png"));
            CheckSafety(renderer.Canvas, name, check);
        }
        var fixture = Fixture(); renderer.Draw(fixture, state: "输出暂停", frozenAt: DateTimeOffset.Now);
        renderer.Canvas.Save(Path.Combine(directory, "paused.png"));
        CheckSafety(renderer.Canvas, "paused", check);
        renderer.Draw(fixture with { Attention = "CPU温度持续偏高；GPU温度持续偏高；内存余量较少" });
        renderer.Canvas.Save(Path.Combine(directory, "attention.png"));
        CheckSafety(renderer.Canvas, "multiple-attention", check);
        var oldReadings = fixture.Readings.ToDictionary(p => p.Key, p => p.Value with { SampleTick = p.Value.SampleTick - 20 * Stopwatch.Frequency, SampledAt = p.Value.SampledAt?.AddSeconds(-20) });
        renderer.Draw(fixture with { Readings = oldReadings }); renderer.Canvas.Save(Path.Combine(directory, "stale.png"));
        CheckSafety(renderer.Canvas, "all-stale", check);
        using var before = new Bitmap(renderer.Canvas);
        renderer.Draw(RichFixture(Fixture(100, 100, 47.7), true));
        var uncovered = 0;
        for (var y = 0; y < 480; y++) for (var x = 0; x < 480; x++)
            if (before.GetPixel(x, y) != renderer.Canvas.GetPixel(x, y) && !DisplayRenderer.DynamicRegions.Any(r => r.Contains(x, y))) uncovered++;
        check(uncovered == 0, "Partial update regions cover all changed pixels, including status and footer");
        foreach (var preset in new[] { "dense", "game", "cooling" })
        {
            var config = new Settings { Layout = PanelLayout.For(preset) }.Capture();
            renderer.Draw(RichFixture(Fixture(100, 100), true), configuration: config);
            renderer.Canvas.Save(Path.Combine(directory, "layout-" + preset + ".png")); CheckSafety(renderer.Canvas, preset + " extreme preset", check);
        }
        foreach (var percent in new[] { 0, 1, 25, 50, 75, 99, 100 })
        {
            var raster = RichFixture(Fixture(percent, 100, 47.7 * (100 - percent) / 100));
            var readings = raster.Readings.ToDictionary(p => p.Key, p => p.Value);
            readings[MetricId.GpuUsage] = readings[MetricId.GpuUsage] with { Value = percent };
            readings[MetricId.GpuTemperature] = readings[MetricId.GpuTemperature] with { Value = 100 };
            raster = raster with { Readings = readings, Details = raster.Details! with { Devices = raster.Details!.Devices.Select(d => d.Id != "nvidia" ? d : d with
                { Metrics = d.Metrics.Select(m => m.Id != "nvidia/vram-used" ? m : m with { Reading = m.Reading with { Value = 16d * (100 - percent) / 100 } }).ToArray() }).ToArray() } };
            renderer.Draw(raster); renderer.Canvas.Save(Path.Combine(directory, "gauges-" + percent + ".png")); CheckSafety(renderer.Canvas, "gauge endpoints " + percent, check);
        }
        renderer.Draw(RichFixture(), configuration: new Settings { Layout = new() { Rings = false } }.Capture());
        CheckSafety(renderer.Canvas, "bands disabled", check);
        foreach (var (name, sample) in new[] { ("game-normal", RichFixture()), ("game-extreme", RichFixture(Fixture(100, 100), true)), ("game-missing", Fixture(null, null)) })
        {
            renderer.Draw(sample, configuration: new Settings { GameDisplay = "game" }.Capture());
            renderer.Canvas.Save(Path.Combine(directory, name + ".png")); CheckSafety(renderer.Canvas, name, check);
        }
        renderer.Draw(RichFixture()); using var overview = new Bitmap(renderer.Canvas);
        renderer.Draw(RichFixture(), configuration: new Settings { GameDisplay = "game" }.Capture());
        var missed = 0;
        for (var y = 0; y < 480; y++) for (var x = 0; x < 480; x++)
            if (overview.GetPixel(x, y) != renderer.Canvas.GetPixel(x, y) && !DisplayRenderer.DynamicRegions.Any(r => r.Contains(x, y))) missed++;
        check(missed == 0, "Switching overview and gaming layouts clears every changed pixel through partial updates");
    }
    private static void VerifyGameActivity(Action<bool, string> check)
    {
        var tick = Stopwatch.GetTimestamp(); var f = Stopwatch.Frequency;
        check(WindowActivity.CoversMonitor(new(1920, 0, 2560, 1440), new(1920, 0, 2560, 1440)) &&
            !WindowActivity.CoversMonitor(new(1920, 30, 2560, 1370), new(1920, 0, 2560, 1440)), "Fullscreen detection compares the foreground client area with its actual monitor, not desktop work area");
        check(WindowActivity.Ignored("explorer", true) && WindowActivity.Ignored("chrome", false) && !WindowActivity.Ignored("chrome", true), "Shell windows are excluded while genuinely fullscreen browser renderers remain eligible");
        FrameStatistics Stats(long at) => new(60, 16.67, 50, 600, 10, at);
        var tracker = new GameActivityTracker(); var full = new FrameTarget(100, "fixture", true);
        check(!tracker.Update(full, null, tick).GameLayout, "A fullscreen window without real frame events cannot activate gaming layout");
        tracker.Update(full, Stats(tick), tick);
        check(!tracker.Update(full, Stats(tick + 2 * f), tick + 2 * f).GameLayout && tracker.Update(full, Stats(tick + 3 * f), tick + 3 * f).GameLayout,
            "Gaming layout activates only after three seconds of confirmed frame activity");
        check(tracker.Update(new(0, ""), null, tick + 5 * f).GameLayout && !tracker.Update(new(0, ""), null, tick + 12 * f).GameLayout,
            "Temporary focus loss is held briefly and an inactive game eventually returns to overview");
        var windowed = new GameActivityTracker();
        windowed.Update(new(100, "fixture"), Stats(tick), tick);
        check(!windowed.Update(new(100, "fixture"), Stats(tick + 9 * f), tick + 9 * f).GameLayout, "Ordinary windowed rendering is not guessed to be a game");
        var explicitGame = new GameActivityTracker(); var selected = new FrameTarget(101, "fixture", Explicit: true);
        explicitGame.Update(selected, Stats(tick), tick);
        check(explicitGame.Update(selected, Stats(tick + 4 * f), tick + 4 * f).GameLayout, "Explicitly selected windowed games can activate gaming layout");
        var config = new Settings().Capture(); var active = Fixture() with { FrameActivity = new(true, 100, "fixture", true, false, "test") };
        check(DisplayRenderer.GameLayout(active, config) && !DisplayRenderer.GameLayout(active, config with { GameDisplay = "normal" }) &&
            DisplayRenderer.GameLayout(Fixture(), config with { GameDisplay = "game" }) && !DisplayRenderer.GameLayout(active, config with { FramesEnabled = false }),
            "Automatic, pinned overview and pinned gaming display modes respect user settings");
        check(!DisplayRenderer.GameLayout(Fixture(), config), "Old saved telemetry without frame activity defaults safely to overview");
        var interrupted = new GameActivityTracker();
        interrupted.Update(full, Stats(tick), tick);
        check(!interrupted.Update(full, Stats(tick + 12 * f), tick + 12 * f).GameLayout, "Long sampling gaps cannot count as sustained game activity");
    }
    private static void VerifyExtended(Action<bool, string> check)
    {
        var tick = Stopwatch.GetTimestamp(); var frequency = Stopwatch.Frequency;
        var frames = new FrameWindow();
        for (var i = 0; i < 600; i++) frames.Add(tick + i * frequency / 100, i >= 594 ? 50 : 10);
        var stats = frames.Read(tick + 599 * frequency / 100);
        check(stats is { Low: 20, Count: 600 } && stats.Value.Fps is > 70 and < 100, "1% Low uses the mean of the slowest one percent of individual frames");
        check(frames.Read(tick + 64 * frequency) is null, "Stopped frame events expire rather than retaining old FPS");
        frames.Clear(); frames.Add(tick, 0); frames.Add(tick, double.NaN); frames.Add(tick, double.PositiveInfinity);
        check(frames.Read(tick) is null, "Invalid intervals do not create frame statistics");
        for (var i = 0; i < 150000; i++) frames.Add(tick + i * frequency / 3000, 1d / 3);
        check(frames.Read(tick + 149999 * frequency / 3000)?.Count == 120000, "Frame history is bounded even above 2000 FPS");
        frames.Clear(); frames.Add(tick, 16); frames.Add(tick + frequency / 60, 16);
        check(frames.Read(tick + frequency / 60)?.Low is null, "1% Low remains unavailable during warmup");
        const string csv = "\"game,name.exe\",42,0xABC,DXGI,0,0,0,5.5,0.1,16.67";
        check(FrameReader.Column(csv, 0).SequenceEqual("game,name.exe") && FrameReader.TryValue(csv, 9, out var ms) && ms == 16.67 && FrameReader.Column(csv, 20).IsEmpty, "CSV parsing preserves quoted commas and locates numeric fields");
        var bytes = new byte[560];
        void U(int offset, uint value) => System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        U(0, 48); U(4, 48); U(8, 3); U(24, 40); U(28, 512); bytes[49] = 65; bytes[50] = 1;
        check(NvmeReader.ParseTemperature(bytes) is > 47 and < 49, "NVMe Kelvin temperature is decoded from a validated protocol descriptor");
        U(24, uint.MaxValue); check(NvmeReader.ParseTemperature(bytes) is null, "Malformed NVMe data offsets cannot read beyond the response");
        U(24, 40); bytes[49] = bytes[50] = 0; check(NvmeReader.ParseTemperature(bytes) is null && NvmeReader.ParseTemperature(bytes.AsSpan(0, 47)) is null, "Missing and truncated NVMe temperatures do not become zero Celsius");
        var config = new Settings { Layout = new() { Slots = ["invalid"] } }.Capture();
        check(config.Layout.Slots.Length == 12 && config.Layout.Slots.All(PanelLayout.Choices.Contains), "Invalid layout slots normalize to twelve supported metrics");
        var rich = RichFixture();
        check(PanelMetrics.Get("fan", rich, config, tick).Value == 1314 && PanelMetrics.Get("pump", rich, config, tick).Value == 2575, "Only explicit CPU Fan and Pump Fan names map automatically");
        check(PanelMetrics.Get("fan", rich, config with { CpuFanSensorId = "missing" }, tick).Value is null, "A missing configured fan never falls back to an unrelated header");
        check(SystemActivityReader.IsVirtual("Mihomo Meta Tunnel") && !SystemActivityReader.IsVirtual("Intel Ethernet Controller"), "Automatic network selection prefers physical adapters over recognized VPN interfaces");
        using var editor = new LayoutForm(new Settings(), rich.Details, rich, _ => { });
        check(!editor.Visible && editor.Controls.Count > 0, "Layout editor and real-data preview construct without showing a window");
    }
    private static void VerifyGauges(Action<bool, string> check)
    {
        check(UpperGauge.Point(205, UpperGauge.Angle(true, 100)).Y < 36 && UpperGauge.Point(205, UpperGauge.Angle(false, 100)).Y < 36,
            "Both utilization quarters reach one hundred percent at the screen top");
        check(UpperGauge.Point(205, UpperGauge.Angle(true, 0)).X == 35 && UpperGauge.Point(205, UpperGauge.Angle(false, 0)).X == 445,
            "Left and right utilization quarters progress from opposite side baselines");
        var collision = false; var outside = false; var coversTemperature = false;
        foreach (var left in new[] { true, false }) for (var outer = 0; outer <= 100; outer++) for (var inner = 0; inner <= 100; inner++)
        {
            var a = UpperGauge.Label(left, false, outer); var b = UpperGauge.Label(left, true, inner, a.Bounds);
            collision |= a.Bounds.IntersectsWith(b.Bounds);
            var main = left ? new RectangleF(99, 126, 133, 118) : new RectangleF(248, 126, 133, 118);
            var clock = new RectangleF(142, 96, 184, 40);
            coversTemperature |= a.Bounds.IntersectsWith(main) || b.Bounds.IntersectsWith(main) || a.Bounds.IntersectsWith(clock) || b.Bounds.IntersectsWith(clock);
            foreach (var box in new[] { a.Bounds, b.Bounds }) foreach (var p in new[] { new PointF(box.Left, box.Top), new(box.Right, box.Top), new(box.Left, box.Bottom), new(box.Right, box.Bottom) })
                outside |= (p.X - 240) * (p.X - 240) + (p.Y - 240) * (p.Y - 240) > 216 * 216;
        }
        check(!collision && !coversTemperature, "All integer percentage pairs keep following labels separate from each other, clock and large temperatures");
        check(!outside, "Following percentage labels remain inside the circular safety area at every integer percentage");
    }
    private static void VerifyDetails(string directory, Action<bool, string> check)
    {
        var cache = new DetailCache(); var now = Stopwatch.GetTimestamp();
        var good = cache.Capture("dimm/0", "内存条温度", "°C", 44.75, "离线DIMM样本");
        var failed = cache.Capture("dimm/0", "内存条温度", "°C", null, "离线DIMM样本", "模拟SMBus读取失败");
        check(failed.Reading.SampleTick == good.Reading.SampleTick && failed.Reading.SampledAt == good.Reading.SampledAt && failed.Reading.Value == good.Reading.Value,
            "Failed DIMM reads retain last successful time rather than refreshing a cached value");
        check(failed.Availability == DetailAvailability.Unavailable && DetailFormatting.Value(failed, now).Contains("暂不可用"), "Read failure is visible instead of a fresh temperature");
        var unsupported = cache.Capture("gpu/unsupported", "不支持的指标", "%", null, "离线样本", "硬件未支持", DetailAvailability.Unsupported);
        check(DetailFormatting.Value(unsupported, now).Contains("不支持："), "Unsupported and temporarily unavailable are distinct");
        var zero = cache.Capture("igpu/3d", "3D引擎 #0", "%", 0, "离线核显样本");
        check(DetailFormatting.Value(zero, now).StartsWith("0") && DetailFormatting.IntegratedStatus([zero]).Contains("均为0%"), "Zero iGPU activity is a valid reading, not disabled hardware");
        check(DetailFormatting.IntegratedStatus([]).Contains("暂不可用"), "Missing iGPU samples do not imply zero activity");
        check(DetailFormatting.Value(good, now + 20 * Stopwatch.Frequency).Contains("旧值"), "Extended readings expire independently after 15s");
        check(DetailFormatting.Percent(5, 0) is null && DetailFormatting.Percent(17, 16) is null && DetailFormatting.Percent(double.PositiveInfinity, double.PositiveInfinity) is null,
            "Invalid video memory totals never produce percentages");
        check(DetailFormatting.ToGiB(1073741824) == 1 && HardwareSensorRules.Normalize(LibreHardwareMonitor.Hardware.SensorType.SmallData, 1024) == 1,
            "NVML bytes and D3D MiB convert to GiB correctly");
        check(System.Runtime.InteropServices.Marshal.SizeOf<NvidiaReader.MemoryV2>() == 40
            && System.Runtime.InteropServices.Marshal.OffsetOf<NvidiaReader.MemoryV2>(nameof(NvidiaReader.MemoryV2.Used)).ToInt32() == 32, "NVML v2 structure matches x64 ABI");
        var changedSource = cache.Capture("dimm/0", "内存条温度", "°C", null, "另一条DIMM", "暂不可用");
        check(changedSource.Reading.Value is null && changedSource.Reading.SampledAt is null, "A different source cannot inherit another module's temperature");
        var gpu = new DetailDevice("nvidia", "nvidia", "离线独显样本", "离线测试", [unsupported]);
        var details = new HardwareDetails(DateTimeOffset.Now, now,
        [new("dimm/0", "memory", "离线内存样本 #0", "模拟数据", [good]), new("dimm/2", "memory", "离线内存样本 #2", "模拟缺失", [failed]), gpu,
            new("igpu", "igpu", "离线核显样本", DetailFormatting.IntegratedStatus([zero]), [zero], "共享内存来自系统RAM，不与独显显存合并。")]);
        var sample = Fixture() with { Details = details };
        check(sample.Summary(1, now) == "正常更新", "Optional hardware failures do not mark base screen metrics unavailable");
        var rich = RichFixture(); var compact = CompactHardware.From(rich, Stopwatch.GetTimestamp());
        check(compact.CpuPower == 126 && compact.VramUsed == 5.9 && compact.MemoryTemperatures.Length == 2 && compact.IntegratedUsage == 0,
            "Dense screen includes power VRAM DIMM temperatures and valid zero iGPU activity");
        var staleDetails = rich.Details! with { Devices = rich.Details!.Devices.Select(d => d with { Metrics = d.Metrics.Select(metric => metric with
            { Reading = metric.Reading with { SampleTick = metric.Reading.SampleTick - 20 * Stopwatch.Frequency } }).ToArray() }).ToArray() };
        var expired = CompactHardware.From(rich with { Details = staleDetails }, Stopwatch.GetTimestamp());
        check(expired.VramUsed is null && expired.IntegratedUsage is null && expired.MemoryTemperatures.All(v => v is null), "Dense optional fields never show expired values as live");
        var partialEngine = rich.Details! with { Devices = rich.Details!.Devices.Select(d => d.Category == "igpu"
            ? d with { Metrics = [.. d.Metrics, failed with { Id = "igpu/missing", Unit = "%" }] } : d).ToArray() };
        check(CompactHardware.From(rich with { Details = partialEngine }, Stopwatch.GetTimestamp()).IntegratedUsage is null,
            "An unavailable iGPU engine prevents a misleading overall zero");
        using var view = new HardwareDetailsView { Font = new Font("Microsoft YaHei UI", 11.25f), BackColor = Ui.Background };
        view.Width = Ui.ScaleSize(view, 520, 0).Width;
        view.UpdateDetails(details); view.PerformLayout();
        view.Height = view.GetPreferredSize(new Size(view.Width, 0)).Height;
        view.CreateControl(); view.PerformLayout();
        check(view.Controls.Count > 1 && view.Height > 0 && view.Width > 0, "Detached hardware details control lays out without opening a window");
        using var bitmap = new Bitmap(view.Width, view.Height);
        view.DrawToBitmap(bitmap, new(0, 0, bitmap.Width, bitmap.Height)); bitmap.Save(Path.Combine(directory, "details-offscreen.png"));
        using var overview = new CompactOverview { Width = Ui.ScaleSize(view, 500, 0).Width };
        overview.UpdateMetrics(RichFixture(), 1); overview.CreateControl();
        using var dashboard = new Bitmap(overview.Width, overview.Height);
        overview.DrawToBitmap(dashboard, new(0, 0, dashboard.Width, dashboard.Height)); dashboard.Save(Path.Combine(directory, "overview-offscreen.png"));
        check(overview.Height / (overview.DeviceDpi / 96f) <= 568, "Compact overview shows CPU GPU RAM VRAM iGPU within 568 DIP");
        overview.Width = Ui.ScaleSize(view, 442, 0).Width; overview.UpdateMetrics(RichFixture(Fixture(100, 100, 47.7), true), 1);
        using var extremeDashboard = new Bitmap(overview.Width, overview.Height);
        overview.DrawToBitmap(extremeDashboard, new(0, 0, extremeDashboard.Width, extremeDashboard.Height)); extremeDashboard.Save(Path.Combine(directory, "overview-extreme-offscreen.png"));
        using var compactDigits = DigitProbe.Render(82);
        using var unrestrictedDigits = DigitProbe.Render(220);
        check(Fingerprint(new Bitmap(compactDigits)) == Fingerprint(new Bitmap(unrestrictedDigits)),
            "A narrow numeric slot renders every digit of 100 identically to unrestricted layout");
    }
    private static void CheckSafety(Bitmap bitmap, string name, Action<bool, string> check)
    {
        var outside = 0; var background = Ui.Background.ToArgb(); var firstOutside = new List<string>();
        for (var y = 0; y < 480; y++) for (var x = 0; x < 480; x++)
            if ((x - 240) * (x - 240) + (y - 240) * (y - 240) > (y < 240 ? 216 * 216 : 238 * 238) && bitmap.GetPixel(x, y).ToArgb() != background)
            { outside++; if (firstOutside.Count < 5) firstOutside.Add($"{x},{y}"); }
        check(outside == 0, name + " raster stays within upper r216 / lower r238 bounds (outside=" + outside + "; " + string.Join(" ", firstOutside) + ")");
    }
    private static void RenderWindows(string directory, Action<bool, string> check)
    {
        using var renderer = new DisplayRenderer();
        var fixture = Fixture(); renderer.Draw(fixture);
        var config = new Settings();
        var status = new MonitorStatus(fixture, ConnectionPhase.Connected, "离线验证", "模拟屏幕", LastSentAt: DateTimeOffset.Now, LastSentTick: Stopwatch.GetTimestamp(), LastFrameKind: "离线测试画面");
        using var form = new StatusForm(config);
        form.UpdateStatus(status, new Bitmap(renderer.Canvas)); form.Show(); Application.DoEvents();
        Capture(form, Path.Combine(directory, "desktop-wide.png"));
        File.WriteAllText(Path.Combine(directory, "layout-wide.json"), JsonSerializer.Serialize(Layout(form), new JsonSerializerOptions { WriteIndented = true }));
        check(form.Controls[0].Controls.Cast<Control>().All(c => c.Width > 100), "Desktop columns have measurable width");
        form.ClientSize = Ui.ScaleSize(form, 640, 560); Application.DoEvents(); Capture(form, Path.Combine(directory, "desktop-narrow.png"));
        File.WriteAllText(Path.Combine(directory, "layout-narrow.json"), JsonSerializer.Serialize(Layout(form), new JsonSerializerOptions { WriteIndented = true }));
        check(!form.HasHorizontalScroll, "Narrow desktop has no horizontal overflow");
        var largeFonts = Descendants(form).Select(c => (Control: c, Font: new Font(c.Font.FontFamily, c.Font.Size * 1.5f, c.Font.Style))).ToArray();
        foreach (var item in largeFonts) item.Control.Font = item.Font;
        var longReadings = fixture.Readings.ToDictionary(p => p.Key, p => p.Value);
        longReadings[MetricId.CpuTemperature] = longReadings[MetricId.CpuTemperature] with { Failure = "CPU温度暂不可读。这是一条用于验证换行的较长中文说明，请查看数据来源、硬件访问权限和实际失败原因。" };
        form.UpdateStatus(status with { Metrics = fixture with { Readings = longReadings }, Error = "屏幕端口无法打开，可能正被其他程序占用。请确认原SAMA已退出，再重试。" }, null);
        Application.DoEvents(); Capture(form, Path.Combine(directory, "desktop-large-text.png"));
        check(!form.HasHorizontalScroll, "150 percent text and long Chinese messages reflow without horizontal overflow");
        form.ShowDiagnostics(); Application.DoEvents(); Capture(form, Path.Combine(directory, "desktop-diagnostics.png"));
        form.Close();
        using var quick = new QuickForm(config, action => action(config), () => { }, () => { });
        quick.UpdateStatus(status); quick.OpenNear(Screen.PrimaryScreen!.WorkingArea.Location + new Size(700, 700)); Application.DoEvents(); Capture(quick, Path.Combine(directory, "quick.png")); quick.Close();
    }
    private static void RenderIcons(string directory, Action<bool, string> check)
    {
        using var application = IconAssets.Draw(256, TrayMark.Normal, false, true);
        application.Save(Path.Combine(directory, "application-icon.png"));
        using var icons = new Bitmap(480, 210); using var g = Graphics.FromImage(icons); g.Clear(Ui.Background);
        var row = 0;
        foreach (var mark in Enum.GetValues<TrayMark>())
        {
            var x = 10;
            foreach (var size in new[] { 16, 20, 24, 32 })
            {
                using var icon = IconAssets.Draw(size, mark, false); g.DrawImageUnscaled(icon, x, row * 40 + 5); x += size + 15;
                using var lightIcon = IconAssets.Draw(size, mark, true); g.FillRectangle(Brushes.WhiteSmoke, 230 + x - size - 15, row * 40, size + 8, 38); g.DrawImageUnscaled(lightIcon, 230 + x - size - 12, row * 40 + 5);
            }
            row++;
        }
        icons.Save(Path.Combine(directory, "tray-icons.png"));
        File.WriteAllBytes(Path.Combine(directory, "SamaLite.ico"), IconAssets.Export(TrayMark.Normal, application: true));
        using var roundTrip = new Icon(Path.Combine(directory, "SamaLite.ico")); check(roundTrip.Width >= 16, "Multi-size ICO round-trip");
    }
    private static void Capture(Form form, string path)
    {
        using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new(0, 0, bitmap.Width, bitmap.Height)); bitmap.Save(path, ImageFormat.Png);
    }
    private static object Layout(Control control) => new { Type = control.GetType().Name, control.Text, control.DeviceDpi,
        Bounds = control.Bounds.ToString(), Client = control.ClientSize.ToString(), control.Visible, Children = control.Controls.Cast<Control>().Select(Layout).ToArray() };
    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control control in parent.Controls) { yield return control; foreach (var child in Descendants(control)) yield return child; }
    }
    private static void VerifyWorker(Action<bool, string> check)
    {
        var settings = new Settings(); var device = new FakeDevice();
        using var worker = new MonitorWorker(settings, () => new FakeSource(), () => new FakeOutput(device), false);
        WaitUntil(() => worker.Status.Frames >= 2, "initial frames");
        lock (settings) settings.Paused = true; worker.SettingsChanged();
        WaitUntil(() => worker.Status.LastFrameKind == "暂停终态", "pause marker");
        var frozen = worker.Status; var sampled = frozen.Metrics!.Timestamp;
        var pausedPreview = Fingerprint(worker.CopyPreview());
        using (var detached = worker.CopyPreview()) detached!.SetPixel(0, 0, Color.Red);
        check(Fingerprint(worker.CopyPreview()) == pausedPreview, "A desktop preview cannot mutate the last completed screen frame");
        WaitUntil(() => worker.Status.Metrics!.Timestamp > sampled.AddMilliseconds(500), "collection while paused");
        check(worker.Status.Frames == frozen.Frames, "Pause freezes output while desktop samples advance");
        check(worker.Status.PreviewVersion == frozen.PreviewVersion, "Paused preview is immutable");
        lock (settings) settings.RefreshSeconds = 2; worker.SettingsChanged();
        sampled = worker.Status.Metrics!.Timestamp;
        WaitUntil(() => worker.Status.Metrics!.Timestamp > sampled.AddMilliseconds(500), "sample after interval change");
        check(worker.Status.Frames == frozen.Frames, "Changing refresh interval while paused does not redraw the terminal frame");
        lock (settings) settings.RefreshSeconds = 1; worker.SettingsChanged();
        Interlocked.Exchange(ref device.FailNext, 1);
        lock (settings) settings.Rotation = 180; worker.SettingsChanged();
        WaitUntil(() => worker.Status.PauseMarkerFailed, "failed pause frame");
        check(worker.Status.PreviewVersion == frozen.PreviewVersion && worker.Status.LastSentAt == frozen.LastSentAt, "Failed send preserves last completed preview and timestamp");
        WaitUntil(() => worker.Status.Phase == ConnectionPhase.Connected && !worker.Status.PauseMarkerFailed, "auto reconnect", 10000);
        check(worker.Status.Paused && worker.Status.LastFrameKind == "暂停终态", "Auto reconnect preserves pause intent");
        check(Fingerprint(worker.CopyPreview()) == pausedPreview, "Reconnected pause frame preserves original readings and freeze time");
        var before = worker.Status.Frames; worker.SettingsChanged(true);
        WaitUntil(() => worker.Status.Frames > before, "manual reconnect paused");
        check(worker.Status.Paused, "Manual reconnect preserves pause intent");
        lock (settings) settings.Paused = false; worker.SettingsChanged();
        WaitUntil(() => worker.Status.LastFrameKind == "监控画面", "resume");
        check(worker.Status.Phase == ConnectionPhase.Connected, "Resume sends current frame");
    }
    private static void WaitUntil(Func<bool> predicate, string name, int milliseconds = 6000)
    {
        var clock = Stopwatch.StartNew();
        while (!predicate()) { if (clock.ElapsedMilliseconds > milliseconds) throw new TimeoutException(name); Thread.Sleep(20); }
    }
    private static string Fingerprint(Bitmap? bitmap)
    {
        using (bitmap)
        {
            if (bitmap is null) return "none";
            using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png);
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream.ToArray()));
        }
    }
    private sealed class FakeSource : IMetricSource { public Metrics Read() => Fixture(); public void Dispose() { } }
    private sealed class DigitProbe : TooltipCanvas
    {
        internal static Bitmap Render(float width)
        {
            var bitmap = new Bitmap(300, 100);
            using var g = Graphics.FromImage(bitmap); g.Clear(Ui.Background);
            Value(g, 100, "%", 240 - width, 5, width, 44, Ui.Text);
            return bitmap;
        }
    }
    private sealed class FakeDevice { public int FailNext; }
    private sealed class FakeOutput(FakeDevice state) : IScreenOutput
    {
        public string Port => "模拟端口"; public string Identity => "离线测试";
        public long BytesSent { get; private set; } public long BytesReceived => 0; public string LastStatus => "模拟发送完成";
        public void Open() { } public void SetBrightness(int value) { }
        public void Send(Rectangle region, byte[] pixels, int rotation)
        {
            if (Interlocked.Exchange(ref state.FailNext, 0) != 0) throw new IOException("模拟传输失败"); BytesSent += pixels.Length;
        }
        public void Dispose() { }
    }
}
