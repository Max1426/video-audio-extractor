using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using VideoAudioExtractor.Models;

namespace VideoAudioExtractor.Services;

public sealed record ProbeResult(bool Success, bool HasAudio, TimeSpan Duration, string Error);
public sealed record ConversionResult(bool Success, bool Cancelled, string Error);

public sealed class FfmpegService
{
    private readonly string _ffmpegPath;
    private readonly string _ffprobePath;
    private readonly object _processLock = new();
    private Process? _activeProcess;

    public FfmpegService()
    {
        _ffmpegPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe");
        _ffprobePath = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffprobe.exe");
    }

    public bool IsAvailable => File.Exists(_ffmpegPath) && File.Exists(_ffprobePath);

    public async Task<ProbeResult> ProbeAsync(string inputPath, CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo(_ffprobePath);
        AddArguments(startInfo, "-v", "error", "-show_entries", "format=duration:stream=codec_type", "-of", "json", inputPath);

        try
        {
            using var process = new Process { StartInfo = startInfo };
            SetActiveProcess(process);
            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
                return new(false, false, TimeSpan.Zero, FriendlyError(stderr, "无法读取视频信息"));

            using var document = JsonDocument.Parse(stdout);
            var hasAudio = document.RootElement.TryGetProperty("streams", out var streams) &&
                           streams.EnumerateArray().Any(stream => stream.TryGetProperty("codec_type", out var type) && type.GetString() == "audio");
            var duration = TimeSpan.Zero;
            if (document.RootElement.TryGetProperty("format", out var format) &&
                format.TryGetProperty("duration", out var durationElement) &&
                double.TryParse(durationElement.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            {
                duration = TimeSpan.FromSeconds(seconds);
            }

            return new(true, hasAudio, duration, string.Empty);
        }
        catch (OperationCanceledException)
        {
            CancelActive();
            return new(false, false, TimeSpan.Zero, "已取消");
        }
        catch (Exception ex)
        {
            return new(false, false, TimeSpan.Zero, FriendlyError(ex.Message, "无法读取视频信息"));
        }
        finally
        {
            ClearActiveProcess();
        }
    }

    public async Task<ConversionResult> ConvertAsync(
        string inputPath,
        string temporaryOutputPath,
        int bitrateKbps,
        TimeSpan duration,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo(_ffmpegPath);
        AddArguments(startInfo,
            "-hide_banner", "-nostdin", "-y", "-i", inputPath,
            "-map", "0:a:0", "-vn", "-codec:a", "libmp3lame", "-b:a", $"{bitrateKbps}k", "-ac", "2",
            "-progress", "pipe:1", "-nostats", temporaryOutputPath);

        var errors = new StringBuilder();
        try
        {
            using var process = new Process { StartInfo = startInfo };
            SetActiveProcess(process);
            process.Start();

            var stderrTask = Task.Run(async () =>
            {
                while (await process.StandardError.ReadLineAsync(cancellationToken) is { } line)
                {
                    if (errors.Length < 16_000) errors.AppendLine(line);
                }
            }, cancellationToken);

            while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
            {
                if (line.StartsWith("out_time=", StringComparison.Ordinal) &&
                    TimeSpan.TryParse(line[9..], CultureInfo.InvariantCulture, out var current) &&
                    duration.TotalMilliseconds > 0)
                {
                    progress.Report(Math.Min(99.5, current.TotalMilliseconds / duration.TotalMilliseconds * 100));
                }
            }

            await process.WaitForExitAsync(cancellationToken);
            await stderrTask;
            if (process.ExitCode != 0)
                return new(false, false, FriendlyError(errors.ToString(), "音轨转换失败"));

            progress.Report(100);
            return new(true, false, string.Empty);
        }
        catch (OperationCanceledException)
        {
            CancelActive();
            return new(false, true, "已取消");
        }
        catch (Exception ex)
        {
            return new(false, false, FriendlyError(ex.Message, "音轨转换失败"));
        }
        finally
        {
            ClearActiveProcess();
        }
    }

    public void CancelActive()
    {
        lock (_processLock)
        {
            try
            {
                if (_activeProcess is { HasExited: false })
                {
                    _activeProcess.Kill(entireProcessTree: true);
                    _activeProcess.WaitForExit(3000);
                }
            }
            catch
            {
                // 进程可能刚好已经退出。
            }
        }
    }

    private static ProcessStartInfo CreateStartInfo(string executable) => new()
    {
        FileName = executable,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8
    };

    private static void AddArguments(ProcessStartInfo startInfo, params string[] arguments)
    {
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
    }

    private static string FriendlyError(string rawError, string fallback)
    {
        if (rawError.Contains("Permission denied", StringComparison.OrdinalIgnoreCase)) return "没有权限读取源文件或写入输出目录";
        if (rawError.Contains("Invalid data", StringComparison.OrdinalIgnoreCase)) return "文件已损坏或不是受支持的媒体格式";
        if (rawError.Contains("No such file", StringComparison.OrdinalIgnoreCase)) return "找不到源文件";
        if (rawError.Contains("No space left", StringComparison.OrdinalIgnoreCase)) return "磁盘剩余空间不足";
        return string.IsNullOrWhiteSpace(rawError) ? fallback : $"{fallback}：{LastMeaningfulLine(rawError)}";
    }

    private static string LastMeaningfulLine(string value) =>
        value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? value.Trim();

    private void SetActiveProcess(Process process)
    {
        lock (_processLock) _activeProcess = process;
    }

    private void ClearActiveProcess()
    {
        lock (_processLock) _activeProcess = null;
    }
}
