using System.Diagnostics;
using VideoAudioExtractor.Services;

var failures = new List<string>();
var passed = new List<string>();
var tempRoot = Path.Combine(Path.GetTempPath(), $"视频音轨提取测试-{Guid.NewGuid():N}");
Directory.CreateDirectory(tempRoot);

try
{
    var ffmpeg = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe");
    var service = new FfmpegService();
    Check(service.IsAvailable, "FFmpeg 组件可用");

    var sampleVideo = Path.Combine(tempRoot, "中文 空格 样本.mp4");
    await RunAsync(ffmpeg,
        "-hide_banner", "-loglevel", "error", "-y",
        "-f", "lavfi", "-i", "color=c=blue:s=320x240:d=2",
        "-f", "lavfi", "-i", "sine=frequency=880:duration=2",
        "-shortest", "-c:v", "mpeg4", "-c:a", "aac", sampleVideo);

    var probe = await service.ProbeAsync(sampleVideo, CancellationToken.None);
    Check(probe.Success && probe.HasAudio && probe.Duration.TotalSeconds is > 1.8 and < 2.2, "中文路径视频预检与音轨识别");

    foreach (var bitrate in new[] { 128, 192, 320 })
    {
        var output = Path.Combine(tempRoot, $"输出-{bitrate}.mp3");
        var progressValues = new List<double>();
        var result = await service.ConvertAsync(sampleVideo, output, bitrate, probe.Duration, new Progress<double>(value => progressValues.Add(value)), CancellationToken.None);
        Check(result.Success && File.Exists(output) && new FileInfo(output).Length > 10_000, $"{bitrate} kbps MP3 转换");
        var outputProbe = await service.ProbeAsync(output, CancellationToken.None);
        Check(outputProbe.Success && outputProbe.HasAudio && Math.Abs(outputProbe.Duration.TotalSeconds - probe.Duration.TotalSeconds) < 0.2, $"{bitrate} kbps 输出时长校验");
    }

    var videoOnly = Path.Combine(tempRoot, "无音轨.mp4");
    await RunAsync(ffmpeg,
        "-hide_banner", "-loglevel", "error", "-y",
        "-f", "lavfi", "-i", "color=c=black:s=160x120:d=1",
        "-c:v", "mpeg4", videoOnly);
    var noAudioProbe = await service.ProbeAsync(videoOnly, CancellationToken.None);
    Check(noAudioProbe.Success && !noAudioProbe.HasAudio, "无音轨视频识别");

    var broken = Path.Combine(tempRoot, "损坏视频.mp4");
    await File.WriteAllTextAsync(broken, "not a media file");
    var brokenProbe = await service.ProbeAsync(broken, CancellationToken.None);
    Check(!brokenProbe.Success, "损坏文件错误处理");

    var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var firstPath = OutputPathHelper.GetUniquePath(tempRoot, sampleVideo, reserved);
    await File.WriteAllTextAsync(firstPath, "existing");
    var secondPath = OutputPathHelper.GetUniquePath(tempRoot, sampleVideo, reserved);
    Check(firstPath != secondPath && Path.GetFileName(secondPath).Contains("(1)"), "同名输出自动避让");

    var longVideo = Path.Combine(tempRoot, "取消测试.mp4");
    await RunAsync(ffmpeg,
        "-hide_banner", "-loglevel", "error", "-y",
        "-f", "lavfi", "-i", "color=c=red:s=640x360:d=20",
        "-f", "lavfi", "-i", "sine=frequency=440:duration=20",
        "-shortest", "-c:v", "mpeg4", "-c:a", "aac", longVideo);
    var longProbe = await service.ProbeAsync(longVideo, CancellationToken.None);
    using var cts = new CancellationTokenSource();
    cts.Cancel();
    var cancelledOutput = Path.Combine(tempRoot, "取消输出.mp3");
    var cancelled = await service.ConvertAsync(longVideo, cancelledOutput, 320, longProbe.Duration, new Progress<double>(), cts.Token);
    Check(cancelled.Cancelled, "转换取消路径可安全结束");
}
catch (Exception ex)
{
    failures.Add($"未处理异常：{ex}");
}
finally
{
    try { Directory.Delete(tempRoot, recursive: true); } catch { }
}

foreach (var item in passed) Console.WriteLine($"PASS  {item}");
foreach (var item in failures) Console.WriteLine($"FAIL  {item}");
Console.WriteLine($"结果：{passed.Count} 通过，{failures.Count} 失败");
return failures.Count == 0 ? 0 : 1;

void Check(bool condition, string name)
{
    if (condition) passed.Add(name); else failures.Add(name);
}

static async Task RunAsync(string executable, params string[] arguments)
{
    var startInfo = new ProcessStartInfo(executable)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardError = true
    };
    foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"无法启动 {executable}");
    var error = await process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    if (process.ExitCode != 0) throw new InvalidOperationException(error);
}
