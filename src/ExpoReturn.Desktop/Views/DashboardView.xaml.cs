using System.Windows.Controls;
using System.Windows.Input;
using ExpoReturn.Desktop.ViewModels;

namespace ExpoReturn.Desktop.Views;

public partial class DashboardView : UserControl
{
    public DashboardView() => InitializeComponent();

    /// <summary>UX-B13: doble clic en un VS abre la bandeja filtrada.</summary>
    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is DashboardViewModel vm) vm.OpenRowCommand.Execute(Grid.SelectedItem);
    }
}
