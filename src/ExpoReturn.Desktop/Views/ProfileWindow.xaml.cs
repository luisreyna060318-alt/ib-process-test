using System.Windows;
using ExpoReturn.Desktop.ViewModels;

namespace ExpoReturn.Desktop.Views;

public partial class ProfileWindow : Window
{
    public ProfileWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ProfileViewModel vm) vm.Close = ok => { DialogResult = ok; };
        };
    }
}
