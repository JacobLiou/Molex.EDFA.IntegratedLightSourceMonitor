using System.Windows.Controls;
using System.Windows;
using LightSourceMonitor.ViewModels;

namespace LightSourceMonitor.Views;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            EmailPasswordBox.Password = vm.SmtpPassword ?? string.Empty;
        }
    }

    private void EmailPasswordBox_OnPasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.SmtpPassword = EmailPasswordBox.Password;
        }
    }
}
