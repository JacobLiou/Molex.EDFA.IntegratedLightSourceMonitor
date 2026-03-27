using System.Windows;
using LightSourceMonitor.ViewModels;

namespace LightSourceMonitor.Views;

public partial class MainWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += (_, _) => viewModel.NavigateTo(0);
    }
}
