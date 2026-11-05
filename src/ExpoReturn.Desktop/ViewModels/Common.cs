using CommunityToolkit.Mvvm.ComponentModel;
using ExpoReturn.Application;

namespace ExpoReturn.Desktop.ViewModels;

/// <summary>Elemento de lista para ComboBox (valor + texto).</summary>
public sealed record LookupItem(object? Value, string Label)
{
    public override string ToString() => Label;
}

public abstract partial class PageViewModel : ObservableObject
{
    protected PageViewModel(AppServices services, UserContext user) { Services = services; _user = user; }

    protected AppServices Services { get; }
    public abstract string Title { get; }

    [ObservableProperty] private UserContext _user;

    partial void OnUserChanged(UserContext value) => UserChanged();

    protected virtual void UserChanged() { }
    public virtual void Refresh() { }
}

/// <summary>Indexador para enlazar IsEnabled="{Binding Perm[NombreCampo]}".</summary>
public sealed class PermissionMap
{
    private readonly Func<string, bool> _canEdit;
    public PermissionMap(Func<string, bool> canEdit) => _canEdit = canEdit;
    public bool this[string field] => _canEdit(field);
}
