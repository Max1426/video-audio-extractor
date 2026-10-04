using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VideoAudioExtractor.Models;

public enum ConversionState
{
    Waiting,
    Probing,
    Converting,
    Success,
    Failed,
    Cancelled
}

public sealed class ConversionTaskItem : INotifyPropertyChanged
{
    private ConversionState _state = ConversionState.Waiting;
    private double _progress;
    private string _statusText = "等待中";
    private string _detail = "等待转换";
    private TimeSpan? _duration;
    private string? _outputPath;

    public required string FilePath { get; init; }
    public string FileName => Path.GetFileName(FilePath);
    public string DisplayPath => Path.GetDirectoryName(FilePath) ?? string.Empty;
    public TimeSpan? Duration { get => _duration; set { _duration = value; OnPropertyChanged(); OnPropertyChanged(nameof(DurationText)); } }
    public string DurationText => Duration is null ? "—" : Duration.Value.ToString(Duration.Value.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss");
    public ConversionState State { get => _state; set { _state = value; OnPropertyChanged(); } }
    public double Progress { get => _progress; set { _progress = Math.Clamp(value, 0, 100); OnPropertyChanged(); } }
    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }
    public string Detail { get => _detail; set { _detail = value; OnPropertyChanged(); } }
    public string? OutputPath { get => _outputPath; set { _outputPath = value; OnPropertyChanged(); } }

    public void Reset()
    {
        State = ConversionState.Waiting;
        Progress = 0;
        StatusText = "等待中";
        Detail = "等待转换";
        OutputPath = null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

