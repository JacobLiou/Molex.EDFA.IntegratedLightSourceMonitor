using System.Windows;
using LightSourceMonitor.ViewModels;
using HcMessageBox = HandyControl.Controls.MessageBox;

namespace LightSourceMonitor.Views;

public partial class MainWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += (_, _) => viewModel.NavigateTo(0);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        var result = HcMessageBox.Ask(
            "确定要退出集成光源监控系统吗？\n退出后将停止数据采集和告警监测。",
            "确认退出");

        if (result != MessageBoxResult.OK)
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }
}
