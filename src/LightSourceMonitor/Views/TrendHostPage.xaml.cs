using System.Windows;
using System.Windows.Controls;
using LightSourceMonitor.ViewModels;

namespace LightSourceMonitor.Views;

public partial class TrendHostPage : UserControl
{
    public TrendHostPage()
    {
        InitializeComponent();
    }

    private void PdTrendTab_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is TrendHostViewModel vm)
            vm.ActiveTrendTab = 0;
    }

    private void WmTrendTab_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is TrendHostViewModel vm)
            vm.ActiveTrendTab = 1;
    }
}
