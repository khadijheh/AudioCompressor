using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using AudioCompressor.Core;
using AudioCompressor.Core.Logging;
using AudioCompressor.Core.Models;
using AudioCompressor.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace AudioCompressor.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IWavService _wavService = new WavService();
    private readonly NAudioPlaybackService _playback_service = new();
    private readonly AsyncLogger _logger = new();
    private readonly CompressionEngine _engine;
    private WavFileInfo? _currentFileInfo;
    private string? _currentFilePath;
    private CancellationTokenSource? _cts;
    private Stopwatch _compressSw = new();
    private int _totalSamples;


    public MainViewModel()
    {
        _engine = new CompressionEngine(_wavService, _logger);
        _logger.OnLog += msg => OnLogMessage(msg);
        CompressedFiles = new ObservableCollection<string>();
        // load existing compressed files on startup
        RefreshCompressedFiles();
    }

    [ObservableProperty] private bool _isFileLoaded;
    [ObservableProperty] private string _statusMessage = "Drop a .WAV file to begin";
    [ObservableProperty] private string _windowTitle = "Audio Compressor";
    [ObservableProperty] private string _dropZoneText = "Drop a .WAV file here";
    [ObservableProperty] private string _fileName = "-";
    [ObservableProperty] private string _fileSize = "-";
    [ObservableProperty] private string _duration = "-";
    [ObservableProperty] private string _sampleRate = "-";
    [ObservableProperty] private string _channels = "-";
    [ObservableProperty] private string _bitRate = "-";
    [ObservableProperty] private string _encoding = "-";
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private string _originalBitsLabel = "16 bits / sample";

    [ObservableProperty] private AlgorithmType _selectedAlgorithm = AlgorithmType.NonlinearQuantization;

    public List<DisplayItem<AlgorithmType>> AlgorithmOptions { get; } = new()
    {
        new("Nonlinear Quantization", AlgorithmType.NonlinearQuantization),
        new("DPCM", AlgorithmType.DPCM),
        new("Delta Modulation", AlgorithmType.DeltaModulation),
        new("ADPCM", AlgorithmType.ADPCM),
        new("TransformCodingDCT", AlgorithmType.TransformCodingDCT),
        new("AdaptiveDeltaModulation", AlgorithmType.AdaptiveDeltaModulation)


    };

    public DisplayItem<AlgorithmType> SelectedAlgorithmItem
    {
        get => AlgorithmOptions.First(a => a.Value == SelectedAlgorithm);
        set
        {
            if (value is not null)
                SelectedAlgorithm = value.Value;
        }
    }

    public List<DisplayItem<MuLawType>> LawOptions { get; } = new()
    {
        new("μ-law", MuLawType.MuLaw),
        new("A-law", MuLawType.ALaw)
    };

    public DisplayItem<MuLawType> SelectedLawItem
    {
        get => LawOptions.First(a => a.Value == SelectedLawType);
        set
        {
            if (value is not null)
                SelectedLawType = value.Value;
        }
    }
    [ObservableProperty] private int _targetBits = 8;
    [ObservableProperty] private double _stepSize = 0.02;
    [ObservableProperty] private bool _useAdaptiveDelta;
    [ObservableProperty] private MuLawType _selectedLawType = MuLawType.MuLaw;
    [ObservableProperty] private int _predictorOrder = 1;
    [ObservableProperty] private bool _canCompress;

    // New advanced settings exposed to UI
    [ObservableProperty] private double _muLawMu =255.0;
    [ObservableProperty] private double _aLawA =87.6;

    // New: target sample rate for resampling (0 = keep original)
    [ObservableProperty] private int _targetSampleRate =0;

    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private bool _isCompressing;
    [ObservableProperty] private string _compressionStatus = "";
    [ObservableProperty] private string _speedDisplay = "";
    [ObservableProperty] private string _ratioDisplay = "";

    // New properties for real-time stats
    [ObservableProperty] private double _currentSpeed;
    [ObservableProperty] private double _currentCompressionPercent;

    [ObservableProperty] private int _plotRefreshVersion;
    [ObservableProperty] private string _snrResult = "SNR: N/A";

    public List<double> ChartTimePoints { get; } = new();
    public List<double> ChartProgressPoints { get; } = new();
    public List<double> ChartSpeedPoints { get; } = new();

    public ObservableCollection<string> LogMessages { get; } = new();

    // New: compressed files management
    public ObservableCollection<string> CompressedFiles { get; }
    [ObservableProperty] private string? _selectedCompressedFile;

    // New: tracks whether a compressed file is selected
    [ObservableProperty] private bool _hasSelectedCompressedFile;
    [ObservableProperty] private bool _hasCompressedFiles;

    private string? _lastCompressedFilePath;

    private void OnLogMessage(string msg)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            LogMessages.Add(msg);
            if (LogMessages.Count > 500)
                LogMessages.RemoveAt(0);
        });
    }

    public void LoadFile(string path)
    {
        try
        {
            _currentFilePath = path;
            _currentFileInfo = _wavService.ReadFileInfo(path);
            FileName = Path.GetFileName(path);
            FileSize = FormatFileSize(_currentFileInfo.FileSize);
            Duration = _currentFileInfo.Duration.TotalHours >= 1
                ? _currentFileInfo.Duration.ToString(@"hh\:mm\:ss\.fff")
                : _currentFileInfo.Duration.ToString(@"mm\:ss\.fff");
            SampleRate = $"{_currentFileInfo.SampleRate:N0} Hz";
            Channels = _currentFileInfo.Channels switch
            {
                1 => "1 (Mono)",
                2 => "2 (Stereo)",
                _ => $"{_currentFileInfo.Channels} channels"
            };
            BitRate = FormatBitRate(_currentFileInfo.BitRate);
            Encoding = _currentFileInfo.Encoding;
            OriginalBitsLabel = $"{_currentFileInfo.BitsPerSample} bits / sample";
            WindowTitle = $"Audio Compressor - {FileName}";
            DropZoneText = FileName;
            IsFileLoaded = true;
            CanCompress = true;
            StatusMessage = "File loaded successfully";

            // refresh compressed files list relative to this file
            RefreshCompressedFiles();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            ResetInternal();
        }
    }

    [RelayCommand]
    private void Play()
    {
        if (!IsFileLoaded || _currentFilePath == null) return;
        try
        {
            _playback_service.Play(_currentFilePath);
            IsPlaying = true;
            StatusMessage = "Playing...";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Playback error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Stop()
    {
        _playback_service.Stop();
        IsPlaying = false;
        StatusMessage = IsFileLoaded ? "File loaded" : "Stopped";
    }

    [RelayCommand]
    private void CancelCompress()
    {
        _cts?.Cancel();
        IsCompressing = false;
        StatusMessage = "Cancelled by user";
    }

    private string GetCompressedDir()
    {
        if (!string.IsNullOrEmpty(_currentFilePath))
        {
            var dir = Path.Combine(Path.GetDirectoryName(_currentFilePath) ?? ".", "compressed_output");
            return dir;
        }
        return Path.Combine(Environment.CurrentDirectory, "compressed_output");
    }

    public void RefreshCompressedFiles()
    {
        var dir = GetCompressedDir();
        var previousSelection = SelectedCompressedFile;
        CompressedFiles.Clear();
        if (Directory.Exists(dir))
        {
            var files = Directory.GetFiles(dir, "*.comp").OrderByDescending(f => File.GetLastWriteTime(f)).ToList();
            foreach (var f in files)
                CompressedFiles.Add(f);

            if (CompressedFiles.Count > 0)
            {
                if (string.IsNullOrEmpty(previousSelection) || !CompressedFiles.Contains(previousSelection))
                {
                    SelectedCompressedFile = CompressedFiles.First();
                }
            }
            else
            {
                SelectedCompressedFile = null;
            }
        }
        else
        {
            SelectedCompressedFile = null;
        }

        HasCompressedFiles = CompressedFiles.Count > 0;
    }

    [RelayCommand]
    private async Task DoCompress()
    {
        if (_currentFilePath == null || _currentFileInfo == null) return;

        string fileToCompress = _currentFilePath;
        string? tempResampled = null;

        // If user requested a target sample rate different than original, resample first
        if (TargetSampleRate >0 && _currentFileInfo != null && TargetSampleRate != _currentFileInfo.SampleRate)
        {
            try
            {
                var tmp = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(_currentFilePath) + $"_resampled_{TargetSampleRate}.wav");
                _wavService.ResampleFile(_currentFilePath, tmp, TargetSampleRate);
                tempResampled = tmp;
                fileToCompress = tmp;
                // update info to resampled file
                _currentFileInfo = _wavService.ReadFileInfo(fileToCompress);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Resample error: {ex.Message}";
                return;
            }
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var config = new CompressionConfig
        {
            Algorithm = SelectedAlgorithm,
            TargetBitsPerSample = TargetBits,
            StepSize = StepSize,
            UseAdaptiveDelta = UseAdaptiveDelta,
            LawType = SelectedLawType,
            PredictorOrder = PredictorOrder,
            MuLawMu = _muLawMu,
            ALawA = _aLawA
        };

        _totalSamples = _currentFileInfo.DataSize / (_currentFileInfo.BitsPerSample /8);
        ChartTimePoints.Clear();
        ChartProgressPoints.Clear();
        ChartSpeedPoints.Clear();
        LogMessages.Clear();
        _compressSw.Restart();

        IsCompressing = true;
        CanCompress = false;
        ProgressValue =0;
        CompressionStatus = "Initializing...";
        SpeedDisplay = "";
        RatioDisplay = "";

        var progress = new Progress<double>(value =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                ProgressValue = value;
                CompressionStatus = $"{value *100:F0}%";
                var elapsed = _compressSw.Elapsed.TotalSeconds;
                var processed = (int)(value * _totalSamples);
                var speed = elapsed >0 ? processed / elapsed :0;

                _currentSpeed = speed; // backing field
                _currentCompressionPercent = value *100; // backing field


                ChartTimePoints.Add(elapsed);
                ChartProgressPoints.Add(value *100);
                ChartSpeedPoints.Add(speed);
                PlotRefreshVersion++;

                SpeedDisplay = $"{speed:N0} samples/sec";
                RatioDisplay = $"{value *100:F0}% complete";
            });
        });

        try
        {
            var result = await Task.Run(() => _engine.Compress(fileToCompress, config, token, progress,
                msg => { }), token);

            if (!token.IsCancellationRequested)
            {
                SnrResult = $"SNR: {result.SNR:F2} dB";
                CompressionStatus = "Complete!";
                StatusMessage = $"Compression done: {result.CompressionRatio:F2}x, saved {FormatFileSize(result.CompressedDataSize)}. Decompressed WAV at {result.OutputPath}";
                RatioDisplay = $"Ratio: {result.CompressionRatio:F2}x, Saved: {result.SavingsPercent:F1}%";
                SpeedDisplay = $"{_totalSamples / result.Elapsed.TotalSeconds:N0} samples/sec";
                ProgressValue = 1.0;

                _lastCompressedFilePath = result.CompressedFilePath;

                var reportVm = new ReportViewModel(result);
                var reportWnd = new Views.ReportWindow { DataContext = reportVm };
                reportWnd.ShowDialog();

                // refresh list of compressed files and keep the latest file selected
                _lastCompressedFilePath = result.CompressedFilePath;
                RefreshCompressedFiles();
                SelectedCompressedFile = _lastCompressedFilePath;
            }
        }
        catch (OperationCanceledException)
        {
            CompressionStatus = "Cancelled";
            StatusMessage = "Compression was cancelled";
        }
        catch (Exception ex)
        {
            CompressionStatus = "Error";
            StatusMessage = $"Compression error: {ex.Message}";
        }
        finally
        {
            IsCompressing = false;
            CanCompress = IsFileLoaded;
            _cts.Dispose();
            _cts = null;

            // clean up temporary resampled file if created
            if (!string.IsNullOrEmpty(tempResampled) && File.Exists(tempResampled))
            {
                try { File.Delete(tempResampled); } catch { }
            }
        }

    }

    [RelayCommand]
    private void RefreshCompressedFilesCommand()
    {
        RefreshCompressedFiles();
    }

    private string? ShowSaveFileDialog(string suggestedFileName)
    {
        try
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = suggestedFileName,
                DefaultExt = ".wav",
                Filter = "WAV files (*.wav)|*.wav|All files (*.*)|*.*"
            };
            var res = dlg.ShowDialog();
            if (res == true)
                return dlg.FileName;
        }
        catch
        {
            // ignore in non-UI/test contexts
        }
        return null;
    }

    public void Cleanup()
    {
        _cts?.Cancel();
        _playback_service.Dispose();
        _logger.Dispose();
    }

    private void ResetInternal()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _playback_service.Stop();
        _currentFilePath = null;
        _currentFileInfo = null;
        IsFileLoaded = false;
        IsPlaying = false;
        CanCompress = false;
        IsCompressing = false;
        ProgressValue = 0;
        WindowTitle = "Audio Compressor";
        DropZoneText = "Drop a .WAV file here";
        FileName = "-";
        FileSize = "-";
        Duration = "-";
        SampleRate = "-";
        Channels = "-";
        BitRate = "-";
        Encoding = "-";
        OriginalBitsLabel = "16 bits / sample";
        SelectedAlgorithm = AlgorithmType.NonlinearQuantization;
        SelectedLawType = MuLawType.MuLaw;
        TargetBits = 8;
        StepSize = 0.02;
        UseAdaptiveDelta = false;
        PredictorOrder = 1;
        TargetSampleRate = 0;
        MuLawMu = 255.0;
        ALawA = 87.6;
        CompressionStatus = "";
        SpeedDisplay = "";
        RatioDisplay = "";
        SelectedCompressedFile = null;
        _lastCompressedFilePath = null;
        CompressedFiles.Clear();
        HasCompressedFiles = false;
        ChartTimePoints.Clear();
        ChartProgressPoints.Clear();
        ChartSpeedPoints.Clear();
        LogMessages.Clear();
        PlotRefreshVersion++;
        StatusMessage = "Drop a .WAV file to begin";
    }

    private void ResetLoadedFileState()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _playback_service.Stop();
        IsPlaying = false;
        IsCompressing = false;
        CanCompress = IsFileLoaded;
        ProgressValue = 0;
        CompressionStatus = "";
        SpeedDisplay = "";
        RatioDisplay = "";
        SelectedCompressedFile = null;
        _lastCompressedFilePath = null;
        CompressedFiles.Clear();
        HasCompressedFiles = false;
        ChartTimePoints.Clear();
        ChartProgressPoints.Clear();
        ChartSpeedPoints.Clear();
        LogMessages.Clear();
        PlotRefreshVersion++;
        StatusMessage = "File loaded";
    }

    private static string FormatFileSize(long bytes)
    {
        return bytes switch
        {
            <1024 => $"{bytes} bytes",
            <1024 *1024 => $"{bytes /1024.0:F1} KB",
            _ => $"{bytes / (1024.0 *1024.0):F2} MB"
        };
    }

    private static string FormatBitRate(int bps)
    {
        if (bps >=1_000_000) return $"{bps /1_000_000.0:F2} Mbps";
        if (bps >=1000) return $"{bps /1000.0:F1} kbps";
        return $"{bps} bps";
    }

    partial void OnSelectedCompressedFileChanged(string? value)
    {
        HasSelectedCompressedFile = !string.IsNullOrEmpty(value);
    }

    [RelayCommand]
    private void RemoveFile()
    {
        _cts?.Cancel();
        _playback_service.Stop();
        ResetInternal();
        StatusMessage = "Audio file removed.";
    }

    [RelayCommand]
    private void Reset()
    {
        _cts?.Cancel();
        _playback_service.Stop();
        if (IsFileLoaded)
        {
            ResetLoadedFileState();
        }
        else
        {
            ResetInternal();
        }
        StatusMessage = "Reset complete.";
    }

    [RelayCommand]
    private void OpenCompressedFolder()
    {
        var dir = GetCompressedDir();
        if (!Directory.Exists(dir))
        {
            StatusMessage = "No compressed_output folder found.";
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true, Verb = "open" });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unable to open folder: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ShowCompressedMetadata()
    {
        if (string.IsNullOrEmpty(SelectedCompressedFile)) return;
        try
        {
            var meta = _engine.ReadCompressedMetadata(SelectedCompressedFile);
            // Build a short info string and show in StatusMessage or log
            StatusMessage = $"{Path.GetFileName(SelectedCompressedFile)} — size: {FormatFileSize(meta.CompressedDataSize)}, samples: {meta.OriginalSampleCount}, algo: {meta.Config.Algorithm}, bits: {meta.Config.TargetBitsPerSample}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Read metadata failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void DeleteCompressed()
    {
        if (string.IsNullOrEmpty(SelectedCompressedFile)) return;

        try
        {
            File.Delete(SelectedCompressedFile);
            if (string.Equals(_lastCompressedFilePath, SelectedCompressedFile, StringComparison.OrdinalIgnoreCase))
            {
                _lastCompressedFilePath = null;
            }
            StatusMessage = "Deleted compressed file.";
            RefreshCompressedFiles();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Delete failed: {ex.Message}";
        }
    }
}
