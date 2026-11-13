using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ExpoReturn.Desktop.ViewModels;

namespace ExpoReturn.Desktop;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    /// <summary>
    /// Clicking the menu entry that is already selected goes back to that screen (e.g. "Returns inbox" while a return
    /// is open): the selection does not change, so the view model is told directly.
    /// </summary>
    private void OnNavClick(object sender, MouseButtonEventArgs e)
    {
        var d = e.OriginalSource as DependencyObject;
        while (d is not null and not ListBoxItem) d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        if (d is ListBoxItem { DataContext: NavItem item } && DataContext is ShellViewModel vm) vm.NavClicked(item);
    }
}
