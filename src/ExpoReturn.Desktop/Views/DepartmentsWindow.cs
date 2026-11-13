using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ExpoReturn.Application;
using ExpoReturn.Domain;

namespace ExpoReturn.Desktop.Views;

public sealed partial class DepartmentRow : ObservableObject
{
    public DepartmentRow(Department d) { Source = d; _canRequest = d.CanRequest; _isActive = d.IsActive; }
    public Department Source { get; }
    public string Code => Source.Code;
    public string Area => Source.Area;
    public string Edits => Department.Areas.FirstOrDefault(a => a.Area == Source.Area).Edits ?? "read only";
    public string Group => Source.Group == Department.CrossFunctional ? "Cross-functional" : "Process";
    [ObservableProperty] private bool _canRequest;
    [ObservableProperty] private bool _isActive;
    public bool Changed => CanRequest != Source.CanRequest || IsActive != Source.IsActive;
}

/// <summary>
/// Owner only: departments of IB_PROCESS_DEPARTMENTS. Add a new one, or change "Active" / "Can request" of an existing one.
/// Nothing is deleted: a department that is no longer used is deactivated, and the records that have it stay valid.
/// </summary>
public sealed class DepartmentsWindow : Window
{
    private readonly AppServices _services;
    private readonly UserContext _owner;
    private readonly ObservableCollection<DepartmentRow> _rows = new();
    private readonly TextBox _code = new() { Width = 130, CharacterCasing = CharacterCasing.Upper, MaxLength = 20 };
    private readonly ComboBox _area = new() { Width = 300, DisplayMemberPath = "Label", SelectedValuePath = "Value" };
    private readonly ComboBox _group = new() { Width = 150, DisplayMemberPath = "Label", SelectedValuePath = "Value" };
    private readonly CheckBox _canRequest = new() { Content = "Can request returns", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

    public DepartmentsWindow(AppServices services, UserContext owner)
    {
        _services = services;
        _owner = owner;
        Title = "Departments";
        Width = 820; Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI");
        if (TryFindResource("AppBackground") is Brush bg) Background = bg;

        _area.ItemsSource = Department.Areas.Select(a => new ViewModels.LookupItem(a.Area, $"{a.Area}  ·  edits: {a.Edits}")).ToList();
        _group.ItemsSource = new[] { new ViewModels.LookupItem(Department.Process, "Process"), new ViewModels.LookupItem(Department.CrossFunctional, "Cross-functional") };
        _group.SelectedIndex = 0;

        var grid = new DataGrid { ItemsSource = _rows, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, RowHeight = 28 };
        grid.Columns.Add(new DataGridTextColumn { Header = "Department", Binding = new Binding(nameof(DepartmentRow.Code)), IsReadOnly = true, Width = 130 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Area", Binding = new Binding(nameof(DepartmentRow.Area)), IsReadOnly = true, Width = 70 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Champion / Proxy edits", Binding = new Binding(nameof(DepartmentRow.Edits)), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Group", Binding = new Binding(nameof(DepartmentRow.Group)), IsReadOnly = true, Width = 120 });
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Can request", Binding = new Binding(nameof(DepartmentRow.CanRequest)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 95 });
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Active", Binding = new Binding(nameof(DepartmentRow.IsActive)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 65 });

        var add = Button("Add department", "PrimaryButton", (_, _) => Add());
        var save = Button("Save changes", "PrimaryButton", (_, _) => Save());
        var close = Button("Close", "SecondaryButton", (_, _) => Close());
        close.IsCancel = true;

        var form = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var (label, control) in new (string, FrameworkElement)[] { ("Code", _code), ("Area", _area), ("Group", _group) })
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
            cell.Children.Add(new TextBlock { Text = label, FontSize = 12, Margin = new Thickness(0, 0, 0, 2) });
            cell.Children.Add(control);
            form.Children.Add(cell);
        }
        _canRequest.Margin = new Thickness(0, 16, 10, 0);
        form.Children.Add(_canRequest);
        add.VerticalAlignment = VerticalAlignment.Bottom;
        form.Children.Add(add);

        var top = new StackPanel();
        top.Children.Add(new TextBlock { Text = "New department", FontWeight = FontWeights.SemiBold, FontSize = 15 });
        top.Children.Add(new TextBlock
        {
            Text = "The area decides which fields the Champions / Proxies of the department edit; code and area cannot be changed afterwards.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .75,
        });
        top.Children.Add(form);
        top.Children.Add(new TextBlock { Text = "Existing departments", FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 16, 0, 2) });
        top.Children.Add(new TextBlock
        {
            Text = "Departments are never deleted. Untick Active to stop offering one (records and users that have it keep it); " +
                   "Can request = offered as Requesting Department of a return.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .75, Margin = new Thickness(0, 0, 0, 6),
        });

        var bottom = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(save);
        buttons.Children.Add(close);
        bottom.Children.Add(buttons);
        bottom.Children.Add(_message);

        var dock = new DockPanel { Margin = new Thickness(18) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        dock.Children.Add(top);
        dock.Children.Add(bottom);
        dock.Children.Add(grid);
        Content = dock;
        Load();
    }

    private Button Button(string text, string style, RoutedEventHandler click)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0), MinWidth = 120 };
        if (TryFindResource(style) is Style s) b.Style = s;
        b.Click += click;
        return b;
    }

    private void Load()
    {
        _rows.Clear();
        foreach (var d in _services.Departments.All().OrderByDescending(d => d.IsActive).ThenBy(d => d.Group == Department.CrossFunctional).ThenBy(d => d.Code))
            _rows.Add(new DepartmentRow(d));
    }

    private void Add()
    {
        var code = _code.Text.Trim().ToUpperInvariant();
        var area = _area.SelectedValue as string;
        var group = _group.SelectedValue as string;
        if (Department.Validate(code, area, group) is { } problem) { Say(problem, true); return; }
        if (_rows.Any(r => r.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
        { Say($"{code} already exists. If it is inactive, tick Active in the list and save.", true); return; }
        var d = new Department(code, area!, _canRequest.IsChecked == true, group!, true);
        try
        {
            _services.Departments.Add(d);
            _services.Audit.Record(new[] { new AuditEntry(0, "DEPARTMENTS", "Department", null,
                $"{d.Code} · area {d.Area} · {(d.CanRequest ? "can request" : "cannot request")} · {d.Group}", _owner.NtUser, _services.Clock.UtcNow, "Added") });
            _services.RefreshCatalogs();
        }
        catch (Exception ex) { Say($"Not saved: {ConnectionRetry.Message(ex)}", true); return; }
        _code.Text = "";
        Load();
        Say($"{d.Code} added. It is offered in the application from now on.", false);
    }

    private void Save()
    {
        var changed = _rows.Where(r => r.Changed).ToList();
        if (changed.Count == 0) { Say("No changes to save.", false); return; }
        try
        {
            var now = _services.Clock.UtcNow;
            foreach (var r in changed)
            {
                _services.Departments.Update(r.Code, r.CanRequest, r.IsActive);
                static string Text(bool req, bool act) => $"{(act ? "active" : "inactive")}, {(req ? "can request" : "cannot request")}";
                _services.Audit.Record(new[] { new AuditEntry(0, "DEPARTMENTS", "Department", $"{r.Code}: {Text(r.Source.CanRequest, r.Source.IsActive)}",
                    $"{r.Code}: {Text(r.CanRequest, r.IsActive)}", _owner.NtUser, now, "Changed") });
            }
            _services.RefreshCatalogs();
        }
        catch (Exception ex) { Load(); Say($"Not saved: {ConnectionRetry.Message(ex)}", true); return; }
        Load();
        Say($"{changed.Count} department(s) updated.", false);
    }

    private void Say(string text, bool error)
    {
        _message.Text = text;
        _message.Foreground = error ? Brushes.Firebrick : Brushes.DarkGreen;
    }
}
