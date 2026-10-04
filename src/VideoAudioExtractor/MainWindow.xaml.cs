using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using VideoAudioExtractor.Models;
using VideoAudioExtractor.Services;

namespace VideoAudioExtractor;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private static readonly string[] VideoExtensions =
    [
        ".mp4", ".mov", ".mkv", ".avi", ".webm", ".m4v", ".wmv", ".flv", ".mpeg", ".mpg", ".ts", ".mts", ".m2ts", ".3gp"
    ];

    private readonly FfmpegService _ffmpeg = new();
    private readonly SettingsService _settingsService = new();
    private readonly AppSettings _settings;
    private CancellationTokenSource? _conversionCts;
    private bool _isConverting;
    private ConversionTaskItem? _selectedTask;

    public ObservableCollection<ConversionTaskItem> Tasks { get; } = [];

    public ConversionTaskItem? SelectedTask
    {
        get => _selectedTask;
        set { _selectedTask = value; PropertyChanged?.Invoke(this, new(nameof(SelectedTask))); }
    }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        _settings = _settingsService.Load();
        OutputDirectoryTextBox.Text = _settings.OutputDirectory;
        SelectBitrate(_settings.BitrateKbps);
        Tasks.CollectionChanged += (_, _) => UpdateControls();
        UpdateControls();
    }

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择一个或多个视频",
            Multiselect = true,
            Filter = "视频文件|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.m4v;*.wmv;*.flv;*.mpeg;*.mpg;*.ts;*.mts;*.m2ts;*.3gp|所有文件|*.*"
        };
        if (dialog.ShowDialog(this) == true) AddFiles(dialog.FileNames);
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        var existing = Tasks.Select(task => task.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (!File.Exists(path) || !VideoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) || !existing.Add(path)) continue;
            Tasks.Add(new ConversionTaskItem { FilePath = Path.GetFullPath(path) });
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = !_isConverting && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (_isConverting || e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        AddFiles(paths);
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择 MP3 保存位置",
            InitialDirectory = Directory.Exists(OutputDirectoryTextBox.Text) ? OutputDirectoryTextBox.Text : null
        };
        if (dialog.ShowDialog(this) == true)
        {
            OutputDirectoryTextBox.Text = dialog.FolderName;
            SaveSettings();
        }
    }

    private void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in TasksGrid.SelectedItems.Cast<ConversionTaskItem>().ToList()) Tasks.Remove(item);
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => Tasks.Clear();

    private void TasksGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateControls();

    private void OpenOutput_Click(object sender, RoutedEventArgs e)
    {
        if (TasksGrid.SelectedItem is not ConversionTaskItem task ||
            task.State != ConversionState.Success ||
            string.IsNullOrWhiteSpace(task.OutputPath) ||
            !File.Exists(task.OutputPath)) return;
        var info = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        info.ArgumentList.Add("/select,");
        info.ArgumentList.Add(task.OutputPath);
        Process.Start(info);
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_isConverting || Tasks.Count == 0) return;
        if (!_ffmpeg.IsAvailable)
        {
            MessageBox.Show(this, "程序组件 FFmpeg 不完整，请重新安装本软件。", "缺少转换组件", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var outputDirectory = OutputDirectoryTextBox.Text.Trim();
        try
        {
            Directory.CreateDirectory(outputDirectory);
            var probePath = Path.Combine(outputDirectory, $".write-test-{Guid.NewGuid():N}");
            await File.WriteAllTextAsync(probePath, string.Empty);
            File.Delete(probePath);
        }
        catch
        {
            MessageBox.Show(this, "无法写入所选保存目录，请选择其他位置。", "保存位置不可用", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _isConverting = true;
        _conversionCts = new CancellationTokenSource();
        SaveSettings();
        UpdateControls();

        var bitrate = GetSelectedBitrate();
        var reservedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var task in Tasks) task.Reset();

        try
        {
            foreach (var task in Tasks)
            {
                if (_conversionCts.IsCancellationRequested)
                {
                    MarkCancelled(task);
                    continue;
                }

                task.State = ConversionState.Probing;
                task.StatusText = "正在检查";
                task.Detail = "读取音轨信息…";
                var probe = await _ffmpeg.ProbeAsync(task.FilePath, _conversionCts.Token);

                if (_conversionCts.IsCancellationRequested)
                {
                    MarkCancelled(task);
                    continue;
                }
                if (!probe.Success)
                {
                    MarkFailed(task, probe.Error);
                    continue;
                }
                if (!probe.HasAudio)
                {
                    MarkFailed(task, "视频中没有可提取的音轨");
                    continue;
                }

                task.Duration = probe.Duration;
                task.State = ConversionState.Converting;
                task.StatusText = "转换中";
                task.Detail = "正在生成 MP3…";

                var finalPath = OutputPathHelper.GetUniquePath(outputDirectory, task.FilePath, reservedPaths);
                var tempPath = Path.Combine(outputDirectory, $".{Path.GetFileNameWithoutExtension(finalPath)}.{Guid.NewGuid():N}.tmp.mp3");
                var progress = new Progress<double>(value =>
                {
                    task.Progress = value;
                    task.Detail = $"已完成 {value:0}%";
                });

                var result = await _ffmpeg.ConvertAsync(task.FilePath, tempPath, bitrate, probe.Duration, progress, _conversionCts.Token);
                if (result.Success)
                {
                    try
                    {
                        File.Move(tempPath, finalPath);
                        task.OutputPath = finalPath;
                        task.State = ConversionState.Success;
                        task.StatusText = "已完成";
                        task.Detail = Path.GetFileName(finalPath);
                        task.Progress = 100;
                    }
                    catch (Exception ex)
                    {
                        TryDelete(tempPath);
                        MarkFailed(task, $"无法保存成品：{ex.Message}");
                    }
                }
                else
                {
                    TryDelete(tempPath);
                    if (result.Cancelled) MarkCancelled(task); else MarkFailed(task, result.Error);
                }
            }
        }
        finally
        {
            _isConverting = false;
            _conversionCts.Dispose();
            _conversionCts = null;
            UpdateControls();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _conversionCts?.Cancel();
        _ffmpeg.CancelActive();
        foreach (var task in Tasks.Where(item => item.State is ConversionState.Waiting or ConversionState.Probing)) MarkCancelled(task);
    }

    private void BitrateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) SaveSettings();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        SaveSettings();
        _conversionCts?.Cancel();
        _ffmpeg.CancelActive();
    }

    private void UpdateControls()
    {
        if (!IsInitialized) return;
        CountText.Text = $"{Tasks.Count} 个文件";
        EmptyState.Visibility = Tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AddFilesButton.IsEnabled = !_isConverting;
        BrowseOutputButton.IsEnabled = !_isConverting;
        BitrateComboBox.IsEnabled = !_isConverting;
        RemoveButton.IsEnabled = !_isConverting && TasksGrid.SelectedItems.Count > 0;
        ClearButton.IsEnabled = !_isConverting && Tasks.Count > 0;
        StartButton.IsEnabled = !_isConverting && Tasks.Count > 0;
        StartButton.Visibility = _isConverting ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Visibility = _isConverting ? Visibility.Visible : Visibility.Collapsed;
        OpenOutputButton.IsEnabled = TasksGrid.SelectedItem is ConversionTaskItem { State: ConversionState.Success, OutputPath: not null };
    }

    private void SaveSettings()
    {
        _settings.OutputDirectory = OutputDirectoryTextBox.Text.Trim();
        _settings.BitrateKbps = GetSelectedBitrate();
        _settingsService.Save(_settings);
    }

    private int GetSelectedBitrate() => BitrateComboBox.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var value) ? value : 192;

    private void SelectBitrate(int bitrate)
    {
        foreach (var value in BitrateComboBox.Items.OfType<ComboBoxItem>())
        {
            if (value.Tag?.ToString() == bitrate.ToString()) { BitrateComboBox.SelectedItem = value; return; }
        }
        BitrateComboBox.SelectedIndex = 1;
    }

    private static void MarkFailed(ConversionTaskItem task, string message)
    {
        task.State = ConversionState.Failed;
        task.StatusText = "失败";
        task.Detail = message;
        task.Progress = 0;
    }

    private static void MarkCancelled(ConversionTaskItem task)
    {
        task.State = ConversionState.Cancelled;
        task.StatusText = "已取消";
        task.Detail = "转换已取消";
        task.Progress = 0;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
