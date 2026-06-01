using System.Windows;
using AudioCompressor.UI.ViewModels;
using Microsoft.Win32;

namespace AudioCompressor.UI;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _chartInitialized;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.PlotRefreshVersion))
            RefreshChart();
    }

    private void RefreshChart()
    {
        Chart.Plot.Clear();

        if (_viewModel.ChartTimePoints.Count < 2)
        {
            try { Chart.Refresh(); } catch { }
            return;
        }

        if (!_chartInitialized)
        {
            Chart.Plot.Title("Real-Time Monitoring");
            Chart.Plot.XLabel("Time (seconds)");
            Chart.Plot.YLabel("Value");
            _chartInitialized = true;
        }

        var xs = _viewModel.ChartTimePoints.ToArray();
        var pValues = _viewModel.ChartProgressPoints.ToArray();
        var sValues = _viewModel.ChartSpeedPoints.ToArray();

        var sig1 = Chart.Plot.Add.Scatter(xs, pValues);
        sig1.Label = "Progress %";
        sig1.Color = ScottPlot.Color.FromHex("#1976D2");
        sig1.LineWidth = 2;

        var sig2 = Chart.Plot.Add.Scatter(xs, sValues);
        sig2.Label = "Speed (samples/sec)";
        sig2.Color = ScottPlot.Color.FromHex("#388E3C");
        sig2.LineWidth = 2;

        Chart.Plot.Legend.Alignment = ScottPlot.Alignment.UpperRight;
        Chart.Plot.Axes.AutoScale();

        try { Chart.Refresh(); } catch { }
    }

    private void DropZone_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
            if (files is { Length: > 0 } && files[0].EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                return;
            }
        }
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
            if (files is { Length: > 0 })
            {
                var path = files[0];
                if (!path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("Please drop a .WAV file.", "Invalid File",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _viewModel.LoadFile(path);
            }
        }
    }

    private void DropZone_Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open WAV file",
            Filter = "WAV files (*.wav)|*.wav|All files (*.*)|*.*"
        };
        var res = dlg.ShowDialog();
        if (res == true)
        {
            _viewModel.LoadFile(dlg.FileName);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Cleanup();
        base.OnClosed(e);
    }

    private void ProgressBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {

    }
}
