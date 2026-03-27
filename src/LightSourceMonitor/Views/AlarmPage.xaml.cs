using System.Windows.Controls;

namespace LightSourceMonitor.Views;

public partial class AlarmPage : UserControl
{
    public AlarmPage()
    {
        InitializeComponent();
    }

    private void SearchBar_OnSearchStarted(object sender, HandyControl.Data.FunctionEventArgs<string> e)
    {
        if (DataContext is ViewModels.AlarmViewModel vm)
        {
            vm.SearchText = e.Info;
        }
    }
}
