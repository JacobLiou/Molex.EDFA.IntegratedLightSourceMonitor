using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LightSourceMonitor.ViewModels;

namespace LightSourceMonitor.Views;

public partial class TrendPage : UserControl
{
    public TrendPage()
    {
        InitializeComponent();
    }

    private void ExportPng_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TrendViewModel vm) return;

        if (vm.SelectedTrendSubTabIndex == 2)
        {
            vm.StatusText = "WBA 趋势未实现，无法导出 PNG";
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG Image|*.png",
            DefaultExt = ".png",
            FileName = vm.SelectedTrendSubTabIndex == 0
                ? $"trend_pd_{DateTime.Now:yyyyMMdd_HHmmss}.png"
                : $"trend_wm_{DateTime.Now:yyyyMMdd_HHmmss}.png"
        };

        if (dialog.ShowDialog() != true) return;

        var target = vm.SelectedTrendSubTabIndex == 0
            ? (FrameworkElement)PdChartHost
            : WmChartsHost;

        try
        {
            target.UpdateLayout();
            var dpi = 96.0 * 2;
            var w = target.ActualWidth;
            var h = target.ActualHeight;
            if (w <= 0 || h <= 0)
            {
                vm.StatusText = "PNG 导出失败: 图表区域尺寸无效，请先切换到对应 Tab 并等待布局完成";
                return;
            }

            var rtb = new RenderTargetBitmap(
                (int)(w * 2), (int)(h * 2),
                dpi, dpi, PixelFormats.Pbgra32);

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x23, 0x23, 0x3D)),
                    null, new Rect(0, 0, w, h));
            }

            rtb.Render(dv);
            rtb.Render(target);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));

            using var fs = new FileStream(dialog.FileName, FileMode.Create);
            encoder.Save(fs);

            vm.StatusText = $"已导出 PNG: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            vm.StatusText = $"PNG 导出失败: {ex.Message}";
        }
    }
}
