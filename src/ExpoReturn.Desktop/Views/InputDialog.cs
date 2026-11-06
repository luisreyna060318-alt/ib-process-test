using System.Windows;
using System.Windows.Controls;

namespace ExpoReturn.Desktop.Views;

/// <summary>Diálogo simple para pedir un texto obligatorio (comentario al atender una alerta).</summary>
public static class InputDialog
{
    public static string? Ask(string title, string prompt, Window? owner)
    {
        var box = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 90, Margin = new Thickness(0, 8, 0, 12),
                                VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var ok = new Button { Content = "Aceptar", IsDefault = false, Width = 100, Margin = new Thickness(8, 0, 0, 0) };
        var cancel = new Button { Content = "Cancelar", IsCancel = true, Width = 100 };
        if (System.Windows.Application.Current.TryFindResource("PrimaryButton") is Style p) ok.Style = p;
        if (System.Windows.Application.Current.TryFindResource("SecondaryButton") is Style s) cancel.Style = s;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(box);
        panel.Children.Add(buttons);

        var win = new Window
        {
            Title = title, Content = panel, Width = 480, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner, FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
        };
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(box.Text)) { box.Focus(); return; }
            win.DialogResult = true;
        };
        win.Loaded += (_, _) => box.Focus();
        return win.ShowDialog() == true ? box.Text : null;
    }
}
