using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using ExpoReturn.Desktop.ViewModels;

namespace ExpoReturn.Desktop.Views;

public partial class UsersView : UserControl
{
    private bool _columnsAdded;

    public UsersView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AddValueStreamColumns();
    }

    /// <summary>One column per Value Stream of the catalog (a new VS appears without changing the screen).</summary>
    private void AddValueStreamColumns()
    {
        if (_columnsAdded || DataContext is not UsersViewModel vm) return;
        _columnsAdded = true;
        for (var i = 0; i < vm.MatrixValueStreams.Count; i++)
        {
            var template = (DataTemplate)XamlReader.Parse($@"
<DataTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
  <ComboBox ItemsSource=""{{Binding DataContext.CellOptions, RelativeSource={{RelativeSource AncestorType=UserControl}}}}""
            SelectedItem=""{{Binding Cells[{i}].Code, UpdateSourceTrigger=PropertyChanged}}"" IsEnabled=""{{Binding HasDepartment}}"" />
</DataTemplate>");
            AccessGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = $"VS{vm.MatrixValueStreams[i]}", Width = 105, MinWidth = 105, CellTemplate = template,
            });
        }
    }
}
