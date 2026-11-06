using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ExpoReturn.Desktop.ViewModels;

namespace ExpoReturn.Desktop.Views;

public partial class PalletListView : UserControl
{
    public PalletListView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is INotifyPropertyChanged old) old.PropertyChanged -= OnVmChanged;
            if (e.NewValue is INotifyPropertyChanged vm) vm.PropertyChanged += OnVmChanged;
            UpdateDqColumn();
        };
        // UX-B12: Ctrl+F lleva el foco a la búsqueda
        InputBindings.Add(new KeyBinding(new RelayUiCommand(() => { SearchBox.Focus(); SearchBox.SelectAll(); }), Key.F, ModifierKeys.Control));
        Loaded += (_, _) => Grid.Focus();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PalletListViewModel.HasLegacy)) UpdateDqColumn();
    }

    /// <summary>UX-B07: la columna "Calidad de datos" sólo aparece si hay registros históricos.</summary>
    private void UpdateDqColumn() =>
        DqColumn.Visibility = DataContext is PalletListViewModel { HasLegacy: true } ? Visibility.Visible : Visibility.Collapsed;

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is PalletListViewModel vm && Grid.SelectedItem is PalletRow row)
            vm.OpenCommand.Execute(row);
    }

    /// <summary>UX-B12: Enter abre el renglón seleccionado (en lugar de bajar al siguiente).</summary>
    private void OnGridKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not PalletListViewModel vm || Grid.SelectedItem is not PalletRow row) return;
        vm.OpenCommand.Execute(row);
        e.Handled = true;
    }
}

/// <summary>Comando simple para atajos de teclado propios de la vista.</summary>
public sealed class RelayUiCommand : ICommand
{
    private readonly Action _execute;
    public RelayUiCommand(Action execute) => _execute = execute;
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _execute();
}
