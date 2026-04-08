using System.Windows;
using LightSourceMonitor.Services.Localization;
using LightSourceMonitor.ViewModels;
using HcMessageBox = HandyControl.Controls.MessageBox;

namespace LightSourceMonitor.Views;

public partial class MainWindow
{
    private readonly ILanguageService _language;

    public MainWindow(MainViewModel viewModel, ILanguageService language)
    {
        _language = language;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += (_, _) => viewModel.NavigateTo(0);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        var title = _language.GetString("Msg_ExitConfirmTitle");
        var body = _language.GetString("Msg_ExitConfirmBody");
        var result = HcMessageBox.Ask(body, title);

        if (result != MessageBoxResult.OK)
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }
}
