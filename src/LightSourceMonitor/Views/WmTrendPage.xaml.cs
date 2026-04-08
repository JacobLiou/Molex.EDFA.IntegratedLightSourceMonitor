using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LightSourceMonitor.ViewModels;

namespace LightSourceMonitor.Views;

public partial class WmTrendPage : UserControl
{
    public WmTrendPage()
    {
        InitializeComponent();
    }

    private void ExportPng_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG Image|*.png",
            DefaultExt = ".png",
            FileName = $"trend_wm_{DateTime.Now:yyyyMMdd_HHmmss}.png"
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

            if (DataContext is WmTrendViewModel vm)
                vm.StatusText = $"已导出 PNG: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            if (DataContext is WmTrendViewModel vm)
                vm.StatusText = $"PNG 导出失败: {ex.Message}";
        }
    }
}
