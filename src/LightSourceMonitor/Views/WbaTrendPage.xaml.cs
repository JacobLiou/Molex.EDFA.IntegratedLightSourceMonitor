using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LightSourceMonitor.ViewModels;

namespace LightSourceMonitor.Views;

public partial class WbaTrendPage : UserControl
{
    private INotifyPropertyChanged? _vmNotify;

    public WbaTrendPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnIsVisibleChanged;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RequestWbaChartRemeasure();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachVmPropertyChanged();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachVmPropertyChanged();
        if (e.NewValue is INotifyPropertyChanged n)
        {
            _vmNotify = n;
            n.PropertyChanged += OnVmPropertyChanged;
        }
    }

    private void DetachVmPropertyChanged()
    {
        if (_vmNotify != null)
        {
            _vmNotify.PropertyChanged -= OnVmPropertyChanged;
            _vmNotify = null;
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WbaTrendViewModel.Series))
            RequestWbaChartRemeasure();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
            RequestWbaChartRemeasure();
    }

    private void RequestWbaChartRemeasure()
    {
        Dispatcher.BeginInvoke(() =>
        {
            WbaCartesianChart.UpdateLayout();
            InvalidateMeasure();
            InvalidateArrange();
            WbaCartesianChart.InvalidateMeasure();
            WbaCartesianChart.InvalidateArrange();
            WbaCartesianChart.UpdateLayout();
        }, DispatcherPriority.Loaded);
    }

    private void ExportPng_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG Image|*.png",
            DefaultExt = ".png",
            FileName = $"trend_wba_{DateTime.Now:yyyyMMdd_HHmmss}.png"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var target = ChartContainer;
            var dpi = 96.0 * 2;
            var size = target.RenderSize;
            var rtb = new RenderTargetBitmap(
                (int)(size.Width * 2), (int)(size.Height * 2),
                dpi, dpi, PixelFormats.Pbgra32);

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x23, 0x23, 0x3D)),
                    null, new Rect(0, 0, size.Width, size.Height));
            }

            rtb.Render(dv);
            rtb.Render(target);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));

            using var fs = new FileStream(dialog.FileName, FileMode.Create);
            encoder.Save(fs);

            if (DataContext is WbaTrendViewModel vm)
                vm.StatusText = $"PNG: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            if (DataContext is WbaTrendViewModel vm)
                vm.StatusText = $"PNG export failed: {ex.Message}";
        }
    }
}
