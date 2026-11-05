using System.Windows.Controls;
using System.Windows.Input;
using ExpoReturn.Desktop.ViewModels;

namespace ExpoReturn.Desktop.Views;

public partial class PalletListView : UserControl
{
    public PalletListView() => InitializeComponent();

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is PalletListViewModel vm && Grid.SelectedItem is PalletRow row)
            vm.OpenCommand.Execute(row);
    }
}
